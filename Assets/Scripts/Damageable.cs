using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 传递给 Damageable 的数据包
/// 用于从配置（如 EnemyTuningProfile、PlayerConfig）向 Damageable 组件传递数值
/// </summary>
public struct DamageableStats
{
    public float maxHealth;  // 阶段 3A: 迁移为 float 以支持玩家小数生命值
    public float invincibilityTime;
    public float knockbackMultiplier;
}

	public class Damageable : MonoBehaviour
	{
	    public UnityEvent<int, Vector2> damageableHit = new UnityEvent<int, Vector2>();
	    public UnityEvent damageableDeath = new UnityEvent();
	    public event System.Action<DamageableStats> DamageableStateChanged;

	    /// <summary>
	    /// Phase 7: 生命值变化事件（用于 HUD 订阅）
	    /// 契约 [C-Runtime-6]: 必须在 Health 变化时触发，参数为 (current, max)
	    /// </summary>
	    public event Action<int, int> OnHealthChanged;

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
    private float _maxHealth = 100;

    public float MaxHealth
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

    /// <summary>
    /// Phase 7: CurrentHealth 只读属性（用于 HUD 初始刷新）
    /// 契约 [C-Runtime-6]: 返回当前生命值（四舍五入到 int）
    /// </summary>
    public int CurrentHealth => Mathf.RoundToInt(_health);

    [SerializeField]
    private float _health = 100;

    public float Health
    {
        get
        {
            return _health;
        }
        set
        {
            // Phase 7: 计算变化前后的 int 值，只有实际变化时才触发事件
            int oldHealthInt = Mathf.RoundToInt(_health);
            _health = value;
            int newHealthInt = Mathf.RoundToInt(_health);

            // 触发 OnHealthChanged 事件（Phase 7: 契约 [C-Runtime-6]）
            if (oldHealthInt != newHealthInt)
            {
                int current = Mathf.Clamp(newHealthInt, 0, Mathf.RoundToInt(_maxHealth));
                int max = Mathf.RoundToInt(_maxHealth);
                OnHealthChanged?.Invoke(current, max);
            }

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
    /// Phase 7: 配置后必须触发 OnHealthChanged 事件进行初始刷新
    /// </summary>
    /// <param name="stats">包含配置数值的结构体</param>
    public void Configure(DamageableStats? stats)
    {
        if (stats.HasValue)
        {
            var value = stats.Value;
            MaxHealth = value.maxHealth;
            Health = value.maxHealth;  // 初始化当前生命为最大生命（会触发 OnHealthChanged）
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
            if (invincibilityTime > 0f)
            {
                isInvincible = true;
                timeSinceHit = 0f;
            }

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
            // 阶段 3A: maxHealth 已迁移为 float，使用 float 运算并 clamp
            float maxHeal = Mathf.Max(MaxHealth - Health, 0);
            float actualHeal = Mathf.Min(maxHeal, healthRestore);
            Health += actualHeal;

            // 事件仍使用 int，对实际治疗量四舍五入
            int actualHealInt = Mathf.RoundToInt(actualHeal);
            CharacterEvents.characterHealed?.Invoke(gameObject, actualHealInt);
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
