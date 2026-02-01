using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 检测区域绑定结构
/// 用于在Inspector中配置多个DetectionZone及其角色
/// </summary>
[System.Serializable]
public struct DetectionZoneBinding
{
    /// <summary>
    /// 检测区域的角色/用途
    /// </summary>
    public enum Role
    {
        PrimaryAttack = 0,   // 主攻击检测（用于GetDetectedTargets）
        SecondaryAttack = 1, // 副攻击检测
        Cliff = 2,           // 崖边检测
        Alert = 3,           // 警戒范围
        Lookout = 4,         // 视野范围
        Custom = 5,          // 自定义用途
        Wall = 6,            // 墙壁检测
    }

    [Tooltip("该检测区的角色/用途")]
    public Role role;

    [Tooltip("拖拽子物体上的DetectionZone组件")]
    public DetectionZone zone;

    [TextArea(1, 3)]
    [Tooltip("对该检测区的说明，便于维护（如'攻击判定'、'崖边检测'等）")]
    public string note;
}

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
public abstract partial class EnemyAgentBase : MonoBehaviour, IAgentPerception, IDamageResponder
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
    [SerializeField] private List<DetectionZoneBinding> zoneBindings = new List<DetectionZoneBinding>();

    [Header("调试")]
    [SerializeField] protected bool debugStateOverlay = true;

    // ===== 组件缓存（在Awake中初始化，禁止直接修改） =====
    protected Rigidbody2D rb2d;
    protected Animator animator;
    protected Damageable damageable;
    protected StatModifierLayer statLayer;
    protected DetectionZone detectionZone;
    protected Transform cacheTransform;

    // ===== 状态机变量 =====
    protected EnemyState currentState = EnemyState.Idle;
    protected EnemyState previousState = EnemyState.Idle;

    // ===== 运行时缓存 =====
    protected Transform currentTarget;
    protected List<Collider2D> detectedTargets = new List<Collider2D>();

    // ===== 攻击系统（统一由基类管理）=====
    private float _attackCooldownTimer = 0f;
    private NpcAbilityController _npcAbilityController;
    private bool _hasTarget = false;  // 由 PrimaryAttack 检测区事件驱动更新（SecondaryAttack 仅在 Tick 中轮询）
    private DetectionZone _primaryAttackZone;  // 缓存 PrimaryAttack 检测区

    /// <summary>
    /// 是否有攻击目标（只读访问器，供子类查询）
    /// </summary>
    protected bool HasTarget => _hasTarget;

    // ===== 2A 数值缓存（由 ApplyTuningProfile 填充）=====
    // 以下字段在运行时从 EnemyTuningProfile 下发，供子类使用
    protected float _moveSpeed;
    protected int _attackDamage;
    protected float _attackRange;
    protected float _attackCooldown;
    protected int _attackZonePriority;
    protected int _abilityZonePriority;
    protected float _perceptionRadius;
    protected float _knockbackMultiplier;
    protected float _knockbackToPlayer;
    protected bool _enableDeathAnimation;
    protected string _attackTriggerName;

    // ===== Attack 组件基础击退缓存（用于避免重复缩放）=====
    // 存储每个 Attack 组件的原始 knockback 值（Prefab 上配置的基础值）
    private Dictionary<Attack, Vector2> _attackBaseKnockbacks = new Dictionary<Attack, Vector2>();

    // ===== 击退保护机制 =====
    // 在受击后的一小段时间内，防止移动逻辑覆盖击退速度
    private float _knockbackProtectionTimer = 0f;
    private const float KNOCKBACK_PROTECTION_DURATION = 0.15f; // 击退保护时长（秒）

    /// <summary>
    /// 是否处于击退保护期间
    /// 子类的 TickPhysics 应在此期间跳过移动逻辑，避免覆盖击退速度
    /// </summary>
    protected bool IsKnockbackProtected => _knockbackProtectionTimer > 0f;

    // 提供只读访问器供子类使用（推荐方式）
    protected float MoveSpeed => _moveSpeed * (statLayer != null ? statLayer.MoveSpeedMultiplier : 1f);
    protected int AttackDamage => _attackDamage;
    protected float AttackRange => _attackRange;
    protected float AttackCooldown => _attackCooldown;
    protected float PerceptionRadius => _perceptionRadius;
    protected float KnockbackMultiplier => _knockbackMultiplier;
    protected float KnockbackToPlayer => _knockbackToPlayer;
    protected bool EnableDeathAnimation => _enableDeathAnimation;
    protected string AttackTriggerName => _attackTriggerName;

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
