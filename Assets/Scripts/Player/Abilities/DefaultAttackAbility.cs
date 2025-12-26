using UnityEngine;

/// <summary>
/// 默认攻击能力（阶段 3B）
///
/// 封装原有 PlayerController.OnAttack 的业务逻辑：
/// - 触发攻击动画
/// </summary>
public class DefaultAttackAbility : IPlayerAbility
{
    private Animator animator;

    public string AbilityId { get; private set; }
    public int Priority { get; private set; }
    public bool Enabled { get; set; }

    public DefaultAttackAbility(PlayerController playerController, string abilityId, int priority, bool enabled)
    {
        this.animator = playerController.GetComponent<Animator>();
        this.AbilityId = abilityId;
        this.Priority = priority;
        this.Enabled = enabled;
    }

    public bool OnAttack(AbilityInput input)
    {
        // 只响应 Started 阶段
        if (input.Phase == AbilityInputPhase.Started)
        {
            animator.SetTrigger(AnimationStrings.attackTrigger);
            return true; // 消费输入
        }

        return false;
    }

    public bool OnMove(AbilityInput input) => false;
    public bool OnRun(AbilityInput input) => false;
    public bool OnJump(AbilityInput input) => false;
    public bool OnRangedAttack(AbilityInput input) => false;
}
