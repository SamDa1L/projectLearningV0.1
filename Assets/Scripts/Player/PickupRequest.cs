/// <summary>
/// 拾取请求结构体
/// 用于传递拾取操作的参数
///
/// 规范（契约 [C-Runtime-2]）：
/// - itemId：要拾取的物品 ID（必须在 ItemCatalog 中存在）
/// - amount：拾取数量（Ability 类必须为 1，Consumable 可 >1）
/// - sourcePickup：触发拾取的 ItemPickup 实例（RequireReplace 场景必须非空）
/// </summary>
public readonly struct PickupRequest
{
    /// <summary>
    /// 要拾取的物品 ID
    /// 必须在 ItemCatalog 中存在，否则返回 Failed_InvalidItemId
    /// </summary>
    public readonly string itemId;

    /// <summary>
    /// 拾取数量
    /// - Ability 类必须为 1，否则返回 Failed_NotSupported
    /// - Consumable 可以 >1，会累加到 potionCount
    /// - ≤0 返回 Failed_NotSupported
    /// </summary>
    public readonly int amount;

    /// <summary>
    /// 触发拾取的源 ItemPickup 实例
    /// - RequireReplace 场景必须非空（用于锁定拾取物）
    /// - 其他场景可为空
    /// </summary>
    public readonly ItemPickup sourcePickup;

    public PickupRequest(string itemId, int amount, ItemPickup sourcePickup)
    {
        this.itemId = itemId;
        this.amount = amount;
        this.sourcePickup = sourcePickup;
    }

    public override string ToString()
    {
        string sourceStr = sourcePickup != null ? $", sourcePickup={sourcePickup.name}" : "";
        return $"PickupRequest[itemId={itemId}, amount={amount}{sourceStr}]";
    }
}
