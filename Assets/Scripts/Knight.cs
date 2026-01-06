using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(TouchingDirections), typeof(Damageable))]

public class Knight : EnemyAgentBase
{
    public float walkAcceleration = 3f;
    public float maxSpeed = 3f;
    public float walkStopRate = 0.05f;

    TouchingDirections touchingDirections;

    public enum WalkableDirection { Right, Left };

    private WalkableDirection _walkDirection;
    private Vector2 walkDirectionVector = Vector2.right;

    public WalkableDirection WalkDirection
    {
        get { return _walkDirection; }
        set
        {
            if (_walkDirection != value)
            {
                gameObject.transform.localScale = new Vector2(gameObject.transform.localScale.x * -1, gameObject.transform.localScale.y);
                if (value == WalkableDirection.Right)
                {
                    walkDirectionVector = Vector2.right;
                }
                else if (value == WalkableDirection.Left)
                {
                    walkDirectionVector = Vector2.left;
                }
            }
            _walkDirection = value;
        }
    }

    protected override void Initialize()
    {
        base.Initialize();

        // 缓存Knight特有的组件
        touchingDirections = GetComponent<TouchingDirections>();

        // 初始化默认方向
        WalkDirection = WalkableDirection.Right;
    }

    protected override void TickState(float deltaTime)
    {
        // 调用基类的统一攻击系统更新方法
        // 子类可传入回调来做额外处理（如状态切换）
        TickAttackSystem(deltaTime, () =>
        {
            // 攻击触发时的子类额外处理（可选）
            if (debugStateOverlay)
            {
                Debug.Log($"[Knight] 攻击触发 - Damage={AttackDamage}, Range={AttackRange}");
            }
        });
    }

    protected override void TickPhysics(float fixedDeltaTime)
    {
        // 击退保护期间，跳过所有移动逻辑，让击退速度自然生效
        if (IsKnockbackProtected)
        {
            return;
        }

        // 崖边检测 - 使用v0.2新API
        if (touchingDirections.IsGrounded && touchingDirections.IsOnWall)
        {
            var cliffTargets = GetDetectedTargetsForRole(DetectionZoneBinding.Role.Cliff);
            if (cliffTargets.Count > 0)
            {
                OnCliffDetected();
            }
        }

        // 移动逻辑 - Step 2: 使用基类的 MoveSpeed 而不是 Inspector 的 maxSpeed
        if (!damageable.LockVelocity)
        {
            if (animator.GetBool(AnimationStrings.canMove) && touchingDirections.IsGrounded)
            {
                // 使用 Profile 下发的 MoveSpeed
                float targetSpeed = MoveSpeed;

                rb2d.velocity = new Vector2(
                    Mathf.Clamp(
                        rb2d.velocity.x + (walkAcceleration * walkDirectionVector.x * Time.fixedDeltaTime),
                        -targetSpeed,
                        targetSpeed
                    ),
                    rb2d.velocity.y
                );
            }
            else
            {
                rb2d.velocity = new Vector2(Mathf.Lerp(rb2d.velocity.x, 0, walkStopRate), rb2d.velocity.y);
            }
        }
    }

    private void FlipDirection()
    {
        if (WalkDirection == WalkableDirection.Right)
        {
            WalkDirection = WalkableDirection.Left;
        }
        else if (WalkDirection == WalkableDirection.Left)
        {
            WalkDirection = WalkableDirection.Right;
        }
        else
        {
            Debug.LogError("当前移动方向不合法，既不是右也不是左");
        }
    }

    /// <summary>
    /// 崖边检测回调
    ///
    /// v0.2迁移说明：
    /// - 旧方式（v0.1）：通过public DetectionZone cliffDetectionZone字段访问
    /// - 新方式（v0.2）：通过GetDetectedTargetsForRole(DetectionZoneBinding.Role.Cliff)访问
    ///
    /// 现在的工作流程：
    /// 1. zoneBindings中配置 DZ_Cliff 为 Cliff 角色的检测区
    /// 2. TickPhysics()通过GetDetectedTargetsForRole()查询目标
    /// 3. 如有目标则调用OnCliffDetected()
    /// </summary>
    public void OnCliffDetected()
    {
        if (touchingDirections.IsGrounded)
        {
            FlipDirection();
        }
    }

    public void OnWallDetected()
    {
        if (!touchingDirections.IsGrounded)
        {
            return;
        }

        FlipDirection();

        rb2d.velocity = new Vector2(0f, rb2d.velocity.y);
    }
}
