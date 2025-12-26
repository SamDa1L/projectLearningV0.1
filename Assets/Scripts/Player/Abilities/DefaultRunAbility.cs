using UnityEngine;

/// <summary>
/// 默认奔跑能力（阶段 3B）
///
/// 封装原有 PlayerController.OnRun 的业务逻辑：
/// - Started: 启用奔跑
/// - Canceled: 禁用奔跑
/// </summary>
public class DefaultRunAbility : IPlayerAbility
{
    private PlayerController playerController;

    public string AbilityId { get; private set; }
    public int Priority { get; private set; }
    public bool Enabled { get; set; }

    public DefaultRunAbility(PlayerController playerController, string abilityId, int priority, bool enabled)
    {
        this.playerController = playerController;
        this.AbilityId = abilityId;
        this.Priority = priority;
        this.Enabled = enabled;
    }

    public bool OnRun(AbilityInput input)
    {
        // 使用 PlayerController 的公开 API 设置奔跑状态
        // 这会使用 IsRunning 属性 setter，保证 Animator 参数同步
        if (input.Phase == AbilityInputPhase.Started)
        {
            playerController.SetRunning(true);
        }
        else if (input.Phase == AbilityInputPhase.Canceled)
        {
            playerController.SetRunning(false);
        }

        return true; // 消费输入
    }

    public bool OnMove(AbilityInput input) => false;
    public bool OnJump(AbilityInput input) => false;
    public bool OnAttack(AbilityInput input) => false;
    public bool OnRangedAttack(AbilityInput input) => false;
}
