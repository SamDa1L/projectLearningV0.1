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
    }

    /// <summary>
    /// 状态逻辑更新
    /// </summary>
    protected override void TickState(float deltaTime)
    {
        // 根据DetectionZone更新HasTarget
        bool hasTarget = GetDetectedTargets().Count > 0;

        // 设置Animator参数
        animator.SetBool(AnimationStrings.hasTarget, hasTarget);
    }

    /// <summary>
    /// 物理更新
    /// </summary>
    protected override void TickPhysics(float fixedDeltaTime)
    {
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
