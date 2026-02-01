using UnityEngine;

public abstract partial class EnemyAgentBase
{
    public virtual void OnDamageTaken(int damage, Vector2 knockbackDirection)
    {
        // 应用击退
        if (rb2d != null)
        {
            rb2d.velocity = new Vector2(knockbackDirection.x, rb2d.velocity.y + knockbackDirection.y);
        }

        // 启动击退保护，防止移动逻辑立即覆盖击退速度
        _knockbackProtectionTimer = KNOCKBACK_PROTECTION_DURATION;

        // 进入受伤状态
        SetState(EnemyState.Hit);

        if (debugStateOverlay)
        {
            Debug.Log($"[{gameObject.name}] 受击 - 伤害={damage}, 击退={knockbackDirection}, 保护时间={KNOCKBACK_PROTECTION_DURATION}s");
        }
    }

    // Damageable.damageableHit 的默认回调入口（UnityEvent 需要 public）
    public virtual void OnHit(int damage, Vector2 knockback)
    {
        OnDamageTaken(damage, knockback);
    }

    public virtual bool IsInvulnerable()
    {
        // 检查无敌帧（由Damageable系统管理）
        if (damageable == null)
            return false;

        return damageable.IsInvulnerable;
    }
}

