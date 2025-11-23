using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// 挂在每颗棋子上：
/// - 需要同物体上有 XRGrabInteractable + Rigidbody + Collider
/// - 当 VR 手柄松手时，让棋子吸附到最近的棋盘格点
/// </summary>
[RequireComponent(typeof(XRGrabInteractable))]
[RequireComponent(typeof(Rigidbody))]
public class GoPieceVRSnap : MonoBehaviour
{
    private XRGrabInteractable grab;
    private Rigidbody rb;

    [Header("用于投射到棋盘的 Layer（只勾棋盘/桌面那层）")]
    public LayerMask boardLayerMask;

    private void Awake()
    {
        grab = GetComponent<XRGrabInteractable>();
        rb   = GetComponent<Rigidbody>();

        grab.selectExited.AddListener(OnSelectExited);
    }

    private void OnDestroy()
    {
        if (grab != null)
            grab.selectExited.RemoveListener(OnSelectExited);
    }

    // 手柄松手
    private void OnSelectExited(SelectExitEventArgs args)
    {
        Debug.Log($"[GoPieceVRSnap] SelectExited on {name}");

        GoGameManager mgr = GoGameManager.Instance != null
            ? GoGameManager.Instance
            : FindObjectOfType<GoGameManager>();

        if (mgr == null)
        {
            Debug.LogWarning("[GoPieceVRSnap] 找不到 GoGameManager");
            return;
        }

        // 1. 先用向下射线找一下棋盘表面作为参考点
        Vector3 referencePos = transform.position;
        if (Physics.Raycast(transform.position + Vector3.up * 0.05f,
                            Vector3.down,
                            out RaycastHit hit,
                            2f,
                            boardLayerMask))
        {
            referencePos = hit.point;
            Debug.Log($"[GoPieceVRSnap] Hit board at {referencePos}");
        }
        else
        {
            Debug.Log("[GoPieceVRSnap] 没打到棋盘，用棋子当前位置作为参考");
        }

        // 2. 调用吸附
        mgr.SnapPieceToClosestGridPoint(transform, referencePos);

        // 3. 清掉速度，避免继续乱弹
        if (rb != null)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }
}
