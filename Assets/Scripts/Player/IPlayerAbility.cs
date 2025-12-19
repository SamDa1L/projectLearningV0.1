/// <summary>
/// 玩家能力接口（阶段 3B）
///
/// 定义统一的能力执行契约：
/// - Priority：优先级（数值越大越先执行）
/// - Enabled：是否启用（镜像 CastleDB 配置）
/// - 5 个输入入口：返回 bool 表示是否消费输入（handled）
/// </summary>
public interface IPlayerAbility
{
    /// <summary>
    /// 优先级（数值越大越先执行）
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// 是否启用（镜像 CastleDB AbilityCatalogEntry.enabled）
    /// 仅作为只读/调试字段，不作为运行时禁用的数据源
    /// </summary>
    bool Enabled { get; }

    /// <summary>
    /// 移动输入回调
    /// </summary>
    /// <param name="input">输入快照</param>
    /// <returns>是否消费输入（true 则中止后续能力）</returns>
    bool OnMove(AbilityInput input);

    /// <summary>
    /// 奔跑输入回调
    /// </summary>
    /// <param name="input">输入快照</param>
    /// <returns>是否消费输入（true 则中止后续能力）</returns>
    bool OnRun(AbilityInput input);

    /// <summary>
    /// 跳跃输入回调
    /// </summary>
    /// <param name="input">输入快照</param>
    /// <returns>是否消费输入（true 则中止后续能力）</returns>
    bool OnJump(AbilityInput input);

    /// <summary>
    /// 近战攻击输入回调
    /// </summary>
    /// <param name="input">输入快照</param>
    /// <returns>是否消费输入（true 则中止后续能力）</returns>
    bool OnAttack(AbilityInput input);

    /// <summary>
    /// 远程攻击输入回调
    /// </summary>
    /// <param name="input">输入快照</param>
    /// <returns>是否消费输入（true 则中止后续能力）</returns>
    bool OnRangedAttack(AbilityInput input);
}
