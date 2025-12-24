using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GoGameManager : MonoBehaviour
{
    public static GoGameManager Instance { get; private set; }

    public enum SnapFailReason
    {
        None = 0,
        NoValidGrid = 1,
        NeighbourhoodFull = 2,
    }

    [Header("棋盘格点（由 GridGenerator/你的格点脚本调用 RegisterGridPoints 注册）")]
    public List<Transform> gridPoints = new List<Transform>();

    [Header("吸附设置")]
    [Tooltip("最大吸附距离。<=0 表示不限距离")]
    public float maxSnapDistance = 0f;

    [Tooltip("初始化占位时，棋子与最近格点距离超过该值则不计入占位")]
    public float initOccupancyMaxDistance = 0.05f;

    [Header("调试")]
    public bool debugSnapLog = false;


    // 占位：格点 <-> 棋子
    private readonly Dictionary<Transform, Transform> gridToPiece = new();
    private readonly Dictionary<Transform, Transform> pieceToGrid = new();

    #region Undo 历史（本地用户）

    [Header("撤销设置（Undo）")]
    [Tooltip("撤销上一步放子时，若棋子原来不在任何格点（例如从棋盒新放上来），是否直接销毁该棋子。")]
    public bool undoDestroyIfFromOffboard = true;

    [Tooltip("手势可能在一段时间内重复触发；用于防抖。")]
    public float undoCooldown = 0.25f;

    private float _lastUndoTime = -999f;

    private struct PlacementAction
    {
        public Transform Piece;
        public Transform FromGrid;     // null 表示此前不在格点上
        public Vector3 FromPos;
        public Quaternion FromRot;
        public Transform ToGrid;       // 放下后的目标格点
    }

    private readonly Stack<PlacementAction> _placementHistory = new();

    #endregion


    // 邻域判定用：Intersection_x_z
    private readonly Dictionary<Transform, Vector2Int> gridToCoord = new();
    private readonly Dictionary<Vector2Int, Transform> coordToGrid = new();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
    }


    #region Grid 注册与初始化占位

    /// <summary>
    /// 注册格点，并建立坐标映射与初始占位。
    /// 约定格点命名：Intersection_x_z（或任意 "_" 分隔，且第2/3段是 x/z）
    /// </summary>
    public void RegisterGridPoints(List<Transform> points)
    {
        gridPoints = points ?? new List<Transform>();

        gridToPiece.Clear();
        pieceToGrid.Clear();
        gridToCoord.Clear();
        coordToGrid.Clear();
        _placementHistory.Clear();

        foreach (var t in gridPoints)
        {
            if (t == null) continue;

            var parts = t.name.Split('_');
            if (parts.Length >= 3 &&
                int.TryParse(parts[1], out int x) &&
                int.TryParse(parts[2], out int z))
            {
                var coord = new Vector2Int(x, z);
                gridToCoord[t] = coord;
                coordToGrid[coord] = t;
            }
        }

        InitializeOccupancyFromExistingPieces();

        if (debugSnapLog)
            Debug.Log($"[GoGameManager] RegisterGridPoints: grid={gridPoints.Count}, occupied={pieceToGrid.Count}");
    }

    private void InitializeOccupancyFromExistingPieces()
    {
        if (gridPoints == null || gridPoints.Count == 0) return;

        GameObject[] pieces;
        try { pieces = GameObject.FindGameObjectsWithTag("Pieces"); }
        catch { return; }

        foreach (var go in pieces)
        {
            if (go == null) continue;

            Transform piece = go.transform;
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

            if (closest == null) continue;
            if (initOccupancyMaxDistance > 0f && minDist > initOccupancyMaxDistance)
                continue;

            if (gridToPiece.TryGetValue(closest, out Transform occupied) &&
                occupied != null && occupied != piece)
                continue;

            gridToPiece[closest] = piece;
            pieceToGrid[piece] = closest;
        }
    }

    #endregion

    #region 对外 API：抓起解绑 / 强制回退 / 查询

    /// <summary>
    /// 棋子被抓起时调用：如果棋子当前在某个格点上，就把该格点标记为空，并返回格点；否则返回 null。
    /// </summary>
    public Transform DetachPieceFromGrid(Transform piece)
    {
        if (piece == null) return null;

        if (pieceToGrid.TryGetValue(piece, out Transform grid))
        {
            pieceToGrid.Remove(piece);

            if (grid != null &&
                gridToPiece.TryGetValue(grid, out Transform occupant) &&
                occupant == piece)
            {
                gridToPiece[grid] = null;
            }

            return grid;
        }

        return null;
    }

    /// <summary>
    /// 强制把棋子放回指定格点（不做规则检查）。
    /// </summary>
    public void ForcePlacePieceOnGrid(Transform piece, Transform grid)
    {
        if (piece == null || grid == null) return;

        // 清理原占位
        if (pieceToGrid.TryGetValue(piece, out Transform oldGrid))
        {
            if (oldGrid != null &&
                gridToPiece.TryGetValue(oldGrid, out Transform oldOccupant) &&
                oldOccupant == piece)
            {
                gridToPiece[oldGrid] = null;
            }
        }

        gridToPiece[grid] = piece;
        pieceToGrid[piece] = grid;

        piece.position = grid.position;

        //平放棋子 -90°
        Vector3 euler = piece.rotation.eulerAngles;
        piece.rotation = Quaternion.Euler(-90f, euler.y, 0f);
    }

    public Transform GetCurrentGridOfPiece(Transform piece)
    {
        if (piece == null) return null;
        pieceToGrid.TryGetValue(piece, out Transform grid);
        return grid;
    }

    
    /// <summary>
    /// 由棋子脚本在“松手且吸附成功”时调用：记录一次“放子动作”，用于撤销。
    /// </summary>
    public void RecordPlacementAction(
        Transform piece,
        Transform fromGrid,
        Vector3 fromWorldPos,
        Quaternion fromWorldRot,
        Transform toGrid)
    {
        if (piece == null || toGrid == null) return;

        // 只记录“确实发生了格点变化/落子”的情况：如果 fromGrid == toGrid 则不入栈
        if (fromGrid != null && fromGrid == toGrid) return;

        _placementHistory.Push(new PlacementAction
        {
            Piece = piece,
            FromGrid = fromGrid,
            FromPos = fromWorldPos,
            FromRot = fromWorldRot,
            ToGrid = toGrid
        });

        if (debugSnapLog)
            Debug.Log($"[GoGameManager] RecordPlacementAction: {piece.name} from={(fromGrid ? fromGrid.name : "OFFBOARD")} to={toGrid.name}, stack={_placementHistory.Count}");
    }

    /// <summary>
    /// 撤销“上一次（本地用户）放子/移动棋子到格点”的动作。
    /// 适配 UnityEvent（Gesture Performed）直接绑定。
    /// </summary>
    public void UndoLastPlacement()
    {
        if (Time.unscaledTime - _lastUndoTime < undoCooldown)
            return;

        _lastUndoTime = Time.unscaledTime;

        // 丢弃已销毁的记录
        while (_placementHistory.Count > 0)
        {
            var action = _placementHistory.Pop();
            if (action.Piece == null)
                continue;

            // 1) 先从当前格点解绑（无论它是否还在 ToGrid）
            DetachPieceFromGrid(action.Piece);

            // 2) 目标：回到 FromGrid；若 FromGrid 为空，则视为“撤销新落子”
            if (action.FromGrid != null)
            {
                // 若 FromGrid 被其他棋子占了，避免强行覆盖；退化为回到抓取前的位姿
                if (IsGridFree(action.FromGrid, ignorePiece: action.Piece))
                {
                    ForcePlacePieceOnGrid(action.Piece, action.FromGrid);
                }
                else
                {
                    action.Piece.position = action.FromPos;
                    action.Piece.rotation = action.FromRot;

                    if (debugSnapLog)
                        Debug.LogWarning($"[GoGameManager] Undo: FromGrid occupied, restore pose for {action.Piece.name}.");
                }
            }
            else
            {
                if (undoDestroyIfFromOffboard)
                {
                    Destroy(action.Piece.gameObject);
                }
                else
                {
                    action.Piece.position = action.FromPos;
                    action.Piece.rotation = action.FromRot;
                }
            }

            return;
        }

        if (debugSnapLog)
            Debug.Log("[GoGameManager] Undo: history empty.");
    }

    private bool IsGridFree(Transform grid, Transform ignorePiece = null)
    {
        if (grid == null) return false;

        if (!gridToPiece.TryGetValue(grid, out Transform occ) || occ == null)
            return true;

        return ignorePiece != null && occ == ignorePiece;
    }

    /// <summary>
    /// 清空棋盘上“已占位”的棋子（不含正在手里抓着、已解绑的棋子）。
    /// 适配 UnityEvent（Gesture Performed）直接绑定。
    /// </summary>
    public void ClearAllPiecesOnBoard()
    {
        // 从占位表拿快照，避免遍历过程中修改字典
        var toDelete = new List<Transform>();
        foreach (var kv in gridToPiece)
        {
            if (kv.Value != null)
                toDelete.Add(kv.Value);
        }

        foreach (var piece in toDelete)
        {
            if (piece != null)
                Destroy(piece.gameObject);
        }

        gridToPiece.Clear();
        pieceToGrid.Clear();
        _placementHistory.Clear();

        if (debugSnapLog)
            Debug.Log($"[GoGameManager] ClearAllPiecesOnBoard: deleted={toDelete.Count}");
    }
