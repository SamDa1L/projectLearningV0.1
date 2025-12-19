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

    public int Priority { get; private set; }
    public bool Enabled { get; private set; }

    public DefaultRunAbility(PlayerController playerController, int priority, bool enabled)
    {
        this.playerController = playerController;
        this.Priority = priority;
        this.Enabled = enabled;
    }

    public bool OnRun(AbilityInput input)
    {
        // 封装原有 OnRun 逻辑
        if (input.Phase == AbilityInputPhase.Started)
        {
            playerController._isRunning = true;
        }
        else if (input.Phase == AbilityInputPhase.Canceled)
        {
            playerController._isRunning = false;
        }

        return true; // 消费输入
    }

    public bool OnMove(AbilityInput input) => false;
    public bool OnJump(AbilityInput input) => false;
    public bool OnAttack(AbilityInput input) => false;
    public bool OnRangedAttack(AbilityInput input) => false;
}
