using System.Collections.Generic;
using UnityEngine;

public class GoGridGenerator : MonoBehaviour
{
    [Header("网格设置")]
    public int gridSize = 19;   // 19x19 棋盘

    [Header("棋盘 MeshRenderer（拖棋盘那块平面的 MeshRenderer 上来）")]
    public MeshRenderer boardRenderer;

    [Header("旧参数（只有在没填 boardRenderer 时兜底使用）")]
    public Vector2 boardSize = new Vector2(0.4999f, 0.4999f);
    public float BoardScale = 10f;

    [Header("输出")]
    public Transform gridRoot;
    public List<Transform> gridPoints = new List<Transform>();

    void Awake()
    {
        GenerateGridPoints();

        // 把格点列表交给 GoGameManager
        var mgr = FindObjectOfType<GoGameManager>();
        if (mgr != null)
        {
            mgr.RegisterGridPoints(gridPoints);
        }
        else
        {
            Debug.LogWarning("[GoGridGenerator] 没找到 GoGameManager，无法注册格点。");
        }
    }

    void GenerateGridPoints()
    {
        // 保证有一个父节点
        if (gridRoot == null)
        {
            gridRoot = new GameObject("IntersectionsRoot").transform;
            gridRoot.SetParent(transform);
            gridRoot.localPosition = Vector3.zero;
            gridRoot.localRotation = Quaternion.identity;
            gridRoot.localScale = Vector3.one;
        }

        // 清空旧的格点
        for (int i = gridRoot.childCount - 1; i >= 0; i--)
        {
            var child = gridRoot.GetChild(i);
            Destroy(child.gameObject);   // Awake 里用 Destroy 就行，不要 DestroyImmediate
        }
        gridPoints.Clear();

        bool usedRendererBounds = false;
        float yOffset = 0.02f;

        // ---------------- 方案 1：用 MeshRenderer 的世界空间 bounds（推荐） ----------------
        if (boardRenderer != null)
        {
            Bounds bounds = boardRenderer.bounds;   // 世界空间包围盒
            Vector3 center = bounds.center;
            Vector3 size   = bounds.size;

            float stepX = size.x / (gridSize - 1);
            float stepZ = size.z / (gridSize - 1);

            for (int x = 0; x < gridSize; x++)
            {
                float worldX = center.x - size.x / 2f + x * stepX;

                for (int z = 0; z < gridSize; z++)
                {
                    float worldZ = center.z - size.z / 2f + z * stepZ;

                    Vector3 worldPoint = new Vector3(worldX, center.y + yOffset, worldZ);

                    GameObject p = new GameObject($"Intersection_{x}_{z}");
                    Transform t = p.transform;
                    t.position = worldPoint;
                    t.rotation = Quaternion.identity;
                    t.SetParent(gridRoot, true);

                    p.tag = "Intersections";
                    gridPoints.Add(t);
                }
            }

            Debug.Log($"[GoGridGenerator] 使用 Renderer.bounds 生成 {gridPoints.Count} 个格点。");
            usedRendererBounds = true;
        }

        // ---------------- 方案 2：没填 Renderer 时，用旧参数兜底 ----------------
        if (!usedRendererBounds)
        {
            float worldX = boardSize.x * BoardScale;
            float worldZ = boardSize.y * BoardScale;
            float cellX = worldX / (gridSize - 1);
            float cellZ = worldZ / (gridSize - 1);

            for (int x = 0; x < gridSize; x++)
            {
                for (int z = 0; z < gridSize; z++)
                {
                    GameObject p = new GameObject($"Intersection_{x}_{z}");
                    Transform t = p.transform;
                    t.SetParent(gridRoot);

                    float px = -worldX / 2f + x * cellX;
                    float pz = -worldZ / 2f + z * cellZ;

                    t.localPosition = new Vector3(px, yOffset, pz);
                    t.localRotation = Quaternion.identity;

                    p.tag = "Intersections";
                    gridPoints.Add(t);
                }
            }

            Debug.Log($"[GoGridGenerator] 使用 BoardSize/BoardScale 生成 {gridPoints.Count} 个格点。");
        }
    }

    // 运行时 + 选中棋盘格时，在 Scene 里画出所有格点
    void OnDrawGizmosSelected()
    {
        if (gridPoints == null || gridPoints.Count == 0)
            return;

        Gizmos.color = Color.green;
        foreach (var t in gridPoints)
        {
            if (t != null)
                Gizmos.DrawSphere(t.position, 0.01f);
        }
    }
}
