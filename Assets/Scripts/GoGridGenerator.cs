using System.Collections.Generic;
using UnityEngine;

public class GoGridGenerator : MonoBehaviour
{
    [Header("网格设置")]
    public int gridSize = 19;   // 19x19 棋盘

    [Header("棋盘 MeshRenderer（拖棋盘那块平面的 MeshRenderer 上来）")]
    public MeshRenderer boardRenderer;

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
        float yOffset = 0.005f;

        // ---------------- 用 MeshRenderer 的世界空间生成棋盘格点 ----------------

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
        }
    

    // 运行时选中棋盘格，在 Scene 里画出所有格点(Debug)
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
