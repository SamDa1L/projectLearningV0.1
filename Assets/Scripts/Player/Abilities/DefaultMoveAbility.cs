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

    public int Priority { get; private set; }
    public bool Enabled { get; private set; }

    public DefaultMoveAbility(PlayerController playerController, int priority, bool enabled)
    {
        this.playerController = playerController;
        this.Priority = priority;
        this.Enabled = enabled;
    }

    public bool OnMove(AbilityInput input)
    {
        // 封装原有 OnMove 逻辑
        Vector2 moveInput = input.Move;

        if (!playerController.IsAlive)
        {
            playerController._isMoving = false;
            return true; // 消费输入（死亡时不传播）
        }

        // 分离水平和垂直输入分量
        float moveInputHorizontal = moveInput.x;
        float moveInputVertical = moveInput.y;

        // 注意：这里需要访问 PlayerController 的私有字段
        // 我们需要将这些字段改为 public 或提供 accessor 方法

        // 爬墙逻辑判断
        var touchingDirections = playerController.GetComponent<TouchingDirections>();
        if (touchingDirections.IsOnWall && moveInputVertical != 0 && playerController.CanMove)
        {
            playerController._isClimbing = true;
        }
        else if (!touchingDirections.IsOnWall || moveInputVertical == 0)
        {
            playerController._isClimbing = false;
        }

        // 根据爬墙状态更新行走状态和朝向
        if (!playerController._isClimbing)
        {
            playerController._isMoving = moveInputHorizontal != 0;
            SetFacingDirection(moveInput);
        }
        else
        {
            playerController._isMoving = false;
        }

        return true; // 消费输入
    }

    private void SetFacingDirection(Vector2 moveInput)
    {
        if (moveInput.x > 0 && !playerController._isFacingRight)
        {
            playerController._isFacingRight = true;
        }
        else if (moveInput.x < 0 && playerController._isFacingRight)
        {
            playerController._isFacingRight = false;
        }
    }

    public bool OnRun(AbilityInput input) => false;
    public bool OnJump(AbilityInput input) => false;
    public bool OnAttack(AbilityInput input) => false;
    public bool OnRangedAttack(AbilityInput input) => false;
}
