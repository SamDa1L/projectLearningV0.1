using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FlyingEye : MonoBehaviour
{
    public float flightSpeed = 2f;
    public float waypointReachedDistance = 0.1f;
    public Collider2D deathCollider;
    public DetectionZone bitDetectionZone;
    public List<Transform> waypoints;


    Animator animator;
    Rigidbody2D rb;
    Damageable damageable;


    Transform nextWaypoint;
    int waypointNum = 0;


    public bool _hasTarget = false;


    public bool HasTarget
    {
        get
        {
            return _hasTarget;
        }
        private set
        {
            _hasTarget = value;
            animator.SetBool(AnimationStrings.hasTarget, value);
        }
    }

    public bool CanMove
    {
        get
        {
            return animator.GetBool(AnimationStrings.canMove);
        }
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        damageable = GetComponent<Damageable>();
    }



    // Start is called before the first frame update
    void Start()
    {
        nextWaypoint = waypoints[waypointNum];
    }


    private void OnEnable()
    {
        damageable.damageableDeath.AddListener(OnDeath);
    }

    // Update is called once per frame
    void Update()
    {
        HasTarget = bitDetectionZone.detectedColliders.Count > 0;
    }

    private void FixedUpdate()
    {
        if (damageable.IsAlive)
        {
            if (CanMove)
            {
                Flight();
            }
            else
            {
                rb.velocity = Vector3.zero;
            }
        }
    }

    private void Flight()
    {
        //飞向下一个目标点
        Vector2 directionToWaypoint = (nextWaypoint.position - transform.position).normalized;

        //检查是否已经到达下一个目标点
        float distance = Vector2.Distance(nextWaypoint.position, transform.position);

        rb.velocity = directionToWaypoint * flightSpeed;
        UpdateDirection();

        //检查是否需要切换目标点
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
            //面向右
            if(rb.velocity.x < 0)
            {
                //翻转
                transform.localScale = new Vector3(-1 * locScale.x, locScale.y, locScale.z);
            }
        }
        else
        {
            //面相左
            if (rb.velocity.x > 0)
            {
                //翻转
                transform.localScale = new Vector3(-1 * locScale.x, locScale.y, locScale.z);
            }

        }
    }

    public void OnDeath()
    {
        //死亡后坠落到地面
        rb.gravityScale = 2f;
        rb.velocity = new Vector2(0, rb.velocity.y);
        deathCollider.enabled = true;
    }

}
