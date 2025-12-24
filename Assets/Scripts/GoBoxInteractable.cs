using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

[RequireComponent(typeof(XRSimpleInteractable))]
public class GoBoxInteractable : MonoBehaviour
{
    [Header("棋盒类型")]
    public bool isBlackBox = true;

    [Header("生成棋子")]
    public GameObject blackStonePrefab;
    public GameObject whiteStonePrefab;

    [Tooltip("棋子生成后的父节点（可不填）")]
    public Transform boardBlackRoot;
    public Transform boardWhiteRoot;

    [Header("XR 交互")]
    [Tooltip("XR Interaction Manager（建议显式拖拽；不填则在 Awake 自动查找一次）")]
    public XRInteractionManager interactionManager;

    [Header("生成棋子 Tag（需在 Project Settings > Tags and Layers 中存在）")]
    public string spawnedPieceTag = "Pieces";

    private XRSimpleInteractable simple;
    private float lastSpawnTime = 0f;
    private const float SPAWN_COOLDOWN = 0.5f;

    private void Awake()
    {
        simple = GetComponent<XRSimpleInteractable>();

        if (interactionManager == null)
            interactionManager = FindObjectOfType<XRInteractionManager>();
    }

    private void OnEnable()
    {
        if (simple != null)
            simple.selectEntered.AddListener(OnSelectEntered);
    }

    private void OnDisable()
    {
        if (simple != null)
            simple.selectEntered.RemoveListener(OnSelectEntered);
    }

    private void OnSelectEntered(SelectEnterEventArgs args)
    {
        if (interactionManager == null) return;

        // 【修复】防止双重触发/连击：如果距离上次生成太近，直接忽略
        if (Time.time - lastSpawnTime < SPAWN_COOLDOWN)
            return;

        // XRI 3.x: args.interactorObject 是 IXRSelectInteractor
        // 这里取 XRBaseInteractor 仅用于 GetAttachTransform（不再用 StartManualInteraction）
        if (args.interactorObject is not XRBaseInteractor interactor)
            return;

        GameObject prefab = isBlackBox ? blackStonePrefab : whiteStonePrefab;
        Transform parent = isBlackBox ? boardBlackRoot : boardWhiteRoot;
        if (prefab == null) return;

        // 1) 先让手松开棋盒（避免把盒子抓走 / selection 卡住）
        interactionManager.SelectExit(args.interactorObject, args.interactableObject);

        // 更新冷却时间
        lastSpawnTime = Time.time;

        // 2) 生成真正棋子（Prefab 必须带 XRGrabInteractable + Rigidbody + Collider）
        GameObject piece = (parent != null) ? Instantiate(prefab, parent) : Instantiate(prefab);

        // Tag 若不存在会抛异常；这里避免运行期中断
        if (!string.IsNullOrEmpty(spawnedPieceTag))
        {
            try { piece.tag = spawnedPieceTag; } catch { }
        }

        XRGrabInteractable grab = piece.GetComponent<XRGrabInteractable>();
        if (grab == null)
        {
            Debug.LogWarning("[GoBoxInteractable] 棋子 prefab 缺少 XRGrabInteractable");
            Destroy(piece);
            return;
        }

        // 3) 根据交互器类型决定生成位置
        // 如果是 Ray Interactor 且有射线击中点 -> 视为“远程抓取”，在击中点生成，保持距离
        if (interactor is XRRayInteractor rayInteractor && 
            rayInteractor.TryGetCurrent3DRaycastHit(out RaycastHit hit))
        {
            piece.transform.position = hit.point;
            // 保持 prefab 默认旋转，通常棋子平放即可
        }
        else
        {
            // 否则（Direct Interactor 或 Ray 没扫到），吸附到手的 Attach 点
            Transform attach = interactor.GetAttachTransform(grab);
            if (attach != null)
                piece.transform.SetPositionAndRotation(attach.position, attach.rotation);
            else
                piece.transform.position = interactor.transform.position;
        }

        // 4) 关键修正：
        // 不用 StartManualInteraction（否则容易出现“永远抓着放不下”），
        // 改用 InteractionManager.SelectEnter 走正常抓取链路
        interactionManager.SelectEnter(args.interactorObject, (IXRSelectInteractable)grab);
    }
}
