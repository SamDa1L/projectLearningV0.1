using UnityEngine;

/// <summary>
/// 默认跳跃能力（阶段 3B）
///
/// 封装原有 PlayerController.OnJump 的业务逻辑：
/// - 检查跳跃条件（地面/壁跳）
/// - 触发动画和物理
/// </summary>
public class DefaultJumpAbility : IPlayerAbility
{
    private PlayerController playerController;
    private Rigidbody2D rb;
    private Animator animator;
    private TouchingDirections touchingDirections;

    public string AbilityId { get; private set; }
    public int Priority { get; private set; }
    public bool Enabled { get; set; }
    public float CooldownSeconds => 0f;
    public float CooldownRemaining => 0f;

    public DefaultJumpAbility(PlayerController playerController, string abilityId, int priority, bool enabled)
    {
        this.playerController = playerController;
        this.rb = playerController.GetComponent<Rigidbody2D>();
        this.animator = playerController.GetComponent<Animator>();
        this.touchingDirections = playerController.GetComponent<TouchingDirections>();
        this.AbilityId = abilityId;
        this.Priority = priority;
        this.Enabled = enabled;
    }

    public bool OnJump(AbilityInput input)
    {
        // 只响应 Started 阶段
        if (input.Phase != AbilityInputPhase.Started)
        {
            return false;
        }

        if (!playerController.CanMove)
        {
            return false;
        }

        // 支持地面跳跃或壁跳
        bool canJumpFromGround = touchingDirections.IsGrounded;
        bool canJumpFromWall = playerController._isClimbing && touchingDirections.IsOnWall;

        if (canJumpFromGround || canJumpFromWall)
        {
            // 触发Animator的跳跃动画
            animator.SetTrigger(AnimationStrings.jumpTrigger);

            if (canJumpFromWall)
            {
                // 壁跳逻辑
                float wallJumpForce = 8f;
                float horizontalForce = playerController._isFacingRight ? -wallJumpForce : wallJumpForce;
                rb.velocity = new Vector2(horizontalForce, playerController.jumpImpules);
                playerController._isClimbing = false;
            }
            else
            {
                // 地面跳跃
                rb.velocity = new Vector2(rb.velocity.x, playerController.jumpImpules);
            }

            return true; // 消费输入
        }

        return false;
    }

    public bool OnMove(AbilityInput input) => false;
    public bool OnRun(AbilityInput input) => false;
    public bool OnAttack(AbilityInput input) => false;
    public bool OnRangedAttack(AbilityInput input) => false;
}
