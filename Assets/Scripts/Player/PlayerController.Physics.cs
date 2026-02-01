using UnityEngine;

public partial class PlayerController
{
    /// <summary>
    /// FixedUpdate生命周期函数
    /// 每个物理帧调用一次，用于更新物理相关的逻辑
    ///
    /// 功能:
    /// - 判断是否在爬墙：IsClimbing
    /// - 如果爬墙: 使用climbInput和climbSpeed控制垂直速度
    /// - 如果正常: 根据CurrentMoveSpeed和水平输入更新速度
    /// - 将Y轴速度同步到Animator，用于控制下落/上升动画
    ///
    /// 爬墙物理:
    /// - X轴速度 = 0 (完全贴住墙壁)
    /// - Y轴速度 = climbInput.y × climbSpeed (由玩家输入控制)
    ///
    /// 正常物理:
    /// - X轴速度 = moveInputHorizontal × CurrentMoveSpeed
    /// - Y轴速度 = 保持不变(由重力和跳跃控制)
    /// </summary>
    private void FixedUpdate()
    {
        // 判断爬墙状态并应用对应的物理
        if (IsClimbing)
        {
            // 爬墙模式: X轴锁定为0(贴在墙上), Y轴由爬墙输入控制
            rb.velocity = new Vector2(0, climbInput.y * climbSpeed);

            // 同步爬墙速度到Animator的climbSpeed参数
            // 用于驱动爬墙动画的混合(向上/停止/向下)
            animator.SetFloat(AnimationStrings.climbSpeed, climbInput.y);
        }
        else if (!damageable.LockVelocity)
        {
            // 正常模式: 使用水平输入和当前速度
            rb.velocity = new Vector2(moveInputHorizontal * CurrentMoveSpeed, rb.velocity.y);
        }

        // 将当前Y轴速度同步到Animator
        // 用于控制下落/上升动画，以及到达最高点时的转换
        animator.SetFloat(AnimationStrings.yVelocity, rb.velocity.y);
    }

    public void OnHit(int damage, Vector2 knockback)
    {
        rb.velocity = new Vector2(knockback.x, rb.velocity.y + knockback.y);
    }
}

