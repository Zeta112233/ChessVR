using UnityEngine;
using System.Collections;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(XRGrabInteractable))]
[RequireComponent(typeof(Rigidbody))]
public class GoPieceVRSnap : MonoBehaviour
{
    private XRGrabInteractable grab;
    private Rigidbody rb;
    private RigidbodyConstraints originalConstraints;

    [Header("用于向下检测棋盘表面的 LayerMask（建议只包含棋盘/桌面）")]
    public LayerMask boardLayerMask;

    [Header("松手后行为")]
    [Tooltip("吸附成功或回退后是否冻结刚体，避免继续滑动/抖动")]
    public bool freezeAfterSnap = true;

    [Tooltip("若棋子不在棋盘格点上（例如从棋盒新生成）且松手落点不合法，是否销毁该棋子")]
    public bool destroyIfInvalidAndNoPreviousGrid = true;

    [Header("回收设置")]
    [Tooltip("如果松手时检测到碰到了棋盒（通过 Tag 判断），是否直接销毁棋子（回收）")]
    public bool recycleIfTouchingBox = true;
    [Tooltip("棋盒的 Tag 列表，碰到这些 Tag 的物体就视为回收")]
    public string[] boxTags = new string[] { "BlackBoxPieces", "WhiteBoxPieces" };
    [Tooltip("回收检测半径")]
    public float recycleCheckRadius = 0.002f;

    // 抓起前所在格点（若原本就在棋盘上）
    private Transform prevGridBeforeGrab = null;

    // 用于撤销：记录抓取前的世界位姿（如果此前不在格点上，则撤销时可回到这里或销毁）
    private Vector3 prevWorldPosBeforeGrab;
    private Quaternion prevWorldRotBeforeGrab;


    private IEnumerator Start()
    {
        // 稍微等待一帧，确保 GoGameManager 和 GridGenerator 已经初始化完毕
        yield return null;

        var mgr = GoGameManager.Instance;
        if (mgr != null)
        {
            // 尝试吸附一次（用于开局自动对齐场景里预摆的棋子）
            // 注意：这里不需要销毁逻辑，如果吸附失败就留在原地即可
            mgr.TrySnapPieceToClosestGridPoint(transform, transform.position, out _);
            
            FreezeRigidbodyIfNeeded();
        }
    }

    private void Awake()
    {
        grab = GetComponent<XRGrabInteractable>();
        rb = GetComponent<Rigidbody>();

        if (rb != null)
            originalConstraints = rb.constraints;
    }

    private void OnEnable()
    {
        if (grab != null)
        {
            grab.selectEntered.AddListener(OnSelectEntered);
            grab.selectExited.AddListener(OnSelectExited);
        }
    }

    private void OnDisable()
    {
        if (grab != null)
        {
            grab.selectEntered.RemoveListener(OnSelectEntered);
            grab.selectExited.RemoveListener(OnSelectExited);
        }
    }

    private void OnSelectEntered(SelectEnterEventArgs args)
    {
        if (rb == null) return;

        var mgr = GoGameManager.Instance;
        prevGridBeforeGrab = null;

        // 记录抓取前位姿（用于撤销/回退）
        prevWorldPosBeforeGrab = transform.position;
        prevWorldRotBeforeGrab = transform.rotation;

        // 抓起：从占位表解绑，并记录原格点（用于回退）
        if (mgr != null)
            prevGridBeforeGrab = mgr.DetachPieceFromGrid(transform);

        rb.isKinematic = false;
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.constraints = originalConstraints;
    }

    private void OnSelectExited(SelectExitEventArgs args)
    {
        // 0) 回收检测：如果扔回了棋盒，直接销毁
        if (recycleIfTouchingBox)
        {
            Collider[] hits = Physics.OverlapSphere(transform.position, recycleCheckRadius);
            foreach (var c in hits)
            {
                if (c == null) continue;
                
                // 检查是否撞到了棋盒（检查 Tag）
                foreach (var tag in boxTags)
                {
                    // 允许棋盒本身的 Collider，或者棋盒的子物体
                    if (c.CompareTag(tag))
                    {
                        Debug.Log($"[GoPieceVRSnap] 棋子 {name} 碰到了棋盒 ({c.name})，执行回收销毁。");
                        Destroy(gameObject);
                        return;
                    }
                }
            }
        }

        var mgr = GoGameManager.Instance;
        if (mgr == null)
        {
            FreezeRigidbodyIfNeeded();
            return;
        }

        // 1) 取参考位置：优先用向下射线命中棋盘点，否则用当前 transform.position
        Vector3 referencePos = transform.position;
        if (Physics.Raycast(transform.position + Vector3.up * 0.05f,
                            Vector3.down,
                            out RaycastHit hit,
                            2f,
                            boardLayerMask))
        {
            referencePos = hit.point;
        }

        // 2) 吸附判定
        GoGameManager.SnapFailReason reason;
        bool snapped = mgr.TrySnapPieceToClosestGridPoint(transform, referencePos, out reason);

        if (!snapped)
        {
            // 不合法：优先回退到原格点
            if (prevGridBeforeGrab != null)
            {
                mgr.ForcePlacePieceOnGrid(transform, prevGridBeforeGrab);
            }
            else
            {
                // 新棋子（来自棋盒生成）且没有可回退格点
                if (destroyIfInvalidAndNoPreviousGrid)
                {
                    Destroy(gameObject);
                    return;
                }
                // 否则就让它留在当前位置（不占位）
            }
        }

        else
        {
            // 吸附成功：记录一次落子动作（用于撤销）
            Transform toGrid = mgr.GetCurrentGridOfPiece(transform);
            if (toGrid != null)
            {
                mgr.RecordPlacementAction(
                    transform,
                    prevGridBeforeGrab,
                    prevWorldPosBeforeGrab,
                    prevWorldRotBeforeGrab,
                    toGrid);
            }
        }

        FreezeRigidbodyIfNeeded();
    }

    private void FreezeRigidbodyIfNeeded()
    {
        if (rb == null) return;

        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        if (freezeAfterSnap)
        {
            rb.constraints = RigidbodyConstraints.FreezeAll;
            rb.isKinematic = true;
        }
    }
}
