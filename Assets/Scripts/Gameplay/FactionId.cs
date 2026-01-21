/// <summary>
/// 阵营枚举（0.5 Summon 扩展）。
/// 说明：
/// - Enemy / Friend / Neutral：实际阵营（仅这三种参与敌对判定）
/// - None：表示“未配置/空值”（对应数据侧的 null），通常只允许出现在“可选覆写”字段里
/// </summary>
public enum FactionId
{
    Enemy = 0,
    Friend = 1,
    Neutral = 2,
    None = 3
}
