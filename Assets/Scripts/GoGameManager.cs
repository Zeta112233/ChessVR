using System.Collections.Generic;
using UnityEngine;

public class GoGameManager : MonoBehaviour
{
    // 单例，方便 VR 脚本访问
    public static GoGameManager Instance { get; private set; }

    [Header("棋盘格点（由 GoGridGenerator 注册）")]
    public List<Transform> gridPoints = new List<Transform>();

    [Header("是否使用 VR（勾上则关闭鼠标控制，仅靠 VR 抓取）")]
    public bool useVR = true;

    [Header("棋子对齐设置")]
    // 是否用“棋子的渲染中心”对齐格点，减少看起来的偏移
    public bool alignUsingRendererCenter = true;
    // 如果整盘都有一点系统性偏移，可以在这里微调 X/Z
    public Vector3 extraOffset = Vector3.zero;

    [Header("吸附范围设置")]
    // 最大吸附距离（单位：米），0 表示不限制，全盘搜索最近空格子
    public float maxSnapDistance = 0f;

    [Header("初始化已有棋子占位")]
    // 开局自动识别在棋盘上的棋子时，允许的最大距离（棋子离最近格点超过这个距离就视为“不在棋盘上”）
    public float initOccupancyMaxDistance = 0.05f;

    [Header("调试")]
    public bool debugSnapLog = false;

    // 鼠标模式用
    private Transform grabbedPiece;
    private Camera cam;

    // 逻辑占位：每个格点当前占着哪颗棋子
    private readonly Dictionary<Transform, Transform> gridToPiece =
        new Dictionary<Transform, Transform>();
    // 反查：每颗棋子当前在哪个格点上
    private readonly Dictionary<Transform, Transform> pieceToGrid =
        new Dictionary<Transform, Transform>();

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
        gridPoints = points ?? new List<Transform>();

        // 棋盘格可能重建，清理原来的占位状态
        gridToPiece.Clear();
        pieceToGrid.Clear();

        if (debugSnapLog)
        {
            Debug.Log($"[GoGameManager] 注册格点数量：{gridPoints.Count}");
        }

