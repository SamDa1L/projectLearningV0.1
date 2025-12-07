using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 敌人代理基类
/// 为所有敌人提供统一的架构框架
///
/// 核心特性：
/// 1. 组件缓存 - 在Awake中统一缓存所有必需的组件
/// 2. 生命周期钩子 - 提供Enter/Exit/Tick状态转换机制
/// 3. 物理分离 - 所有物理操作集中在FixedUpdate中处理
/// 4. 调试可视化 - Gizmos参数显示 + StateDebugOverlay状态面板
/// 5. 接口实现 - 支持IAgentPerception和IDamageResponder
///
/// 迁移指南：
/// - 子类应覆盖 Initialize()、EnterState()、ExitState()、TickState()、TickPhysics()
/// - 所有状态逻辑在 TickState() 中实现
/// - 所有物理操作在 TickPhysics() 中实现
/// </summary>
public abstract class EnemyAgentBase : MonoBehaviour, IAgentPerception, IDamageResponder
{
    // ===== 状态定义 =====
    public enum EnemyState
    {
        Idle,      // 待机/巡逻
        Chase,     // 追踪目标
        Attack,    // 攻击
        Hit,       // 受伤
        Dead       // 死亡
    }

    // ===== 序列化字段（参数） =====
    [Header("配置")]
    [SerializeField] private EnemyTuningProfile tuningProfile;

    [Header("调试")]
    [SerializeField] protected bool debugStateOverlay = true;

    // ===== 组件缓存（在Awake中初始化，禁止直接修改） =====
    protected Rigidbody2D rb2d;
    protected Animator animator;
    protected Damageable damageable;
    protected DetectionZone detectionZone;
    protected Transform cacheTransform;

    // ===== 状态机变量 =====
    protected EnemyState currentState = EnemyState.Idle;
    protected EnemyState previousState = EnemyState.Idle;

    // ===== 运行时缓存 =====
    protected Transform currentTarget;
    protected List<Collider2D> detectedTargets = new List<Collider2D>();

    // ===== Unity生命周期 =====

    protected virtual void Awake()
    {
        // ===== 组件缓存 =====
        CacheComponents();

        // ===== 初始化钩子 =====
        Initialize();
    }

    protected virtual void Update()
    {
        // ===== 状态机更新 =====
        TickState(Time.deltaTime);

        // ===== 调试显示 =====
        #if UNITY_EDITOR
        UpdateDebugOverlay();
        #endif
    }

    protected virtual void FixedUpdate()
    {
        // ===== 所有物理操作集中在这里 =====
        TickPhysics(Time.fixedDeltaTime);
    }

    // ===== 组件缓存 =====

    /// <summary>
    /// 在Awake中缓存所有必需的组件
    /// 子类可以覆盖此方法来扩展组件缓存
    /// </summary>
    protected virtual void CacheComponents()
    {
        // 获取本体的组件
        rb2d = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        damageable = GetComponent<Damageable>();
        detectionZone = GetComponent<DetectionZone>();
        cacheTransform = transform;

        // 验证关键组件
        if (rb2d == null)
            Debug.LogError($"[{gameObject.name}] Missing Rigidbody2D component", gameObject);

        if (animator == null)
            Debug.LogError($"[{gameObject.name}] Missing Animator component", gameObject);

        if (damageable == null)
            Debug.LogError($"[{gameObject.name}] Missing Damageable component", gameObject);

        if (detectionZone == null)
            Debug.LogWarning($"[{gameObject.name}] Missing DetectionZone component (optional)", gameObject);
    }

    // ===== 初始化钩子 =====

    /// <summary>
    /// 初始化钩子，在Awake之后、Start之前
    /// 子类可覆盖此方法来进行自定义初始化
    /// </summary>
    protected virtual void Initialize()
    {
        // 验证调参配置
        if (tuningProfile == null)
            Debug.LogWarning($"[{gameObject.name}] TuningProfile未分配，敌人参数将无法正确加载", gameObject);

        // 子类实现
    }

    // ===== 状态生命周期 =====

    /// <summary>
    /// 进入新状态时调用
    /// 用于状态初始化（设置动画、播放音效等）
    /// </summary>
    /// <param name="newState">新状态</param>
    protected virtual void EnterState(EnemyState newState)
    {
        // Debug.Log($"[{gameObject.name}] Enter State: {newState}");

        // 子类可覆盖此方法
    }

