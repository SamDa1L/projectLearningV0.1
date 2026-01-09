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
        PrimaryAttack,  // 主攻击检测（用于GetDetectedTargets）
        SecondaryAttack,// 副攻击检测
        Cliff,          // 崖边检测
        Alert,          // 警戒范围
        Lookout,        // 视野范围
        Custom          // 自定义用途
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
    private bool _hasTarget = false;  // 由 PrimaryAttack 检测区事件驱动更新
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
    protected float _perceptionRadius;
    protected float _knockbackMultiplier;
    protected float _knockbackToPlayer;
    protected bool _enableDeathAnimation;
    protected string _attackTriggerName;
    protected bool _useLegacyLogicFallback;

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
    protected bool UseLegacyLogicFallback => _useLegacyLogicFallback;

    // ===== Unity生命周期 =====

    protected virtual void Awake()
    {
        // ===== 组件缓存 =====
        CacheComponents();

        // ===== 解决检测区依赖 =====
        ResolveDetectionZone();

        // ===== 初始化钩子 =====
        Initialize();
    }

    protected virtual void OnEnable()
    {
        // ===== 绑定 PrimaryAttack 检测区事件 =====
        if (_primaryAttackZone != null)
        {
            _primaryAttackZone.OnDetectedTargetsChanged.AddListener(OnPrimaryAttackTargetsChanged);

            if (debugStateOverlay)
            {
                Debug.Log($"[{gameObject.name}] 已绑定 PrimaryAttack 检测区事件", gameObject);
            }
        }
    }

    protected virtual void OnDisable()
    {
        // ===== 解绑 PrimaryAttack 检测区事件 =====
        if (_primaryAttackZone != null)
        {
            _primaryAttackZone.OnDetectedTargetsChanged.RemoveListener(OnPrimaryAttackTargetsChanged);

            if (debugStateOverlay)
            {
                Debug.Log($"[{gameObject.name}] 已解绑 PrimaryAttack 检测区事件", gameObject);
            }
        }
    }

    /// <summary>
    /// PrimaryAttack 检测区目标变化事件回调
    /// 由基类统一处理，更新 _hasTarget 标记
    /// </summary>
    private void OnPrimaryAttackTargetsChanged()
    {
        if (_primaryAttackZone != null)
        {
            _hasTarget = _primaryAttackZone.detectedColliders.Count > 0;

            if (debugStateOverlay)
            {
                Debug.Log($"[{gameObject.name}] PrimaryAttack 目标变化：hasTarget={_hasTarget}, count={_primaryAttackZone.detectedColliders.Count}");
            }
        }
    }

    protected virtual void Update()
    {
        // ===== 击退保护计时器递减 =====
        if (_knockbackProtectionTimer > 0f)
        {
            _knockbackProtectionTimer -= Time.deltaTime;
        }

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
        statLayer = GetComponent<StatModifierLayer>();
        detectionZone = GetComponent<DetectionZone>();
        cacheTransform = transform;

        // 验证关键组件
        if (rb2d == null)
            Debug.LogError($"[{gameObject.name}] 缺少Rigidbody2D组件", gameObject);

        if (animator == null)
            Debug.LogError($"[{gameObject.name}] 缺少Animator组件", gameObject);

        if (damageable == null)
            Debug.LogError($"[{gameObject.name}] 缺少Damageable组件", gameObject);

        // 注意：DetectionZone的验证在ResolveDetectionZone()中执行
    }

    /// <summary>
    /// 解决检测区依赖（v0.2重构版 - Plan A）
    /// 在Awake中CacheComponents()之后调用
    ///
    /// 设计说明（方案A：基于zoneBindings）：
    /// - 放弃复杂的自动推断，改为强制要求显式配置
    /// - zoneBindings是唯一的数据源（必须在Inspector中配置）
    /// - 每个binding必须有有效的zone和role
    /// - 直接从zoneBindings查询PrimaryAttack，无需缓存专用字段
    /// - GetDetectedTargets()和GetDetectedTargetsForRole()都从zoneBindings读取
    ///
    /// 配置要求：
    /// 1. 在Inspector中为敌人配置zoneBindings列表
    /// 2. 至少要有一个binding，role=PrimaryAttack
    /// 3. 可选地添加其他role的binding（Cliff、Alert等）
    ///
    /// 优势：
    /// - 数据源单一，无冲突
    /// - 逻辑清晰简洁
    /// - 无歧义的推断问题
    /// - GetDetectedTargets()和GetDetectedTargetsForRole()一致
    /// - 不需要冗余的缓存字段
    ///
    /// v0.3 更新（攻击系统重构）：
    /// - 缓存 PrimaryAttack 检测区到 _primaryAttackZone
    /// - 由基类在 OnEnable/OnDisable 中统一绑定/解绑事件
    /// </summary>
    private void ResolveDetectionZone()
    {
        // 检查zoneBindings是否已配置
        if (zoneBindings == null || zoneBindings.Count == 0)
        {
            Debug.LogError(
                $"[{gameObject.name}] ✗ 检测区配置失败：zoneBindings列表为空！\n" +
                $"请在Inspector中为敌人脚本配置zoneBindings，至少需要一个role=PrimaryAttack的binding，" +
                $"拖拽DZ_Attack子物体的DetectionZone到zone字段。",
                gameObject
            );
            return;
        }

        // 查找PrimaryAttack角色的binding
        DetectionZoneBinding primaryBinding = default;
        int primaryAttackIndex = -1;

        for (int i = 0; i < zoneBindings.Count; i++)
        {
            if (zoneBindings[i].role == DetectionZoneBinding.Role.PrimaryAttack)
            {
                primaryBinding = zoneBindings[i];
                primaryAttackIndex = i;
                break;
            }
        }

        // 检查是否找到PrimaryAttack
        if (primaryAttackIndex == -1)
        {
            Debug.LogError(
                $"[{gameObject.name}] ✗ 检测区配置失败：zoneBindings中未找到PrimaryAttack！\n" +
                $"检测到的binding数量：{zoneBindings.Count}\n" +
                $"请检查zoneBindings中是否有role=PrimaryAttack的项，" +
                $"并将对应的DetectionZone赋值到zone字段。",
                gameObject
            );
            return;
        }

        // 检查zone是否有效
        if (primaryBinding.zone == null)
        {
            Debug.LogError(
                $"[{gameObject.name}] ✗ 检测区配置失败：PrimaryAttack的zone为空！\n" +
                $"请在Inspector中拖拽DZ_Attack（或其他攻击检测区）的DetectionZone到该binding的zone字段。",
                gameObject
            );
            return;
        }

        // 配置成功，缓存主检测区到detectionZone字段
        detectionZone = primaryBinding.zone;

        // v0.3 新增：缓存到 _primaryAttackZone，供攻击系统使用
        _primaryAttackZone = primaryBinding.zone;

        if (debugStateOverlay)
        {
            Debug.Log(
                $"[{gameObject.name}] ✓ 检测区初始化成功\n" +
                $"  配置来源：zoneBindings[{primaryAttackIndex}]\n" +
                $"  PrimaryAttack → {primaryBinding.zone.gameObject.name}\n" +
                $"  已配置的binding总数：{zoneBindings.Count}",
                gameObject
            );
        }
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

        // 应用调参配置到基础组件
        ApplyTuningProfile();

        // 确保受击事件链路可用（否则 knockbackMultiplier 虽然导入成功，但不会产生任何位移效果）
        EnsureDamageableHitListener();

        // 子类实现
    }

    private void EnsureDamageableHitListener()
    {
        if (damageable == null || damageable.damageableHit == null)
            return;

        // Prefab/Inspector 已经配置了持久化回调时，不在运行时重复绑定，避免重复击退
        if (damageable.damageableHit.GetPersistentEventCount() > 0)
            return;

        // 没有持久化回调时，默认绑定到本基类的 OnHit（用于敌人受击击退）
        damageable.damageableHit.RemoveListener(OnHit);
        damageable.damageableHit.AddListener(OnHit);
    }

    /// <summary>
    /// 应用调参配置到敌人
    /// 从EnemyTuningProfile读取所有参数并应用到敌人组件
    /// 在Initialize()中自动调用
    ///
    /// 2A 要求：填充运行时缓存，让所有数值从 Profile 下发
    /// 子类应覆盖此方法来应用自己特有的参数
    /// </summary>
    protected virtual void ApplyTuningProfile()
    {
        if (tuningProfile == null)
            return;

        // ===== 填充运行时数值缓存 =====
        _moveSpeed = tuningProfile.moveSpeed;
        _attackDamage = tuningProfile.attackDamage;
        _attackRange = tuningProfile.attackRange;
        _attackCooldown = tuningProfile.attackCooldown;
        _perceptionRadius = tuningProfile.perceptionRadius;
        _knockbackMultiplier = tuningProfile.knockbackMultiplier;
        _knockbackToPlayer = tuningProfile.knockbackToPlayer;
        _enableDeathAnimation = tuningProfile.enableDeathAnimation;
        _attackTriggerName = tuningProfile.animationTrigger;
        _useLegacyLogicFallback = tuningProfile.useLegacyLogicFallback;

        // ===== 应用 Damageable 配置 =====
        if (damageable != null)
        {
            var stats = tuningProfile.GetDamageableStats();
            damageable.Configure(stats);
        }

        // ===== 应用 knockbackToPlayer 到所有 Attack 组件（Monster → Player 击退缩放）=====
        ApplyKnockbackToPlayerScale();

        // TODO: 同步感知半径到 DetectionZone（如果支持动态半径）
        // 例如：if (detectionZone != null) detectionZone.SetRadius(_perceptionRadius);

        if (debugStateOverlay)
        {
            Debug.Log(
                $"[{gameObject.name}] 调参配置已应用\n" +
                $"  Profile: {tuningProfile.profileName}\n" +
                $"  数值缓存：\n" +
                $"    MoveSpeed={_moveSpeed}, AttackDamage={_attackDamage}\n" +
                $"    AttackRange={_attackRange}, AttackCooldown={_attackCooldown}\n" +
                $"    PerceptionRadius={_perceptionRadius}, KnockbackMult={_knockbackMultiplier}\n" +
                $"    KnockbackToPlayer={_knockbackToPlayer}\n" +
                $"    AnimTrigger='{_attackTriggerName}', LegacyFallback={_useLegacyLogicFallback}",
                gameObject
            );
        }
    }

    /// <summary>
    /// 将 Profile 下发到本敌人层级下所有 Attack 组件
    ///
    /// 实现说明：
    /// - 遍历敌人及其所有子物体上的 Attack 组件
    /// - 缓存每个 Attack 的原始 knockback（Prefab 基础值），避免重复缩放
    /// - 应用缩放：attack.knockback = baseKnockback * knockbackToPlayer
    /// - 下发伤害：attack.attackDamage = Profile.attackDamage
    ///
    /// 关键点：
    /// - Attack 脚本同时被玩家与敌人复用，因此不在 Attack 里做"向上找 Profile"
    /// - 由敌人侧（EnemyAgentBase）统一下发，避免误伤玩家攻击
    /// - 使用 _attackBaseKnockbacks 字典缓存基础值，防止重复初始化时数值被乘多次
    /// </summary>
    private void ApplyKnockbackToPlayerScale()
    {
        // 获取本敌人层级下所有 Attack 组件（包括子物体）
        var attacks = GetComponentsInChildren<Attack>(true);

        if (attacks.Length == 0)
        {
            if (debugStateOverlay)
            {
                Debug.Log($"[{gameObject.name}] 未找到任何 Attack 组件，跳过 knockbackToPlayer 应用", gameObject);
            }
            return;
        }

        int appliedCount = 0;

        foreach (var attack in attacks)
        {
            // 如果是首次处理该 Attack，缓存其原始 knockback
            if (!_attackBaseKnockbacks.ContainsKey(attack))
            {
                _attackBaseKnockbacks[attack] = attack.knockback;
            }

            // 获取基础击退值
            Vector2 baseKnockback = _attackBaseKnockbacks[attack];

            // 应用缩放
            attack.knockback = baseKnockback * _knockbackToPlayer;

            // 下发伤害（命中结算以 Attack.attackDamage 为准）
            attack.attackDamage = _attackDamage;
            appliedCount++;

            if (debugStateOverlay)
            {
                Debug.Log(
                    $"[{gameObject.name}] Attack '{attack.gameObject.name}' knockback 已缩放\n" +
                    $"  基础值: {baseKnockback} → 缩放后: {attack.knockback} (scale={_knockbackToPlayer})",
                    attack.gameObject
                );
            }
        }

        if (debugStateOverlay)
        {
            Debug.Log($"[{gameObject.name}] knockbackToPlayer 已应用到 {appliedCount} 个 Attack 组件", gameObject);
        }
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

    /// <summary>
    /// 获取主检测区（PrimaryAttack角色）的检测目标
    ///
    /// v0.2设计（方案A）：
    /// - 直接查询zoneBindings中role=PrimaryAttack的检测区
    /// - zoneBindings是唯一的数据源，通过GetDetectedTargetsForRole()访问
    /// - GetDetectedTargets()和GetDetectedTargetsForRole(PrimaryAttack)等价
    ///
    /// 返回值：
    /// - 如果配置正确，返回DZ_Attack（或其他PrimaryAttack角色）的目标列表
    /// - 如果配置不正确，返回空列表
    /// </summary>
    public virtual List<Collider2D> GetDetectedTargets()
    {
        return GetDetectedTargetsForRole(DetectionZoneBinding.Role.PrimaryAttack);
    }

    public virtual bool IsTargetInRange(Transform target, float range)
    {
        if (target == null)
            return false;

        float distance = Vector2.Distance(cacheTransform.position, target.position);
        return distance <= range;
    }

    /// <summary>
    /// 根据角色获取特定的检测目标（v0.2核心API）
    ///
    /// 这是v0.2多检测区支持的主要方法，用于子类访问各类检测区的目标
    ///
    /// 使用示例：
    /// - 攻击目标：GetDetectedTargetsForRole(DetectionZoneBinding.Role.PrimaryAttack)
    /// - 崖边检测：GetDetectedTargetsForRole(DetectionZoneBinding.Role.Cliff)
    /// - 警戒范围：GetDetectedTargetsForRole(DetectionZoneBinding.Role.Alert)
    ///
    /// 实现逻辑：
    /// - 遍历zoneBindings查找对应role的binding
    /// - 如果找到且zone有效，返回该zone的目标列表
    /// - 如果未找到或无效，返回空列表（不会报错，子类需自行处理）
    ///
    /// 性能说明：
    /// - 遍历zoneBindings（通常3-5个元素），O(n)时间复杂度
    /// - 如果频繁调用同一role，建议在Initialize()中缓存结果
    /// </summary>
    /// <param name="role">检测区的角色/用途</param>
    /// <returns>该角色对应的检测区中的目标列表，未找到则返回空列表</returns>
    public virtual List<Collider2D> GetDetectedTargetsForRole(DetectionZoneBinding.Role role)
    {
        // 遍历zoneBindings查找对应的binding
        foreach (var binding in zoneBindings)
        {
            if (binding.role == role && binding.zone != null)
                return binding.zone.detectedColliders;
        }

        // 未找到该角色的检测区，返回空列表
        return new List<Collider2D>();
    }

    /// <summary>
    /// 根据角色获取对应的DetectionZone组件
    /// </summary>
    public virtual DetectionZone GetZone(DetectionZoneBinding.Role role)
    {
        foreach (var binding in zoneBindings)
        {
            if (binding.role == role && binding.zone != null)
                return binding.zone;
        }
        return null;
    }

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

    // ===== 调试可视化 =====

    /// <summary>
    /// 在编辑器中显示Gizmos的选项开关
    /// </summary>
    [Header("Gizmos可视化")]
    [SerializeField] private bool showDetectionZoneGizmos = true;
    [SerializeField] private bool showGizmosInPlayMode = false;

    #if UNITY_EDITOR

    protected virtual void OnDrawGizmosSelected()
    {
        if (cacheTransform == null)
            cacheTransform = transform;

        // 绘制基础位置
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(cacheTransform.position, 0.1f);

        // 绘制检测区Gizmos
        DrawDetectionZoneGizmos();
    }

    protected virtual void OnDrawGizmos()
    {
        // 在Play模式下，仅当启用选项时才绘制
        if (Application.isPlaying && !showGizmosInPlayMode)
            return;

        // 非Play模式或已启用Play模式显示时，绘制检测区
        if (!Application.isPlaying || showGizmosInPlayMode)
        {
            DrawDetectionZoneGizmos();
        }
    }

    /// <summary>
    /// 绘制所有检测区的Gizmos
    ///
    /// 颜色方案：
    /// - PrimaryAttack: 红色
    /// - SecondaryAttack: 橙色
    /// - Cliff: 蓝色
    /// - Alert: 绿色
    /// - Lookout: 黄色
    /// - Custom: 灰色
    /// </summary>
    private void DrawDetectionZoneGizmos()
    {
        if (!showDetectionZoneGizmos || zoneBindings == null || zoneBindings.Count == 0)
            return;

        foreach (var binding in zoneBindings)
        {
            if (binding.zone == null)
                continue;

            // 根据Role选择颜色
            Color zoneColor = GetColorForRole(binding.role);
            Gizmos.color = zoneColor;

            // 获取检测区的Collider2D
            var collider = binding.zone.GetComponent<Collider2D>();
            if (collider != null)
            {
                DrawCollider2DGizmo(collider, zoneColor);
            }

            // 绘制标签和检测目标数量
            DrawDetectionZoneLabel(binding);
        }
    }

    /// <summary>
    /// 根据Role获取对应的颜色
    /// </summary>
    private Color GetColorForRole(DetectionZoneBinding.Role role)
    {
        return role switch
        {
            DetectionZoneBinding.Role.PrimaryAttack => new Color(1f, 0f, 0f, 0.3f),      // 红色
            DetectionZoneBinding.Role.SecondaryAttack => new Color(1f, 0.5f, 0f, 0.3f),  // 橙色
            DetectionZoneBinding.Role.Cliff => new Color(0f, 0f, 1f, 0.3f),             // 蓝色
            DetectionZoneBinding.Role.Alert => new Color(0f, 1f, 0f, 0.3f),             // 绿色
            DetectionZoneBinding.Role.Lookout => new Color(1f, 1f, 0f, 0.3f),           // 黄色
            _ => new Color(0.5f, 0.5f, 0.5f, 0.3f)                                       // 灰色（Custom）
        };
    }

    /// <summary>
    /// 绘制Collider2D的Gizmo
    /// 支持BoxCollider2D和CircleCollider2D
    /// </summary>
    private void DrawCollider2DGizmo(Collider2D collider, Color color)
    {
        Gizmos.color = color;
        var transform = collider.transform;

        if (collider is BoxCollider2D boxCollider)
        {
            // 绘制Box
            Vector2 offset = boxCollider.offset;
            Vector2 size = boxCollider.size;
            Vector3 center = transform.position + (Vector3)offset;

            Vector3[] corners = new Vector3[4]
            {
                center + new Vector3(-size.x / 2, -size.y / 2, 0),
                center + new Vector3(size.x / 2, -size.y / 2, 0),
                center + new Vector3(size.x / 2, size.y / 2, 0),
                center + new Vector3(-size.x / 2, size.y / 2, 0)
            };

            // 绘制矩形边界
            Gizmos.DrawLine(corners[0], corners[1]);
            Gizmos.DrawLine(corners[1], corners[2]);
            Gizmos.DrawLine(corners[2], corners[3]);
            Gizmos.DrawLine(corners[3], corners[0]);

            // 绘制填充
            DrawFilledBox(corners, color);
        }
        else if (collider is CircleCollider2D circleCollider)
        {
            // 绘制Circle
            Vector2 offset = circleCollider.offset;
            float radius = circleCollider.radius;
            Vector3 center = transform.position + (Vector3)offset;

            Gizmos.DrawWireSphere(center, radius);

            // 绘制填充圆形
            DrawFilledCircle(center, radius, color, 20);
        }
    }

    /// <summary>
    /// 绘制检测区的标签和信息
    /// </summary>
    private void DrawDetectionZoneLabel(DetectionZoneBinding binding)
    {
        if (binding.zone == null)
            return;

        var transform = binding.zone.transform;
        Vector3 labelPos = transform.position + Vector3.up * 0.5f;

        // 获取检测到的目标数量
        int targetCount = binding.zone.detectedColliders.Count;
        string label = $"{binding.role}\n({targetCount})";

        TryDrawEditorLabel(labelPos, label);
    }

    /// <summary>
    /// 辅助方法：绘制填充的Box
    /// </summary>
    private void DrawFilledBox(Vector3[] corners, Color color)
    {
        // 这里简化处理，只绘制边框
        // 如需填充，可使用Mesh Gizmos (需要更复杂的实现)
    }

    /// <summary>
    /// 辅助方法：绘制填充的圆形
    /// </summary>
    private void DrawFilledCircle(Vector3 center, float radius, Color color, int segments)
    {
        Vector3[] points = new Vector3[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            float angle = (i / (float)segments) * 360f * Mathf.Deg2Rad;
            points[i] = center + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * radius;
        }

        // 绘制圆形线条
        for (int i = 0; i < segments; i++)
        {
            Gizmos.DrawLine(points[i], points[i + 1]);
        }
    }

    private static void TryDrawEditorLabel(Vector3 labelPos, string label)
    {
#if UNITY_EDITOR
        var handlesType = System.Type.GetType("UnityEditor.Handles, UnityEditor");
        if (handlesType == null)
            return;

        var method = handlesType.GetMethod("Label", new[] { typeof(Vector3), typeof(string) });
        if (method == null)
            return;

        method.Invoke(null, new object[] { labelPos, label });
#endif
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

    // ===== 攻击系统 =====

    /// <summary>
    /// 统一的攻击系统更新方法（供子类在 TickState 中调用）
    ///
    /// 功能：
    /// - 递减攻击冷却计时器
    /// - 同步 hasTarget 布尔参数到 Animator（可选）
    /// - 当满足攻击条件时（HasTarget && 冷却归零）触发攻击动画并执行回调
    ///
    /// 参数：
    /// - deltaTime: 帧间隔时间
    /// - onAttackTriggered: 攻击触发时的回调（可选），供子类做额外处理（如播放特效、状态切换）
    ///
    /// 文档来源：Docs代码冗余优化方案.md - 步骤2
    /// </summary>
    protected void TickAttackSystem(float deltaTime, System.Action onAttackTriggered = null)
    {
        // 1. 递减冷却计时器
        if (_attackCooldownTimer > 0f)
        {
            _attackCooldownTimer -= deltaTime;
        }

        // 2. 同步 hasTarget 到 Animator（可选，如果 Animator 使用该参数）
        if (animator != null)
        {
            animator.SetBool(AnimationStrings.hasTarget, _hasTarget);
        }

        // 3. 检查是否满足攻击条件
        if (_hasTarget && _attackCooldownTimer <= 0f)
        {
            // 触发攻击动画
            TriggerAttackAnimation();

            // 执行回调（供子类做额外处理）
            onAttackTriggered?.Invoke();

            // 重置冷却计时器
            _attackCooldownTimer = _attackCooldown;

            if (debugStateOverlay)
            {
                Debug.Log($"[{gameObject.name}] TickAttackSystem 触发攻击 - Cooldown={_attackCooldown}s");
            }
        }
    }

    /// <summary>
    /// 触发攻击动画（统一入口）
    /// 使用 Profile 中配置的 animationTrigger 而不是硬编码
    ///
    /// 2A 要求：所有攻击动画触发必须通过此方法，禁止硬编码 Trigger 名称
    /// </summary>
    protected void TriggerAttackAnimation()
    {
        if (animator == null)
        {
            Debug.LogWarning($"[{gameObject.name}] Animator 为空，无法触发攻击动画", gameObject);
            return;
        }

        if (string.IsNullOrEmpty(_attackTriggerName))
        {
            Debug.LogWarning($"[{gameObject.name}] attackTriggerName 为空，无法触发攻击动画。请检查 TuningProfile 配置。", gameObject);
            return;
        }

        animator.SetTrigger(_attackTriggerName);

        if (debugStateOverlay)
        {
            Debug.Log($"[{gameObject.name}] 触发攻击动画: {_attackTriggerName}", gameObject);
        }
    }

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
