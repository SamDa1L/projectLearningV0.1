using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 伤害系统组件
/// 管理角色的生命值、无敌帧、击退等伤害相关逻辑
///
/// 功能：
/// - 生命值管理（受伤、治疗、死亡）
/// - 无敌帧管理
/// - 击退应用
/// - 事件通知
/// - 运行时配置（Configure方法）
/// </summary>
public class Damageable : MonoBehaviour
{
    public UnityEvent<int, Vector2> damageableHit;
    public UnityEvent damageableDeath;
    public event Action<DamageableStats> DamageableStateChanged;

    Animator animator;

    [SerializeField]
    private int _maxHealth = 100;

    public int MaxHealth
    {
        get
        {
            return _maxHealth;
        }
        set
        {
            _maxHealth = value;
        }
    }

    [SerializeField]
    private int _health = 100;

    public int Health
    {
        get
        {
            return _health;
        }
        set
        {
            _health = value;

            // 当生命值小于等于0时，角色死亡
            if(_health <= 0)
            {
                IsAlive = false;
            }
        }
    }

    [SerializeField]
    private bool _isAlive = true;

    [SerializeField]
    public bool isInvincible = false;

    /// <summary>
    /// 无敌状态属性（符合C#命名规范）
    /// 用于查询当前是否处于无敌帧内
    /// </summary>
    public bool IsInvulnerable
    {
        get { return isInvincible; }
    }

    private float timeSinceHit = 0;
    public float invincibilityTime = 0.25f;

    /// <summary>
    /// 击退倍数
    /// 用于调整击退力度
    /// </summary>
    public float knockbackMultiplier = 1f;

    public bool IsAlive 
    {
        get
        {
            return _isAlive;
        }
        set
        {
            _isAlive = value;
            animator.SetBool(AnimationStrings.isAlive, value);
            Debug.Log("死亡: " +  value);

            if(value == false )
            {
                damageableDeath.Invoke();
            }

        }
    }

    public bool LockVelocity
    {
        get
        {
            return animator.GetBool(AnimationStrings.lockVelocity);
        }
        set
        {
            animator.SetBool(AnimationStrings.lockVelocity, value);
        }
    }


    private void Awake()
    {
        animator = GetComponent<Animator>();
    }

    /// <summary>
    /// 配置Damageable的参数
    /// 用于在运行时设置生命值、无敌时间、击退倍数等
    /// 通常由EnemyAgentBase在Initialize时调用
    /// </summary>
    /// <param name="stats">包含所有配置参数的DamageableStats对象</param>
    public void Configure(DamageableStats stats)
    {
        if (stats == null)
        {
            Debug.LogWarning($"[Damageable] {gameObject.name} - DamageableStats为空，无法配置");
            return;
        }

        MaxHealth = stats.maxHealth;
        _health = stats.maxHealth;
        invincibilityTime = stats.invincibilityTime;
        knockbackMultiplier = stats.knockbackMultiplier;

        // 触发状态变更事件
        DamageableStateChanged?.Invoke(stats);

        #if UNITY_EDITOR
        Debug.Log($"[Damageable] {gameObject.name} 已配置: HP={stats.maxHealth}, 无敌时间={stats.invincibilityTime}, 击退倍数={stats.knockbackMultiplier}");
        #endif
    }

    public bool Hit(int damage, Vector2 knockback)
    {
        if(IsAlive && !isInvincible)
        {
            Health -= damage;
            isInvincible = true;

            animator.SetTrigger(AnimationStrings.hitTrigger);
            LockVelocity = true;

            // 应用击退倍数
            Vector2 adjustedKnockback = knockback * knockbackMultiplier;
            damageableHit?.Invoke(damage, adjustedKnockback);

            CharacterEvents.characterDamaged.Invoke(gameObject, damage);

            return true;
        }

        // 无法被伤害
        return false;
    }


    // 角色是否被恢复血
    public bool Heal(int healthRestore)
    {
        if (IsAlive && Health < MaxHealth) 
        {
            int maxHeal = Mathf.Max(MaxHealth - Health, 0);
            int actualHeal = Mathf.Min(maxHeal, healthRestore);
            Health += actualHeal;
            CharacterEvents.characterHealed(gameObject, actualHeal);
            return true;


        }
        return false;


    }


    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (isInvincible)
        {
            if(timeSinceHit > invincibilityTime)
            {
                // 取消无敌状态
                isInvincible = false ;
                timeSinceHit = 0;
            }
            timeSinceHit += Time.deltaTime;

        }
    }
}
