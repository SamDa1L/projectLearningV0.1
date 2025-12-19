using UnityEngine;

/// <summary>
/// 默认远程攻击能力（阶段 3B）
///
/// 封装原有 PlayerController.OnRangedAttack 的业务逻辑：
/// - 触发远程攻击动画
/// </summary>
public class DefaultRangedAttackAbility : IPlayerAbility
{
    private Animator animator;

    public int Priority { get; private set; }
    public bool Enabled { get; private set; }

    public DefaultRangedAttackAbility(PlayerController playerController, int priority, bool enabled)
    {
        this.animator = playerController.GetComponent<Animator>();
        this.Priority = priority;
        this.Enabled = enabled;
    }

    public bool OnRangedAttack(AbilityInput input)
    {
        // 只响应 Started 阶段
        if (input.Phase == AbilityInputPhase.Started)
        {
            animator.SetTrigger(AnimationStrings.rangedAttackTrigger);
            return true; // 消费输入
        }

        return false;
    }

    public bool OnMove(AbilityInput input) => false;
    public bool OnRun(AbilityInput input) => false;
    public bool OnJump(AbilityInput input) => false;
    public bool OnAttack(AbilityInput input) => false;
}
