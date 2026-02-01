using UnityEngine;

/// <summary>
/// Summon 生命周期控制器（0.5 扩展）。
/// 规则：
/// - lifetimeSeconds > 0：时间到销毁
/// - isDead=true：血量归零（Damageable 死亡事件）销毁
/// - lifetimeSeconds == -1：无时间限制（仅当 isDead=true 时有效；否则给出警告但不阻塞）
/// </summary>
public class SummonLifetimeController : MonoBehaviour
{
    [Tooltip("持续时间（秒）。-1 表示无时间限制（仅当 isDead=true 时有效）。")]
    public float lifetimeSeconds = 0f;

    [Tooltip("是否启用“死亡销毁”（Damageable.Health <= 0）。")]
    public bool isDead = false;

    private Damageable _damageable;
    private bool _subscribed;
    private bool _configured;
    private bool _destroyScheduled;

    private void OnEnable()
    {
        // 该组件通常由 SummonAbility 在运行时 AddComponent + Configure。
        // Unity 会先调用 OnEnable，再返回给调用方；因此这里必须支持“未配置时不执行”。
        if (!_configured)
        {
            return;
        }

        ApplyInternal();
    }

    /// <summary>
    /// 由召唤逻辑配置本组件（推荐：AddComponent 后立刻调用）。
    /// </summary>
    public void Configure(float lifetime, bool dead)
    {
        lifetimeSeconds = lifetime;
        isDead = dead;
        _configured = true;

        ApplyInternal();
    }

    private void ApplyInternal()
    {
        // 若之前已订阅过，先解除，避免重复监听
        if (_subscribed && _damageable != null && _damageable.damageableDeath != null)
        {
            _damageable.damageableDeath.RemoveListener(OnDeath);
            _subscribed = false;
        }

        if (!isDead && lifetimeSeconds < 0f)
        {
            Debug.LogWarning(
                $"[SummonLifetimeController] 配置错误：isDead=false 但 lifetimeSeconds={lifetimeSeconds}（此组合不合法，期望 >= 0）。",
                this);
        }

        if (!_destroyScheduled && lifetimeSeconds > 0f)
        {
            Destroy(gameObject, lifetimeSeconds);
            _destroyScheduled = true;
        }

        if (isDead)
        {
            _damageable = GetComponent<Damageable>();
            if (_damageable == null)
            {
                Debug.LogWarning("[SummonLifetimeController] isDead=true 但未找到 Damageable 组件，无法按死亡销毁。", this);
                return;
            }

            if (_damageable.damageableDeath != null)
            {
                _damageable.damageableDeath.AddListener(OnDeath);
                _subscribed = true;
            }
        }
    }

    private void OnDisable()
    {
        if (_subscribed && _damageable != null && _damageable.damageableDeath != null)
        {
            _damageable.damageableDeath.RemoveListener(OnDeath);
        }

        _subscribed = false;
        _damageable = null;
    }

    private void OnDeath()
    {
        // 若同时存在时间销毁，提前 Destroy 只会更早生效，不会冲突。
        Destroy(gameObject);
    }
}