    /// <summary>
    /// 离开状态时调用
    /// 用于状态清理（停止音效、重置参数等）
    /// </summary>
    /// <param name="oldState">旧状态</param>
    protected virtual void ExitState(EnemyState oldState)
    {
        // Debug.Log($"[{gameObject.name}] Exit State: {oldState}");

        // 子类可覆盖此方法
    }

    /// <summary>
    /// 当前状态更新逻辑，在Update中调用
    /// 所有状态判断和状态转换的逻辑都在这里实现
    /// </summary>
    /// <param name="deltaTime">帧间隔时间</param>
    protected virtual void TickState(float deltaTime)
    {
        // 子类必须实现此方法来定义状态逻辑
    }

    /// <summary>
    /// 物理更新逻辑，在FixedUpdate中调用
    /// 所有物理相关的操作（速度、力等）都在这里处理
    /// </summary>
    /// <param name="fixedDeltaTime">固定时间步长</param>
    protected virtual void TickPhysics(float fixedDeltaTime)
    {
        // 子类可覆盖此方法来定义自定义物理行为
    }

    /// <summary>
    /// 设置敌人状态，自动调用Enter/Exit钩子
    /// </summary>
    /// <param name="newState">新状态</param>
    protected void SetState(EnemyState newState)
    {
        if (newState == currentState)
            return;

        previousState = currentState;
        currentState = newState;

        // 调用状态生命周期钩子
        ExitState(previousState);
        EnterState(newState);
    }

    // ===== IAgentPerception 接口实现 =====

    public virtual List<Collider2D> GetDetectedTargets()
    {
        if (detectionZone != null)
            return detectionZone.detectedColliders;

        return detectedTargets;
    }

    public virtual bool IsTargetInRange(Transform target, float range)
    {
        if (target == null)
            return false;

        float distance = Vector2.Distance(cacheTransform.position, target.position);
        return distance <= range;
    }

    // ===== IDamageResponder 接口实现 =====

    public virtual void OnDamageTaken(int damage, Vector2 knockbackDirection)
    {
        // 应用击退（在FixedUpdate的TickPhysics中应用速度）
        if (rb2d != null && !damageable.LockVelocity)
        {
            rb2d.velocity = new Vector2(knockbackDirection.x, rb2d.velocity.y + knockbackDirection.y);
        }

        // 进入受伤状态
        SetState(EnemyState.Hit);
    }

    public virtual bool IsInvulnerable()
    {
        // 检查无敌帧（由Damageable系统管理）
        if (damageable == null)
            return false;

        return damageable.IsInvulnerable;
    }

    // ===== 调试可视化 =====

    #if UNITY_EDITOR

    protected virtual void OnDrawGizmosSelected()
    {
        if (cacheTransform == null)
            cacheTransform = transform;

        // 绘制基础位置
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(cacheTransform.position, 0.1f);

        // 子类可以在这里扩展更多的Gizmos绘制
    }

    /// <summary>
    /// 在Scene视图显示调试信息面板
    /// </summary>
    private void UpdateDebugOverlay()
    {
        if (!debugStateOverlay)
            return;

        // 在Game视图左上角显示调试信息
        // 注：在Scene视图中显示需要使用Handles或GUILayout
    }

    #endif

    // ===== 工具方法 =====

    /// <summary>
    /// 获取当前状态
    /// </summary>
    public EnemyState GetCurrentState() => currentState;

    /// <summary>
    /// 获取前一个状态
    /// </summary>
    public EnemyState GetPreviousState() => previousState;

    /// <summary>
    /// 检查当前是否为指定状态
    /// </summary>
    public bool IsInState(EnemyState state) => currentState == state;

    /// <summary>
    /// 获取当前目标
    /// </summary>
    public Transform GetCurrentTarget() => currentTarget;

    /// <summary>
    /// 获取调参配置资源
    /// </summary>
    public EnemyTuningProfile TuningProfile => tuningProfile;

    /// <summary>
    /// 检查敌人是否还活着
    /// </summary>
    public bool IsAlive() => damageable != null && damageable.IsAlive;
}
