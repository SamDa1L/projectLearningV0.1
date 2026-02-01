using System.Collections.Generic;
using UnityEngine;
using CastleDB.Runtime;

/// <summary>
/// 场景拾取物组件
/// 负责玩家触发拾取的交互逻辑
///
/// 规范（契约 [C-Runtime-4]）：
/// - 生命周期：Idle → Locked → Destroyed
/// - 依赖定位：通过 GetComponentInParent<PlayerContext>() 获取玩家服务
/// - 交互门控：InteractionEnabled / IsSelecting 检查
/// - 锁定机制：RequireReplace 时立即锁定，禁用 Collider
/// - 重试拾取：OnTriggerStay2D + 0.15秒节流
/// - 数据校验：OnValidate 自动修正 amount/Ability 类型
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class ItemPickup : MonoBehaviour
{
    // ===== 序列化字段 =====
    /// <summary>
    /// 物品 ID（对应 ItemCatalog 中的 id）
    /// </summary>
    [SerializeField]
    [Tooltip("物品 ID（对应 ItemCatalog 中的 id）")]
    public string itemId;

    /// <summary>
    /// 拾取数量
    /// 能力类固定为 1（检视面板会自动修正）
    /// 消耗品类可 > 1
    /// </summary>
    [SerializeField]
    [Tooltip("拾取数量（Ability 固定 1，Consumable 可 >1）")]
    public int amount = 1;

    /// <summary>
    /// 是否自动拾取
    /// 0.4 版本不支持手动拾取，此字段保留以便后续扩展
    /// </summary>
    [SerializeField]
    [Tooltip("是否自动拾取（0.4 版本固定为 true）")]
    public bool autoPickup = true;

    // ===== 运行时状态 =====
    /// <summary>
    /// 是否已锁定（RequireReplace 时锁定）
    /// </summary>
    private bool _locked = false;

    /// <summary>
    /// 碰撞器组件（Collider2D）引用
    /// </summary>
    private Collider2D _collider;

    /// <summary>
    /// 下次允许重试拾取的时间（用于节流）
    /// </summary>
    private float _nextRetryTime = 0f;

    // ===== 一次性日志去重 =====
    private static readonly HashSet<string> _loggedWarnings = new HashSet<string>();

    // ===== 生命周期 =====
    private void Awake()
    {
        _collider = GetComponent<Collider2D>();

        if (_collider == null)
        {
            Debug.LogError($"[ItemPickup] 缺少 Collider2D 组件，交互将无法工作", this);
        }

        // 自动拾取开关（autoPickup）语义检查（0.4 不支持手动拾取）
        if (!autoPickup)
        {
            string key = $"ItemPickup_ManualPickupNotSupported_{itemId}";
            if (!_loggedWarnings.Contains(key))
            {
                Debug.LogWarning($"[ItemPickup] 0.4 暂不支持手动拾取，已按自动拾取处理。itemId={itemId}", this);
                _loggedWarnings.Add(key);
            }
        }
    }

    // ===== 交互逻辑 =====
    private void OnTriggerEnter2D(Collider2D other)
    {
        // 已锁定则不处理
        if (_locked)
            return;

        // 尝试拾取
        TryPickupInternal(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        // 已锁定则不处理
        if (_locked)
            return;

        // 节流：仅当 Time.time >= _nextRetryTime 时才重试
        if (Time.time < _nextRetryTime)
            return;

        // 尝试拾取
        TryPickupInternal(other);
    }

    /// <summary>
    /// 内部拾取逻辑（OnTriggerEnter2D 和 OnTriggerStay2D 共用）
    /// </summary>
    private void TryPickupInternal(Collider2D other)
    {
        // 获取 PlayerContext
        PlayerContext playerCtx = other.GetComponentInParent<PlayerContext>();
        if (playerCtx == null)
            return; // 不是玩家，不记 Error

        // 门控检查：InteractionEnabled
        if (!playerCtx.InteractionEnabled)
            return;

        // 门控检查：IsSelecting
        if (playerCtx.ReplaceController != null && playerCtx.ReplaceController.IsSelecting)
            return;

        // 满血时不拾取回血药水（仅对 heal>0 的 Consumable 生效）
        if (playerCtx.Inventory != null &&
            playerCtx.Damageable != null &&
            playerCtx.Inventory.TryGetConsumableHealAmount(itemId, out int healAmount) &&
            healAmount > 0 &&
            !playerCtx.Damageable.CanReceiveHeal)
        {
            return;
        }

        // 构造 PickupRequest
        PickupRequest req = new PickupRequest(itemId, amount, this);

        // 阶段 7：遗物拾取优先走 PlayerRelicController（不走 Inventory.TryPickup）
        // 规则：若返回非 Failed_NotSupported，则视为“已处理”（成功或失败）并直接进入结果处理。
        if (playerCtx.RelicController != null)
        {
            PickupResult relicResult = playerCtx.RelicController.TryPickupRelic(req);
            if (relicResult != PickupResult.Failed_NotSupported)
            {
                HandlePickupResult(relicResult, default, playerCtx);
                return;
            }
        }

        // 调用 TryPickup
        PickupResult result = playerCtx.Inventory.TryPickup(req, out PendingReplaceContext ctx);

        // 处理结果
        HandlePickupResult(result, ctx, playerCtx);
    }

    /// <summary>
    /// 处理拾取结果
    /// </summary>
    private void HandlePickupResult(PickupResult result, PendingReplaceContext ctx, PlayerContext playerCtx)
    {
        switch (result)
        {
            case PickupResult.Success:
            {
                int actualHeal = TryUsePotionAfterPickup(playerCtx);

                bool isHealingConsumable = false;
                if (playerCtx.Inventory != null &&
                    playerCtx.Inventory.TryGetConsumableHealAmount(itemId, out int healAmount))
                {
                    isHealingConsumable = healAmount > 0;
                }

                // 成功：销毁自身（回血药水必须实际回血才销毁）
                if (!isHealingConsumable || actualHeal > 0)
                {
                    Destroy(gameObject);
                }

                break;
            }

            case PickupResult.RequireReplace:
                // 需要替换：锁定自身，调用 ReplaceController.BeginReplace
                SetLocked(true);

                if (playerCtx.ReplaceController == null)
                {
                    Debug.LogError($"[ItemPickup] ReplaceController 缺失，无法进入 Replace 流程。itemId={itemId}", this);
                    SetLocked(false); // 解锁
                }
                else
                {
                    playerCtx.ReplaceController.BeginReplace(ctx);
                }
                break;

            case PickupResult.Failed_InvalidItemId:
                // 失败：itemId 非法（已由 PlayerInventory 输出 Warning）
                LogFailedPickup(result);
                break;

            case PickupResult.Failed_AlreadyEquipped:
                // 失败：能力已装备（已由 PlayerInventory 输出 Info）
                LogFailedPickup(result);
                break;

            case PickupResult.Failed_NotSupported:
                // 失败：操作不支持（默认不输出，避免噪音）
                // 如果需要调试，可以启用以下日志：
                // 例：Debug.Log($"[ItemPickup] 拾取失败：不支持。itemId={itemId}", this);
                break;

            default:
                Debug.LogError($"[ItemPickup] 未知拾取结果: {result}。itemId={itemId}", this);
                break;
        }

        // 更新重试时间（节流）
        _nextRetryTime = Time.time + 0.15f;
    }

    /// <summary>
    /// 记录失败拾取日志（一次性去重）
    /// </summary>
    private void LogFailedPickup(PickupResult result)
    {
        string key = $"ItemPickup_Failed_{result}_{itemId}";
        if (_loggedWarnings.Contains(key))
            return;

        switch (result)
        {
            case PickupResult.Failed_InvalidItemId:
                Debug.LogWarning($"[ItemPickup] 拾取失败：itemId 非法或不存在。itemId={itemId}", this);
                break;

            case PickupResult.Failed_AlreadyEquipped:
                Debug.LogFormat(LogType.Log, LogOption.None, this,
                    $"[ItemPickup] 拾取失败：能力已装备。itemId={itemId}");
                break;
        }

        _loggedWarnings.Add(key);
    }

    // ===== 药水使用 (0.45) =====
    /// <summary>
    /// 拾取成功后尝试使用（仅 Consumable 会生效）
    /// 0.45 修正：UsePotion 内部会检查 itemType，非 Consumable 静默返回
    /// </summary>
    private int TryUsePotionAfterPickup(PlayerContext playerCtx)
    {
        // 检查依赖
        if (playerCtx.Inventory == null || playerCtx.Damageable == null)
            return 0;

        // 调用 UsePotion（内部会检查 itemType，非 Consumable 时静默返回 false）
        playerCtx.Inventory.UsePotion(itemId, playerCtx.Damageable, out int actualHeal);
        return actualHeal;
    }

    // ===== 锁定机制 =====
    /// <summary>
    /// 设置锁定状态
    /// </summary>
    public void SetLocked(bool locked)
    {
        _locked = locked;

        if (_collider != null)
        {
            _collider.enabled = !locked;
        }

        // 进入锁定时清空节流时间（解锁后可立即再次尝试）
        if (locked)
        {
            _nextRetryTime = 0f;
        }
    }

    /// <summary>
    /// 是否已锁定
    /// </summary>
    public bool IsLocked => _locked;

    // ===== 编辑器校验 =====
#if UNITY_EDITOR
    /// <summary>
    /// 编辑器校验级别（仅用于 OnValidate 与单元测试）。
    /// 注意：这是“纯逻辑”结果，不包含任何日志输出。
    /// </summary>
    public enum EditorValidationSeverity
    {
        None,
        Warning,
        Error
    }

    /// <summary>
    /// 编辑器规则校验（纯逻辑）：按规则修正 amount，并返回需要提示的级别与消息。
    /// 目的：
    /// - OnValidate 仍可在编辑器中输出提示
    /// - 单元测试不再依赖 Debug.LogError/LogAssert.Expect 来“验收”失败路径
    /// </summary>
    public static EditorValidationSeverity ValidateAndFixAmountForEditor(ItemType itemType, ref int amount, out string message)
    {
        message = null;

        // 1) amount <= 0 自动改为 1
        if (amount <= 0)
        {
            amount = 1;
            message = "amount <= 0 已自动修正为 1。";
            return EditorValidationSeverity.Warning;
        }

        // 2) 若 itemType=Ability/Relic，强制 amount=1
        if ((itemType == ItemType.Ability || itemType == ItemType.Relic) && amount != 1)
        {
            amount = 1;
            message = "itemType=Ability/Relic 时 amount 必须为 1，已自动修正。";
            return EditorValidationSeverity.Warning;
        }

        // 3) 若 itemType=Material，提示错误（0.4 不支持）
        if (itemType == ItemType.Material)
        {
            message = "itemType=Material 在 0.4 版本不支持拾取，请勿投放。";
            return EditorValidationSeverity.Error;
        }

        return EditorValidationSeverity.None;
    }

    private void OnValidate()
    {
        // 1) 先处理 amount<=0（不依赖 ItemCatalog）
        int tmpAmount = amount;
        var severity = ValidateAndFixAmountForEditor(ItemType.Consumable, ref tmpAmount, out string message);
        if (severity == EditorValidationSeverity.Warning && tmpAmount != amount)
        {
            amount = tmpAmount;
            Debug.LogWarning($"[ItemPickup] {message} itemId={itemId}", this);
        }

        // 2) 若能从 ItemCatalog 解析到 itemType，则应用类型规则
        // 注意：编辑模式测试中 ItemCatalog.asset 可能被 AssetDatabase 删除/重建，
        // 因此这里不能使用“资源提供器”的缓存 ItemCatalog 引用，必须实时加载最新资源。
        ItemCatalog catalog = ResourcesGameAssetProvider.Shared.Load<ItemCatalog>("Config/ItemCatalog");
        if (catalog == null || !catalog.TryGetItem(itemId, out ItemDefinition def))
        {
            return;
        }

        tmpAmount = amount;
        severity = ValidateAndFixAmountForEditor(def.itemType, ref tmpAmount, out message);
        if (tmpAmount != amount)
        {
            amount = tmpAmount;
        }

        switch (severity)
        {
            case EditorValidationSeverity.Warning:
                Debug.LogWarning($"[ItemPickup] {message} itemId={itemId}", this);
                break;

            case EditorValidationSeverity.Error:
                Debug.LogError($"[ItemPickup] {message} itemId={itemId}", this);
                break;
        }
    }
#endif
}
