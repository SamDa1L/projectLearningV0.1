using UnityEngine;

/// <summary>
/// 默认移动能力（阶段 3B）
///
/// 封装原有 PlayerController.OnMove 的业务逻辑：
/// - 处理移动输入（WASD）
/// - 爬墙状态判断
/// - 朝向更新
/// </summary>
public class DefaultMoveAbility : IPlayerAbility
{
    private PlayerController playerController;

    public string AbilityId { get; private set; }
    public int Priority { get; private set; }
    public bool Enabled { get; set; }
    public float CooldownSeconds => 0f;
    public float CooldownRemaining => 0f;

    public DefaultMoveAbility(PlayerController playerController, string abilityId, int priority, bool enabled)
    {
        this.playerController = playerController;
        this.AbilityId = abilityId;
        this.Priority = priority;
        this.Enabled = enabled;
    }

    public bool OnMove(AbilityInput input)
    {
        // 使用 PlayerController 的公开 API 应用移动输入
        // 这会更新输入缓存、爬墙逻辑、IsMoving 状态和朝向
        playerController.ApplyMoveInput(input.Move);

        return true; // 消费输入
    }

    public bool OnRun(AbilityInput input) => false;
    public bool OnJump(AbilityInput input) => false;
    public bool OnAttack(AbilityInput input) => false;
    public bool OnRangedAttack(AbilityInput input) => false;
}