#endregion

    #region 吸附主逻辑：TrySnap

    public bool TrySnapPieceToClosestGridPoint(
        Transform piece,
        Vector3 referencePos,
        out SnapFailReason failReason)
    {
        failReason = SnapFailReason.None;

        if (piece == null || gridPoints == null || gridPoints.Count == 0)
        {
            failReason = SnapFailReason.NoValidGrid;
            return false;
        }

        // 1) 找最近可用格点
        Transform chosenGrid = FindClosestFreeGrid(referencePos, piece, out float chosenDist);
        if (chosenGrid == null)
        {
            if (debugSnapLog)
                Debug.Log($"[GoGameManager] No valid grid for {piece.name} (range/full).");

            failReason = SnapFailReason.NoValidGrid;
            return false;
        }

        // 2) 规则检查：8 邻居是否全满
        if (IsNeighbourhoodFull(chosenGrid))
        {
            if (debugSnapLog)
                Debug.Log($"[GoGameManager] Neighbourhood full around {chosenGrid.name}, illegal.");

            failReason = SnapFailReason.NeighbourhoodFull;
            return false;
        }

        // 3) 正式占位 + 移动
        if (pieceToGrid.TryGetValue(piece, out Transform prevGrid))
        {
            if (prevGrid != null &&
                gridToPiece.TryGetValue(prevGrid, out Transform prevPiece) &&
                prevPiece == piece)
            {
                gridToPiece[prevGrid] = null;
            }
        }

        gridToPiece[chosenGrid] = piece;
        pieceToGrid[piece] = chosenGrid;

        if (debugSnapLog)
            Debug.Log($"[GoGameManager] Snap {piece.name} -> {chosenGrid.name}, dist={chosenDist:F3}");

        piece.position = chosenGrid.position;

        Vector3 euler = piece.rotation.eulerAngles;
        piece.rotation = Quaternion.Euler(-90f, euler.y, 0f);

        return true;
    }

    #endregion

    #region 内部工具：找格点 / 邻域判定

    private Transform FindClosestFreeGrid(Vector3 referencePos, Transform piece, out float chosenDist)
    {
        chosenDist = float.MaxValue;
        Transform chosenGrid = null;

        if (gridPoints == null || gridPoints.Count == 0)
            return null;

        List<(Transform grid, float dist)> candidates = new List<(Transform, float)>(gridPoints.Count);
        foreach (var g in gridPoints)
        {
            if (g == null) continue;
            float d = Vector3.Distance(referencePos, g.position);
            candidates.Add((g, d));
        }

        candidates.Sort((a, b) => a.dist.CompareTo(b.dist));

        foreach (var (grid, dist) in candidates)
        {
            if (maxSnapDistance > 0f && dist > maxSnapDistance)
                continue;

            if (gridToPiece.TryGetValue(grid, out Transform occupiedBy))
            {
                if (occupiedBy != null && occupiedBy != piece)
                    continue;
            }

            chosenGrid = grid;
            chosenDist = dist;
            break;
        }

        return chosenGrid;
    }

    /// <summary>
    /// 以 centerGrid 为中心检查 8 邻居是否全部被占满。
    /// 边界外视为“空”，因此边缘/角落不会因为该规则而非法。
    /// </summary>
    private bool IsNeighbourhoodFull(Transform centerGrid)
    {
        if (centerGrid == null) return false;
        if (!gridToCoord.TryGetValue(centerGrid, out Vector2Int center))
            return false;

        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                if (dx == 0 && dz == 0) continue;

                var coord = new Vector2Int(center.x + dx, center.y + dz);

                if (!coordToGrid.TryGetValue(coord, out Transform neighbourGrid))
                    return false;

                if (!gridToPiece.TryGetValue(neighbourGrid, out Transform occupant) || occupant == null)
                    return false;
            }
        }

        return true;
    }

    #endregion

}
