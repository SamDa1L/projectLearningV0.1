using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 可伤害对象的统计数据
/// 用于Configure方法传递参数
///
/// 阶段2A新增：
/// - 集中管理所有可伤害对象的参数
/// - 支持从EnemyTuningProfile批量设置
/// - 便于参数验证和日志记录
/// </summary>
[System.Serializable]
public class DamageableStats
{
    /// <summary>
    /// 最大生命值
    /// </summary>
    public float maxHealth = 100f;

    /// <summary>
    /// 无敌帧时长（秒）
    /// </summary>
    public float invincibilityTime = 0.25f;

    /// <summary>
    /// 击退倍数
    /// 用于调整击退力度
    /// </summary>
    public float knockbackMultiplier = 1f;

    public override string ToString()
    {
        return $"DamageableStats[maxHealth={maxHealth}, invincibilityTime={invincibilityTime}, knockbackMultiplier={knockbackMultiplier}]";
    }
}

public class Damageable : MonoBehaviour
{
    public UnityEvent<int, Vector2> damageableHit;
    public UnityEvent damageableDeath;

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
    /// 阶段2A新增：用于调整击退力度
    /// </summary>
    public float knockbackMultiplier = 1f;

    /// <summary>
    /// 数据变更事件
    /// 阶段2A新增：当通过Configure()方法更新参数时触发
    /// </summary>
    public event Action<DamageableStats> DamageableStateChanged;

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


    /// <summary>
    /// 配置可伤害对象的参数
    ///
    /// 阶段2A新增：
    /// - 从DamageableStats批量设置参数
    /// - 触发DamageableStateChanged事件
    /// - 用于从EnemyTuningProfile应用参数
    ///
    /// 使用示例：
    /// damageable.Configure(new DamageableStats
    /// {
    ///     maxHealth = 100,
    ///     invincibilityTime = 0.5f,
    ///     knockbackMultiplier = 1.5f
    /// });
    /// </summary>
    public void Configure(DamageableStats stats)
    {
        if (stats == null)
        {
            Debug.LogWarning($"[{gameObject.name}] DamageableStats为空，跳过配置", gameObject);
            return;
        }

        // 设置参数
        MaxHealth = (int)stats.maxHealth;
        _health = MaxHealth; // 重置当前生命值
        invincibilityTime = stats.invincibilityTime;
        knockbackMultiplier = stats.knockbackMultiplier;

        // 触发事件
        DamageableStateChanged?.Invoke(stats);

        Debug.Log($"[{gameObject.name}] ✓ Damageable已配置: {stats}", gameObject);
    }

    private void Awake()
    {
        animator = GetComponent<Animator>();
    }




    public bool Hit(int damage, Vector2 knockback)
    {
        if(IsAlive && !isInvincible)
        {
            Health -= damage;
            isInvincible = true;

            animator.SetTrigger(AnimationStrings.hitTrigger);
            LockVelocity = true;
            damageableHit?.Invoke(damage, knockback);

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
