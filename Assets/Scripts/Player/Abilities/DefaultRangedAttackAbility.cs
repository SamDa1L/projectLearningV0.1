using UnityEngine;

/// <summary>
/// 默认远程攻击能力（阶段 3B）
///
/// 0.5 约束：玩家基础能力不包含远程攻击。
/// 远程攻击（投射物/法术）应由拾取获得的能力（如 kind=Projectile）负责。
/// </summary>
public class DefaultRangedAttackAbility : IPlayerAbility
{
    private readonly PlayerController playerController;
    private readonly Animator animator;

    public string AbilityId { get; private set; }
    public int Priority { get; private set; }
    public bool Enabled { get; set; }

    public DefaultRangedAttackAbility(PlayerController playerController, string abilityId, int priority, bool enabled)
    {
        this.playerController = playerController;
        this.animator = playerController != null ? playerController.GetComponent<Animator>() : null;
        this.AbilityId = abilityId;
        this.Priority = priority;
        this.Enabled = enabled;
    }

    public bool OnRangedAttack(AbilityInput input)
    {
        return false;
    }

    public bool OnMove(AbilityInput input) => false;
    public bool OnRun(AbilityInput input) => false;
    public bool OnJump(AbilityInput input) => false;
    public bool OnAttack(AbilityInput input) => false;
}
