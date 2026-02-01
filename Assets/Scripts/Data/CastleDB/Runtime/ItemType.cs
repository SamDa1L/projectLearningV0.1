namespace CastleDB.Runtime
{
    /// <summary>
    /// Item 类型枚举
    /// 必须与 CastleDB Item Sheet 中的 itemType 枚举值对应
    ///
    /// 规范（0.4）：
    /// - Ability：可装备到 4 个能力槽位的 Item，必须填写 abilityId
    /// - Consumable：消耗品（如血瓶），累加到 potionCount
    /// - Material：材料类（0.4 不处理，返回 Failed_NotSupported）
    /// </summary>
    public enum ItemType
    {
        Ability = 0,
        Consumable = 1,
        Material = 2,
        Relic = 3
    }
}