        // ★ 在这里初始化开局时已经摆好的棋子占位
        InitializeOccupancyFromExistingPieces();
    }

    /// <summary>
    /// 扫描场景里 Tag = "Pieces" 的棋子，把它们登记到最近的格点上
    /// （只处理离最近格点在 initOccupancyMaxDistance 以内的，防止把托盘里的备用棋子也算进去）
    /// </summary>
    private void InitializeOccupancyFromExistingPieces()
    {
        if (gridPoints == null || gridPoints.Count == 0)
            return;

        GameObject[] piecesInScene;
        try
        {
            piecesInScene = GameObject.FindGameObjectsWithTag("Pieces");
        }
        catch
        {
            // 没有定义 "Pieces" Tag 也没关系
            return;
        }

        foreach (GameObject go in piecesInScene)
        {
            if (go == null) continue;
            Transform piece = go.transform;

            // 找离这颗棋子最近的格点
            Transform closest = null;
            float minDist = float.MaxValue;

            foreach (var g in gridPoints)
            {
                if (g == null) continue;
                float d = Vector3.Distance(piece.position, g.position);
                if (d < minDist)
                {
                    minDist = d;
                    closest = g;
                }
            }

            if (closest == null)
                continue;

            // 如果离最近格点太远，认为它不是“摆在棋盘上的棋子”（例如托盘里的备用子），直接跳过
            if (initOccupancyMaxDistance > 0f && minDist > initOccupancyMaxDistance)
                continue;

            // 如果这个格点已经登记了别的棋子，就保持原状态（一般不会出现，只是防御）
            if (gridToPiece.TryGetValue(closest, out Transform occupied)
                && occupied != null
                && occupied != piece)
            {
                continue;
            }

            gridToPiece[closest] = piece;
            pieceToGrid[piece]   = closest;

            if (debugSnapLog)
            {
                Debug.Log($"[GoGameManager] 初始化占位：{piece.name} -> {closest.name}, dist={minDist:F3}");
            }
        }
    }

    #region 鼠标模式

    void HandleMousePickAndDrop()
    {
        if (Input.GetMouseButtonDown(0))
        {
            if (cam == null) cam = Camera.main;

            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                // 棋子用 Tag "Pieces"
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
        if (cam == null) cam = Camera.main;

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
    /// 按给定参考点寻找最近“空格点”，并把棋子吸附过去。
    /// 如果最近格点被别的棋子占用，会自动寻找下一个最近的空格点。
    /// </summary>
    public void SnapPieceToClosestGridPoint(Transform piece, Vector3 referencePos)
    {
        if (piece == null) return;
        if (gridPoints == null || gridPoints.Count == 0) return;

        // 1. 找到“最近的空格点”（不止一个，按距离排序依次尝试）
        Transform chosenGrid = FindClosestFreeGrid(referencePos, piece, out float chosenDist);
        if (chosenGrid == null)
        {
            if (debugSnapLog)
            {
                Debug.Log($"[GoGameManager] 没有找到可用格点，{piece.name} 不吸附。");
            }
            return; // 全盘都被占或都超出 maxSnapDistance
        }

        // 2. 先计算理想吸附位置（还未真正移动）
        Vector3 targetPos = chosenGrid.position;

        // 2.1 用棋子“视觉中心”对齐格点（可关）
        if (alignUsingRendererCenter)
        {
            var rend = piece.GetComponentInChildren<Renderer>();
            if (rend != null)
            {
                Vector3 centerOffset = rend.bounds.center - piece.position;
                // 只修正水平位置，Y 由下面高度逻辑控制
                targetPos -= new Vector3(centerOffset.x, 0f, centerOffset.z);
            }
        }

        // 2.2 垂直方向：让棋子正好“坐”在棋盘上
        float yOffset = 0f;
        var col = piece.GetComponentInChildren<Collider>();
        if (col != null)
            yOffset = col.bounds.extents.y;

        targetPos.y = chosenGrid.position.y + yOffset;

        // 2.3 额外微调（如果整盘略偏，可在 Inspector 里改 X/Z）
        targetPos += new Vector3(extraOffset.x, 0f, extraOffset.z);

        // 3. 更新占位信息：
        //    先把这颗棋子之前所在的格点腾出来
        if (pieceToGrid.TryGetValue(piece, out Transform prevGrid))
        {
            if (prevGrid != null && gridToPiece.TryGetValue(prevGrid, out Transform prevPiece))
            {
                if (prevPiece == piece)
                    gridToPiece[prevGrid] = null;
            }
        }

        //    再把当前格点标记为被这颗棋子占用
        gridToPiece[chosenGrid] = piece;
        pieceToGrid[piece]      = chosenGrid;

        if (debugSnapLog)
        {
            Debug.Log(
                $"[GoGameManager] Snap {piece.name} from {referencePos} " +
                $"to {chosenGrid.name} at {targetPos}, dist={chosenDist:F3}");
        }

        // 4. 真正移动棋子
        piece.position = targetPos;
    }

    /// <summary>
    /// 在所有格点中找离指定位置最近的“空格点”。
    /// - 距离按 referencePos 到格点距离排升序；
    /// - 如果格点被其他棋子占用，则跳过；
    /// - 如果设置了 maxSnapDistance > 0，则超过这个距离的格点也会被跳过。
    /// </summary>
    private Transform FindClosestFreeGrid(Vector3 referencePos, Transform piece, out float chosenDist)
    {
        chosenDist = float.MaxValue;
        Transform chosenGrid = null;

        if (gridPoints == null || gridPoints.Count == 0)
            return null;

        // 先构建一个列表，把所有格点按距离排序
        List<(Transform grid, float dist)> candidates = new List<(Transform, float)>(gridPoints.Count);
        foreach (var p in gridPoints)
        {
            if (p == null) continue;
            float d = Vector3.Distance(referencePos, p.position);
            candidates.Add((p, d));
        }

        candidates.Sort((a, b) => a.dist.CompareTo(b.dist));

        // 按距离从近到远，找第一个“没被别的棋子占用”的格点
        foreach (var (grid, dist) in candidates)
        {
            // 如果设置了最大吸附距离，并且超出，则跳过
            if (maxSnapDistance > 0f && dist > maxSnapDistance)
                continue;

            if (gridToPiece.TryGetValue(grid, out Transform occupiedBy))
            {
                // 如果是自己占着自己，认为是可用（抬起再放下）
                if (occupiedBy != null && occupiedBy != piece)
                {
                    // 被别的棋子占用，跳过
                    continue;
                }
            }

            // 找到一个可用格点
            chosenGrid = grid;
            chosenDist = dist;
            break;
        }

        return chosenGrid;
    }

    #endregion
}
