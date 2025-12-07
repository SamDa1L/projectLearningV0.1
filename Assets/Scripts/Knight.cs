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
    public DetectionZone attackZone;
    public DetectionZone cliffDetectionZone;

    TouchingDirections touchingDirections;

    public enum WalkableDirection { Right,Left};


    private WalkableDirection _walkDirection;
    private Vector2 walkDirectionVector = Vector2.right;

    public WalkableDirection WalkDirection
    {
        get
        {
            return _walkDirection;
        }
        set
        {
            if (_walkDirection != value)
            {
                gameObject.transform.localScale = new Vector2(gameObject.transform.localScale.x * -1, gameObject.transform.localScale.y);
                if (value == WalkableDirection.Right)
                {
                    walkDirectionVector = Vector2.right;
                }
                else if(value == WalkableDirection.Left)
                {
                    walkDirectionVector = Vector2.left;
                }
            }
            _walkDirection = value;
        }
    }

    /// <summary>
    /// 初始化Knight特有的组件和参数
    /// </summary>
    protected override void Initialize()
    {
        base.Initialize();

        // 缓存Knight特有的组件
        touchingDirections = GetComponent<TouchingDirections>();

        // 初始化默认方向
        WalkDirection = WalkableDirection.Right;
    }

    /// <summary>
    /// 状态逻辑更新
    /// </summary>
    protected override void TickState(float deltaTime)
    {
        // 根据DetectionZone更新HasTarget
        bool hasTarget = GetDetectedTargets().Count > 0;
        if (hasTarget != (currentState == EnemyState.Chase))
        {
            if (hasTarget)
            {
                SetState(EnemyState.Chase);
            }
            else
            {
                SetState(EnemyState.Idle);
            }
        }

        // 设置Animator参数
        animator.SetBool(AnimationStrings.hasTarget, hasTarget);
    }

    /// <summary>
    /// 物理更新
    /// </summary>
    protected override void TickPhysics(float fixedDeltaTime)
    {
        // 崖边检测和转身
        if (touchingDirections.IsGrounded && touchingDirections.IsOnWall)
        {
            FlipDirection();
        }

        // 移动逻辑
        if (!damageable.LockVelocity)
        {
            if (animator.GetBool(AnimationStrings.canMove) && touchingDirections.IsGrounded)
            {
                rb2d.velocity = new Vector2(
                    Mathf.Clamp(
                        rb2d.velocity.x + (walkAcceleration * walkDirectionVector.x * Time.fixedDeltaTime),
                        -maxSpeed,
                        maxSpeed
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
            Debug.LogError("��ǰ�ƶ����򲻺Ϸ����Ȳ�����Ҳ������");
        }
    }

    /// <summary>
    /// 崖边检测回调
    ///
    /// v0.2更新：现在推荐使用GetZone() API而不是直接访问cliffDetectionZone字段
    ///
    /// 旧用法（v0.1）：
    ///   DetectionZone cliffZone = cliffDetectionZone;
    ///   if (cliffZone != null && cliffZone.detectedColliders.Count > 0) { ... }
    ///
    /// 新用法（v0.2）：
    ///   DetectionZone cliffZone = GetZone(DetectionZoneBinding.Role.Cliff);
    ///   if (cliffZone != null && cliffZone.detectedColliders.Count > 0) { ... }
    ///
    /// 好处：
    /// - 更灵活：不需要在Inspector中为每个检测区都添加public字段
    /// - 更可维护：统一通过Role访问，易于理解职能
    /// - 为未来预留：支持无限数量的检测区扩展
    /// </summary>
    public void OnCliffDetected()
    {
        if (touchingDirections.IsGrounded)
        {
            FlipDirection();
        }
    }
}
