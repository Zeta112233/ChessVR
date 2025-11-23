using System.Collections.Generic;
using UnityEngine;

public class GoGameManager : MonoBehaviour
{
    // 方便 VR 脚本访问
    public static GoGameManager Instance { get; private set; }

    [Header("棋盘格点（由 GoGridGenerator 注册）")]
    public List<Transform> gridPoints = new List<Transform>();

    [Header("是否使用 VR（勾上则关闭鼠标控制，仅靠 VR 抓取）")]
    public bool useVR = true;

    // 鼠标模式用
    private Transform grabbedPiece;
    private Camera cam;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        cam = Camera.main;
    }

    void Update()
    {
        // 勾选 useVR 时，不再走鼠标逻辑
        if (useVR)
            return;

        HandleMousePickAndDrop();

        if (grabbedPiece != null)
            DragPieceWithMouse();
    }

    /// <summary>
    /// 由 GoGridGenerator 调用，把生成好的格点列表注册进来
    /// </summary>
    public void RegisterGridPoints(List<Transform> points)
    {
        gridPoints = points;
    }

    #region 鼠标模式（原始逻辑）

    void HandleMousePickAndDrop()
    {
        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                if (hit.collider.CompareTag("Pieces"))
                {
                    grabbedPiece = hit.collider.transform;
                }
            }
        }

        if (Input.GetMouseButtonUp(0) && grabbedPiece != null)
        {
            // 用棋子当前位置作为参考点吸附
            SnapPieceToClosestGridPoint(grabbedPiece, grabbedPiece.position);
            grabbedPiece = null;
        }
    }

    void DragPieceWithMouse()
    {
        Plane boardPlane = new Plane(Vector3.up, Vector3.zero);
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);

        if (boardPlane.Raycast(ray, out float enter))
        {
            Vector3 hitPoint = ray.GetPoint(enter);
            hitPoint.y = grabbedPiece.position.y;
            grabbedPiece.position = hitPoint;
        }
    }

    #endregion

    #region 吸附逻辑（鼠标 & VR 共用）

    /// <summary>
    /// 默认用棋子当前位置作为参考点（给 VR 调用）
    /// </summary>
    public void SnapPieceToClosestGridPoint(Transform piece)
    {
        if (piece == null) return;
        SnapPieceToClosestGridPoint(piece, piece.position);
    }

    /// <summary>
    /// 按给定参考点寻找最近格点，并把棋子吸附过去
    /// </summary>
    public void SnapPieceToClosestGridPoint(Transform piece, Vector3 referencePos)
    {
        if (piece == null) return;
        if (gridPoints == null || gridPoints.Count == 0) return;

        Transform closest = FindClosestGridPoint(referencePos);
        if (closest == null) return;

        // 为了防止棋子嵌进木板，按 Collider 半高把它往上抬一点
        float yOffset = 0f;
        var col = piece.GetComponent<Collider>();
        if (col != null)
        {
            // bounds.extents.y = 棋子在世界空间的“半高”，已经包含缩放
            yOffset = col.bounds.extents.y;
        }

        Vector3 targetPos = closest.position;
        targetPos.y += yOffset;

        piece.position = targetPos;
    }

    public Transform FindClosestGridPoint(Vector3 pos)
    {
        Transform closest = null;
        float minDist = float.MaxValue;

        foreach (var p in gridPoints)
        {
            if (p == null) continue;

            float d = Vector3.Distance(pos, p.position);
            if (d < minDist)
            {
                minDist = d;
                closest = p;
            }
        }

        return closest;
    }

    #endregion
}
