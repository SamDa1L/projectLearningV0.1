using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FlyingEye : EnemyAgentBase
{
    public float flightSpeed = 2f;
    public float waypointReachedDistance = 0.1f;
    public Collider2D deathCollider;
    public DetectionZone bitDetectionZone;
    public List<Transform> waypoints;

    Transform nextWaypoint;
    int waypointNum = 0;

    // 攻击系统（最小实现）
    private float attackCooldownTimer = 0f;
    private bool hasTarget = false;  // 由检测区事件驱动更新（不再每帧查询）



    /// <summary>
    /// 初始化FlyingEye特有的参数
    /// </summary>
    protected override void Initialize()
    {
        base.Initialize();

        // 初始化航点
        if (waypoints != null && waypoints.Count > 0)
        {
            nextWaypoint = waypoints[waypointNum];
        }

        // 订阅检测区事件（Event-Driven，符合文档第1节要求）
        DetectionZone primaryAttackZone = GetZone(DetectionZoneBinding.Role.PrimaryAttack);
        if (primaryAttackZone != null)
        {
            primaryAttackZone.OnTargetEnter.AddListener(OnTargetEnter);
            primaryAttackZone.NoColliderRemain.AddListener(OnAllTargetsExit);
        }
        else
        {
            Debug.LogError($"[FlyingEye] 未配置 PrimaryAttack 检测区！无法订阅目标进入/离开事件。", gameObject);
        }
    }

    /// <summary>
    /// 检测区事件：有目标进入
    /// </summary>
    private void OnTargetEnter()
    {
        hasTarget = true;

        if (debugStateOverlay)
        {
            Debug.Log($"[FlyingEye] 目标进入检测区", gameObject);
        }
    }

    /// <summary>
    /// 检测区事件：所有目标都已离开
    /// </summary>
    private void OnAllTargetsExit()
    {
        hasTarget = false;

        if (debugStateOverlay)
        {
            Debug.Log($"[FlyingEye] 所有目标离开检测区", gameObject);
        }
    }

    /// <summary>
    /// 状态逻辑更新
    /// </summary>
    protected override void TickState(float deltaTime)
    {
        // hasTarget 现在由检测区事件驱动更新（不再每帧查询）
        // 符合文档第1节："检测区判定后先发事件通知 NPC"

        // 设置Animator参数
        animator.SetBool(AnimationStrings.hasTarget, hasTarget);

        // 方案A：冷却计时与 hasTarget 解耦，按真实时间流逝
        // 无论是否有目标，冷却计时器都随时间递减
        if (attackCooldownTimer > 0f)
        {
            attackCooldownTimer -= deltaTime;
        }

        // 攻击逻辑：必须满足两层判定：1）hasTarget == true 2）冷却归零
        if (hasTarget && attackCooldownTimer <= 0f)
        {
            // 调用基类的统一攻击入口
            TriggerAttackAnimation();

            // 重置冷却计时器（使用 Profile 下发的冷却时间）
            attackCooldownTimer = AttackCooldown;

            if (debugStateOverlay)
            {
                Debug.Log($"[FlyingEye] 触发攻击 - Cooldown={AttackCooldown}s, Damage={AttackDamage}, Range={AttackRange}");
            }
        }
    }

    /// <summary>
    /// 物理更新
    /// </summary>
    protected override void TickPhysics(float fixedDeltaTime)
    {
        // 击退保护期间，跳过所有移动逻辑，让击退速度自然生效
        if (IsKnockbackProtected)
        {
            return;
        }

        // 只在活着且能移动时执行飞行逻辑
        if (damageable.IsAlive && animator.GetBool(AnimationStrings.canMove))
        {
            Flight();
        }
        else
        {
            rb2d.velocity = Vector3.zero;
        }
    }

    /// <summary>
    /// 死亡时处理
    /// </summary>
    protected override void ExitState(EnemyState oldState)
    {
        base.ExitState(oldState);

        if (oldState == EnemyState.Dead)
        {
            OnDeath();
        }
    }

    private void Flight()
    {
        //计算指向下一个目标点的方向
        Vector2 directionToWaypoint = (nextWaypoint.position - transform.position).normalized;

        //计算是否已经到达了一个目标点
        float distance = Vector2.Distance(nextWaypoint.position, transform.position);

        // 使用 Profile 下发的 MoveSpeed
        rb2d.velocity = directionToWaypoint * MoveSpeed;
        UpdateDirection();

        //计算是否需要切换目标点
        if(distance <= waypointReachedDistance)
        {
            waypointNum++;

            if(waypointNum >= waypoints.Count)
            {
                waypointNum = 0;
            }

            nextWaypoint = waypoints[waypointNum];

        }

    }

    private void UpdateDirection()
    {
        Vector3 locScale = transform.localScale;

        if(transform.localScale.x > 0)
        {
            //向右飞
            if(rb2d.velocity.x < 0)
            {
                //翻转
                transform.localScale = new Vector3(-1 * locScale.x, locScale.y, locScale.z);
            }
        }
        else
        {
            //向左飞
            if (rb2d.velocity.x > 0)
            {
                //翻转
                transform.localScale = new Vector3(-1 * locScale.x, locScale.y, locScale.z);
            }

        }
    }

    public void OnDeath()
    {
        //掉落时增加重力
        rb2d.gravityScale = 2f;
        rb2d.velocity = new Vector2(0, rb2d.velocity.y);
        deathCollider.enabled = true;
    }
}
