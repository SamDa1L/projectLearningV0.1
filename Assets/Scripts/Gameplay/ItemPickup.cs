using UnityEngine;

/// <summary>
/// 场景拾取物组件（存根，完整实现在 Phase 4）
///
/// Phase 3 职责：
/// - 作为类型声明，供 PickupRequest/PendingReplaceContext 引用
/// - 提供基本字段定义（itemId/amount/autoPickup）
///
/// Phase 4 职责：
/// - 实现交互逻辑（OnTriggerEnter2D）
/// - 实现锁定机制（SetLocked）
/// - 实现拾取流程（与 PlayerInventory 集成）
/// </summary>
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
    /// Ability 类固定为 1（Inspector 会自动修正）
    /// Consumable 类可 >1
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

    // Phase 4 TODO:
    // - 添加 Collider2D 引用
    // - 实现 OnTriggerEnter2D/OnTriggerStay2D
    // - 实现 SetLocked(bool) 方法
    // - 实现拾取流程（PlayerContext 获取、TryPickup 调用、结果处理）
}
