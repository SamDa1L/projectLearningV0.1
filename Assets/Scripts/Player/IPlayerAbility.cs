/// <summary>
/// 玩家能力接口（阶段 3B）
///
/// 定义统一的能力执行契约：
/// - AbilityId：能力唯一标识符（用于 AbilitySystem 查询和控制）
/// - Priority：优先级（数值越大越先执行）
/// - Enabled：是否启用（Phase 5：支持运行时动态修改）
/// - 5 个输入入口：返回 bool 表示是否消费输入（handled）
/// </summary>
public interface IPlayerAbility
{
    /// <summary>
    /// 能力 ID（对应 AbilityCatalog 中的 id，用于运行时查询和控制）
    /// Phase 5 新增：支持 AbilitySystem.SetAbilityEnabled(abilityId, enabled)
    /// </summary>
    string AbilityId { get; }

    /// <summary>
    /// 优先级（数值越大越先执行）
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// 是否启用（Phase 5：支持运行时动态修改）
    /// - get: 查询当前启用状态
    /// - set: 由 AbilitySystem.FlushPendingChanges() 调用
    /// </summary>
    bool Enabled { get; set; }

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

    // Phase 8: expose cooldown for HUD/debug (0 = no cooldown)
    float CooldownSeconds { get; }
    float CooldownRemaining { get; }
}
