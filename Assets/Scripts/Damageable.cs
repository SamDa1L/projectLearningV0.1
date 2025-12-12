using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 传递给 Damageable 的数据包
/// 用于从配置（如 EnemyTuningProfile）向 Damageable 组件传递数值
/// </summary>
public struct DamageableStats
{
    public int maxHealth;
    public float invincibilityTime;
    public float knockbackMultiplier;
}

	public class Damageable : MonoBehaviour
	{
	    public UnityEvent<int, Vector2> damageableHit = new UnityEvent<int, Vector2>();
	    public UnityEvent damageableDeath = new UnityEvent();
	    public event System.Action<DamageableStats> DamageableStateChanged;

    private Animator animator;

    private void EnsureAnimator()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>();
        }
    }

    private void EnsureEvents()
    {
        if (damageableHit == null)
        {
            damageableHit = new UnityEvent<int, Vector2>();
        }

        if (damageableDeath == null)
        {
            damageableDeath = new UnityEvent();
        }
    }

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

    [SerializeField]
    public float knockbackMultiplier = 1f;

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

    public bool IsAlive 
    {
        get
        {
            return _isAlive;
        }
        set
        {
            _isAlive = value;
            EnsureAnimator();
            if (animator != null)
            {
                animator.SetBool(AnimationStrings.isAlive, value);
            }
            Debug.Log("死亡: " +  value);

            if(value == false )
            {
                damageableDeath?.Invoke();
            }

        }
    }

    public bool LockVelocity
    {
        get
        {
            EnsureAnimator();
            return animator != null && animator.GetBool(AnimationStrings.lockVelocity);
        }
        set
        {
            EnsureAnimator();
            if (animator != null)
            {
                animator.SetBool(AnimationStrings.lockVelocity, value);
            }
        }
    }


    private void Awake()
    {
        EnsureAnimator();
        EnsureEvents();
    }

    /// <summary>
    /// 使用 DamageableStats 配置此组件
    /// 将传入的数值应用到组件（最大生命、当前生命、无敌时间等）
    /// </summary>
    /// <param name="stats">包含配置数值的结构体</param>
    public void Configure(DamageableStats? stats)
    {
        if (stats.HasValue)
        {
            var value = stats.Value;
            MaxHealth = value.maxHealth;
            Health = value.maxHealth;  // 初始化当前生命为最大生命
            invincibilityTime = value.invincibilityTime;
            knockbackMultiplier = value.knockbackMultiplier;

            DamageableStateChanged?.Invoke(value);
        }
    }




    public bool Hit(int damage, Vector2 knockback)
    {
        if(IsAlive && !isInvincible)
        {
            Health -= damage;
            isInvincible = true;

            EnsureAnimator();
            if (animator != null)
            {
                animator.SetTrigger(AnimationStrings.hitTrigger);
                LockVelocity = true;
            }
            Vector2 scaledKnockback = knockback * knockbackMultiplier;
            damageableHit?.Invoke(damage, scaledKnockback);

            CharacterEvents.characterDamaged?.Invoke(gameObject, damage);

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
            CharacterEvents.characterHealed?.Invoke(gameObject, actualHeal);
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
