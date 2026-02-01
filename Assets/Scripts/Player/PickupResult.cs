/// <summary>
/// 拾取结果枚举
/// 用于表示 PlayerInventory.TryPickup 的返回状态
///
/// 规范（契约 [C-Runtime-2]）：
/// - Success：拾取成功，物品已加入背包
/// - RequireReplace：槽位已满，需要用户选择替换槽位
/// - Failed_InvalidItemId：itemId 非法或不存在
/// - Failed_AlreadyEquipped：能力已装备，禁止重复装备
/// - Failed_NotSupported：操作不支持（如 Material 拾取、无效参数等）
/// </summary>
public enum PickupResult
{
    /// <summary>
    /// 拾取成功，物品已加入背包
    /// </summary>
    Success = 0,

    /// <summary>
    /// 需要替换：能力槽已满，需要用户选择替换哪个槽位
    /// </summary>
    RequireReplace = 1,

    /// <summary>
    /// 失败：itemId 非法或不存在于 ItemCatalog
    /// </summary>
    Failed_InvalidItemId = 2,

    /// <summary>
    /// 失败：能力已装备在其他槽位，禁止重复装备
    /// </summary>
    Failed_AlreadyEquipped = 3,

    /// <summary>
    /// 失败：操作不支持
    /// 可能原因：
    /// - Material 类 Item（0.4 不支持）
    /// - amount 参数非法（≤0 或 Ability 的 amount≠1）
    /// - sourcePickup 为空但需要 RequireReplace
    /// - Consumable 已达上限
    /// </summary>
    Failed_NotSupported = 4
}
