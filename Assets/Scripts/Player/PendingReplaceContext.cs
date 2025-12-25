/// <summary>
/// 待替换上下文结构体
/// 用于传递需要替换槽位的拾取操作上下文
///
/// 规范（契约 [C-Runtime-2]）：
/// - 当 TryPickup 返回 RequireReplace 时，ctx 包含待入背包的物品信息
/// - sourcePickup 必须非空（用于锁定/解锁拾取物）
/// - ReplaceController 使用此上下文完成替换流程
/// </summary>
public readonly struct PendingReplaceContext
{
    /// <summary>
    /// 待入背包的物品 ID
    /// </summary>
    public readonly string pendingItemId;

    /// <summary>
    /// 待入背包的数量（Ability 类固定为 1）
    /// </summary>
    public readonly int pendingAmount;

    /// <summary>
    /// 触发 RequireReplace 的源 ItemPickup 实例（必须非空）
    /// ReplaceController 用于：
    /// - Confirm 时销毁拾取物
    /// - Cancel 时解锁拾取物
    /// </summary>
    public readonly ItemPickup sourcePickup;

    public PendingReplaceContext(string pendingItemId, int pendingAmount, ItemPickup sourcePickup)
    {
        this.pendingItemId = pendingItemId;
        this.pendingAmount = pendingAmount;
        this.sourcePickup = sourcePickup;
    }

    public override string ToString()
    {
        string sourceStr = sourcePickup != null ? $", sourcePickup={sourcePickup.name}" : ", sourcePickup=null";
        return $"PendingReplaceContext[pendingItemId={pendingItemId}, pendingAmount={pendingAmount}{sourceStr}]";
    }
}
