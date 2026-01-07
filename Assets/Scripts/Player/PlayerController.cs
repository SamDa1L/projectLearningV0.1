using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 玩家控制器脚本
///
/// 功能说明:
/// - 管理玩家的移动、奔跑、跳跃、攻击等核心玩法
/// - 处理输入系统(Input System)的输入回调
/// - 控制角色朝向(左/右)和翻转
/// - 计算当前移动速度，区分行走/奔跑/空中状态
/// - 驱动动画系统，更新Animator参数
/// - 与物理系统(Rigidbody2D)和碰撞检测(TouchingDirections)交互
///
/// 依赖组件:
/// - Rigidbody2D: 角色刚体，处理速度和物理
/// - TouchingDirections: 碰撞检测，检测地面/墙壁/天花板状态
/// - Animator: 动画系统，处理动画状态切换
///
/// 关键属性:
/// - CurrentMoveSpeed: 根据当前状态计算的速度(读取属性)
/// - IsMoving: 是否在移动中
/// - IsRunning: 是否在奔跑
/// - IsFacingRight: 是否朝向右侧
/// - CanMove: 是否允许移动(由动画控制，防止攻击时移动)
/// </summary>
[RequireComponent(typeof(Rigidbody2D), typeof(TouchingDirections), typeof(Damageable))]

[DefaultExecutionOrder(-100)]
public class PlayerController : MonoBehaviour
{
    /// <summary>
    /// 玩家配置资源（阶段 3A）
    /// 从 CastleDB 导入的玩家配置，包含移动速度、生命值等参数
    /// 如果未设置，将使用硬编码的默认值
    /// </summary>
    [Header("配置")]
    [SerializeField]
    [Tooltip("玩家配置资源（从 CastleDB 导入），留空则使用默认值")]
    private PlayerConfig playerConfig;

    /// <summary>
    /// 是否使用 PlayerConfig（回退开关，阶段 3A）
    /// - true: 从 PlayerConfig 加载配置（推荐，数据驱动）
    /// - false: 使用硬编码的默认值（回退方案）
    /// </summary>
    [SerializeField]
    [Tooltip("是否使用 PlayerConfig 加载配置（false则使用硬编码默认值）")]
    private bool usePlayerConfigFromCastleDb = true;

    /// <summary>行走速度(m/s)</summary>
    [Header("移动参数（运行时从 PlayerConfig 覆盖）")]
    public float walkSpeed = 5f;

    /// <summary>奔跑速度(m/s)</summary>
    public float runSpeed = 8f;

    /// <summary>空中移动速度(m/s) - 跳跃时的移动速度</summary>
    public float airWalkSpeed = 3f;

    /// <summary>跳跃冲力 - 给予Y轴速度</summary>
    public float jumpImpules = 10f;

    /// <summary>
    /// 输入的移动方向向量
    ///
    /// 说明: 现在分为水平输入(X轴)和垂直输入(Y轴)
    /// - X轴(moveInput.x): 来自A/D键，用于水平移动
    /// - Y轴(moveInput.y): 来自W/S键，预留给爬墙系统使用
    /// </summary>
    [System.NonSerialized]
    public Vector2 moveInput;

    /// <summary>
    /// 水平输入(只包含X轴分量)
    ///
    /// 说明: 由A/D键控制，用于驱动行走/奔跑动画
    /// 值域: -1.0 ~ 1.0
    /// - 负数: 向左
    /// - 0: 无水平输入
    /// - 正数: 向右
    /// 用途: 判断IsMoving状态，控制角色朝向
    /// </summary>
    [System.NonSerialized]
    public float moveInputHorizontal = 0f;

    /// <summary>
    /// 垂直输入(只包含Y轴分量)
    ///
    /// 说明: 由W/S键控制，预留给爬墙系统使用
    /// 值域: -1.0 ~ 1.0
    /// - 负数: 向下
    /// - 0: 无垂直输入
    /// - 正数: 向上
    /// 用途: 后续爬墙系统中控制攀爬方向
    /// </summary>
    [System.NonSerialized]
    public float moveInputVertical = 0f;

    /// <summary>爬墙输入向量
    ///
    /// 说明: 用于爬墙时的上下输入，与moveInput相同但用于爬墙逻辑
    /// 用途: 在爬墙状态下控制垂直方向的速度
    /// </summary>
    [System.NonSerialized]
    public Vector2 climbInput;

    /// <summary>爬墙速度(m/s)
    ///
    /// 说明: 角色爬墙时的上下移动速度
    /// 配置: 建议设置为2-4f，比行走速度(5f)略慢，给予玩家充足的反应时间
    /// 用途: 在FixedUpdate中计算爬墙时的Y轴速度
    /// </summary>
    public float climbSpeed = 3f;

    /// <summary>碰撞检测组件的引用</summary>
    TouchingDirections touchingDirections;
    Damageable damageable;


    /// <summary>
    /// 计算当前水平移动速度属性
    ///
    /// 逻辑流程:
    /// 1. 检查CanMove - 是否允许移动(由动画系统控制)
    /// 2. 检查IsMoving && !IsOnWall - 是否有水平移动输入且不在墙壁
    /// 3. 判断IsGrounded - 在地面还是空中
    /// 4. 在地面上区分IsRunning - 返回奔跑速度或行走速度
    /// 5. 在空中 - 返回降低的空中移动速度
    ///
    /// 注意: 仅基于水平移动(moveInputHorizontal)，不受垂直输入(W/S)影响
    ///
    /// 返回值: float 当前应该使用的水平移动速度
    /// </summary>
    public float CurrentMoveSpeed
    {
        get
        {
            if (CanMove)
            {
                // 检查是否在移动且没有接触墙壁
                if (IsMoving && !touchingDirections.IsOnWall)
                {
                    // 区分地面和空中状态
                    if (touchingDirections.IsGrounded)
                    {
                        // 在地面上 - 区分奔跑和行走
                        if (IsRunning)
                        {
                            return runSpeed;
                        }
                        else
                        {
                            return walkSpeed;
                        }
                    }
                    else
                    {
                        // 在空中 - 返回降低的移动速度
                        return airWalkSpeed;
                    }
                }
                else
                {
                    // 没有移动输入或接触墙壁 - 待机速度为0
                    return 0;
                }
            }
            else
            {
                // 禁止移动(例如正在攻击) - 返回0
                return 0;
            }

        }
    }


    /// <summary>是否在移动的内部字段（阶段 3B：改为 public 以支持能力系统）</summary>
    [SerializeField]
    public bool _isMoving = false;

    /// <summary>
    /// 是否在进行水平移动的属性
    ///
    /// 定义: 当前是否有水平方向的移动输入(A/D键)
    /// 说明: 仅基于moveInputHorizontal判断，不包含垂直方向(W/S)
    ///
    /// getter: 返回当前水平移动状态
    /// setter: 设置水平移动状态并同步到Animator
    /// </summary>
    public bool IsMoving
    {
        get
        {
            return _isMoving;
        }
        private set
        {
            _isMoving = value;
            // 同步更新Animator的isMoving参数，驱动待机/移动动画切换
            animator.SetBool(AnimationStrings.isMoving, value);
        }
    }

    /// <summary>是否在奔跑的内部字段（阶段 3B：改为 public 以支持能力系统）</summary>
    [SerializeField]
    public bool _isRunning = false;

    /// <summary>
    /// 是否在奔跑的属性
    ///
    /// getter: 返回当前奔跑状态
    /// setter: 设置奔跑状态并同步到动画系统
    /// </summary>
    public bool IsRunning
    {
        get
        {
            return _isRunning;
        }
        set
        {
            _isRunning = value;
            // 同步更新Animator的isRunning参数，驱动行走/奔跑动画切换
            animator.SetBool(AnimationStrings.isRunning, value);
        }
    }

    /// <summary>角色是否朝向右侧的内部字段(true=右, false=左)</summary>
    public bool _isFacingRight = true;

    /// <summary>
    /// 角色朝向属性
    ///
    /// getter: 返回当前朝向(true=右, false=左)
    /// setter: 设置朝向，如果改变则翻转角色(缩放X=-1)
    ///
    /// 翻转原理: 改变transform.localScale的X分量来实现角色左右翻转
    /// </summary>
    public bool IsFacingRight
    {
        get
        {
            return _isFacingRight;
        }
        private set
        {
            if (_isFacingRight != value)
            {
                // 朝向改变时翻转角色 - 缩放X轴乘以-1
                transform.localScale *= new Vector2(-1, 1);
            }
            _isFacingRight = value;
        }
    }

    /// <summary>
    /// 是否允许移动属性(只读)
    ///
    /// 该属性从Animator中读取canMove参数
    /// 用于防止在攻击等特定动画播放时进行移动
    /// 返回值: true表示允许移动，false表示禁止移动
    /// </summary>
    public bool CanMove
    {
        get
        {
            return animator.GetBool(AnimationStrings.canMove);
        }
    }

    public bool IsAlive
    {
        get 
        { 
            return animator.GetBool(AnimationStrings.isAlive);
        }
    }


    /// <summary>是否正在爬墙的内部字段（阶段 3B：改为 public 以支持能力系统）</summary>
    [SerializeField]
    public bool _isClimbing = false;

    /// <summary>
    /// 是否正在爬墙的属性
    ///
    /// 定义: 当前角色是否接触墙壁并进行爬墙操作
    ///
    /// getter: 返回当前爬墙状态
    /// setter: 设置爬墙状态并同步到Animator的isClimbing参数
    ///
    /// 用途:
    /// - 在OnMove中判断是否进入爬墙状态
    /// - 在FixedUpdate中判断是否使用爬墙物理
    /// - 在OnJump中判断是否进行壁跳
    ///
    /// 进入条件:
    /// - 接触墙壁(IsOnWall = true)
    /// - 有垂直方向的输入(moveInputVertical ≠ 0)
    /// - 允许移动(CanMove = true)
    ///
    /// 退出条件:
    /// - 释放垂直输入(moveInputVertical = 0)
    /// - 离开墙壁(IsOnWall = false)
    /// - 执行跳跃(OnJump触发)
    /// </summary>
    public bool IsClimbing
    {
        get
        {
            return _isClimbing;
        }
        private set
        {
            _isClimbing = value;
            // 同步爬墙状态到Animator
            animator.SetBool(AnimationStrings.isClimbing, value);
        }
    }




    /// <summary>刚体组件引用(用于应用速度)</summary>
    Rigidbody2D rb;

    /// <summary>动画系统组件引用(用于驱动动画状态)</summary>
    Animator animator;

    /// <summary>能力系统（阶段 3B）</summary>
    private AbilitySystem abilitySystem;


    /// <summary>
    /// Awake生命周期函数
    /// 用于在Game Object激活时初始化组件引用
    /// 阶段 3A: 从 PlayerConfig 加载配置并应用到组件
    /// </summary>
    private void Awake()
    {
        // 获取当前GameObject上的Rigidbody2D组件
        rb = GetComponent<Rigidbody2D>();

        // 获取当前GameObject上的Animator组件
        animator = GetComponent<Animator>();

        // 获取当前GameObject上的TouchingDirections组件(碰撞检测)
        touchingDirections = GetComponent<TouchingDirections>();
        damageable = GetComponent<Damageable>();

        // 阶段 3A: 从 PlayerConfig 加载配置
        LoadConfigFromPlayerConfig();

        // 阶段 3B: 构建能力系统（当 usePlayerConfigFromCastleDb = true 时）
        if (usePlayerConfigFromCastleDb)
        {
            BuildAbilitySystem();
        }
    }

    /// <summary>
    /// 从 PlayerConfig 加载配置并应用到组件（阶段 3A）
    /// - 如果 usePlayerConfigFromCastleDb=false，跳过加载（使用硬编码默认值）
    /// - 如果未设置 playerConfig，使用默认的硬编码值
    /// - 应用移动速度参数到 PlayerController
    /// - 应用生命值参数到 Damageable 组件
    /// - 应用攻击伤害覆盖到所有 Attack 子物体
    /// </summary>
    private void LoadConfigFromPlayerConfig()
    {
        // 检查回退开关
        if (!usePlayerConfigFromCastleDb)
        {
            Debug.Log("[PlayerController] usePlayerConfigFromCastleDb=false，使用硬编码默认值");
            return;
        }

        // 如果没有配置 PlayerConfig，使用默认值
        if (playerConfig == null)
        {
            Debug.LogWarning("[PlayerController] playerConfig 未设置，使用默认值");
            return;
        }

        // 应用移动速度参数
        walkSpeed = playerConfig.walkSpeed;
        runSpeed = playerConfig.runSpeed;
        airWalkSpeed = playerConfig.airWalkSpeed;
        jumpImpules = playerConfig.jumpImpulse;  // 注意字段名映射
        climbSpeed = playerConfig.climbSpeed;

        // 应用 Damageable 配置
        if (damageable != null)
        {
            DamageableStats stats = new DamageableStats
            {
                maxHealth = playerConfig.maxHealth,
                invincibilityTime = playerConfig.invincibilityTime,
                knockbackMultiplier = 1.0f  // 玩家受击击退倍率（可后续从配置读取）
            };
            damageable.Configure(stats);

            Debug.Log($"[PlayerController] 已从 PlayerConfig 应用配置: " +
                $"walkSpeed={walkSpeed}, runSpeed={runSpeed}, maxHealth={playerConfig.maxHealth}, " +
                $"baseAttackDamage={playerConfig.baseAttackDamage}");
        }
        else
        {
            Debug.LogError("[PlayerController] Damageable 组件未找到，无法应用配置");
        }

        // 阶段 3A: 应用攻击伤害覆盖
        ApplyAttackDamageOverrides();
    }

    /// <summary>
    /// 应用攻击伤害覆盖到所有 Attack 子物体（阶段 3A）
    /// 遍历所有子物体，找到 Attack 组件，根据 attackId 从 PlayerConfig 计算最终伤害
    /// </summary>
    private void ApplyAttackDamageOverrides()
    {
        if (playerConfig == null)
        {
            return;
        }

        // 获取所有子物体的 Attack 组件（包括深层嵌套）
        Attack[] attacks = GetComponentsInChildren<Attack>(true);

        if (attacks.Length == 0)
        {
            Debug.LogWarning("[PlayerController] 未找到任何 Attack 组件");
            return;
        }

        int appliedCount = 0;
        foreach (var attack in attacks)
        {
            // 跳过未配置 attackId 的 Attack
            if (string.IsNullOrEmpty(attack.attackId))
            {
                Debug.LogWarning($"[PlayerController] Attack 组件 '{attack.gameObject.name}' 的 attackId 为空，跳过");
                continue;
            }

            // 从 PlayerConfig 计算最终伤害
            int finalDamage = playerConfig.CalculateFinalDamage(
                PlayerAttackOverride.TargetType.Hitbox,
                attack.attackId
            );

            // 应用伤害值
            attack.attackDamage = finalDamage;
            appliedCount++;

            Debug.Log($"[PlayerController] 已应用攻击伤害覆盖: " +
                $"attackId={attack.attackId}, finalDamage={finalDamage}, " +
                $"GameObject={attack.gameObject.name}");
        }

        Debug.Log($"[PlayerController] 攻击伤害覆盖应用完成，共处理 {appliedCount}/{attacks.Length} 个 Attack 组件");
    }

    /// <summary>
    /// 构建能力系统（阶段 3B）
    /// - 从 Resources 加载 AbilityCatalog
    /// - 根据 catalog 构建 AbilitySystem 并注册能力
    /// - 如果某个 hookType 没有任何启用的能力，输出警告（但允许运行）
    /// - 如果 usePlayerConfigFromCastleDb=true 但 AbilityCatalog 缺失，抛出异常（硬失败）
    /// </summary>
    private void BuildAbilitySystem()
    {
        // 加载 AbilityCatalog
        AbilityCatalog catalog = Resources.Load<AbilityCatalog>("Config/AbilityCatalog");
        if (catalog == null)
        {
            // 硬失败：能力系统是必需的，不允许回退到旧逻辑
            string errorMsg = "[PlayerController] usePlayerConfigFromCastleDb=true 但未找到 AbilityCatalog (Resources/Config/AbilityCatalog.asset)，" +
                "能力系统构建失败。请运行 Tools/CastleDB/Import All 生成 AbilityCatalog，或将 usePlayerConfigFromCastleDb 设为 false。";
            Debug.LogError(errorMsg, this);
            throw new System.InvalidOperationException(errorMsg);
        }

        Debug.Log($"[PlayerController] 开始构建能力系统，共 {catalog.entries.Count} 个能力配置");

        // 创建 AbilitySystem 实例
        abilitySystem = new AbilitySystem();

        // 统计每个 hookType 的注册数量（用于验证覆盖率）
        Dictionary<AbilityHookType, int> hookTypeCount = new Dictionary<AbilityHookType, int>();
        foreach (AbilityHookType hookType in System.Enum.GetValues(typeof(AbilityHookType)))
        {
            hookTypeCount[hookType] = 0;
        }

        // 遍历 catalog，为每个启用的能力创建实例并注册
        int registeredCount = 0;
        int skippedCount = 0;
        foreach (var entry in catalog.entries)
        {
            // 跳过禁用的能力
            if (!entry.enabled)
            {
                Debug.Log($"[PlayerController] 跳过禁用的能力: {entry.id}");
                skippedCount++;
                continue;
            }

            // 使用 AbilityRegistry 创建能力实例
            IPlayerAbility ability = AbilityRegistry.CreateAbility(
                entry.id,
                this,
                entry.priority,
                entry.enabled
            );

            if (ability == null)
            {
                Debug.LogError($"[PlayerController] 能力创建失败: {entry.id}，跳过注册");
                continue;
            }

            // 注册到 AbilitySystem
            abilitySystem.RegisterAbility(entry.hookType, ability);
            hookTypeCount[entry.hookType]++;
            registeredCount++;

            Debug.Log($"[PlayerController] 已注册能力: {entry.id}, hookType={entry.hookType}, priority={entry.priority}");
        }

        // 验证覆盖率：检查每个 hookType 是否至少有一个启用的能力
        foreach (var kvp in hookTypeCount)
        {
            if (kvp.Value == 0)
            {
                Debug.LogWarning($"[PlayerController] hookType {kvp.Key} 没有任何启用的能力，输入将无效");
            }
        }

        Debug.Log($"[PlayerController] 能力系统构建完成: 注册 {registeredCount} 个，跳过 {skippedCount} 个");

        // ===== Phase 4 集成：注入 AbilitySystem 到 PlayerContext =====
        PlayerContext playerContext = GetComponent<PlayerContext>();
        if (playerContext != null)
        {
            playerContext.SetAbilitySystem(abilitySystem);
            Debug.Log("[PlayerController] 已注入 AbilitySystem 到 PlayerContext");
        }
        else
        {
            Debug.LogError("[PlayerController] 未找到 PlayerContext 组件，无法注入 AbilitySystem");
        }

        // ===== Phase 4-6 集成说明 =====
        // 注意：玩家各模块初始化由 GameBootstrap 触发，但入口已收拢到 PlayerContext.InitializeModules（Phase 3，契约 [C-Runtime-0]）
        // PlayerController 仅负责创建 AbilitySystem 并注入到 PlayerContext
        // GameBootstrap.Awake 将完成以下装配：
        //   1. 加载 ItemCatalog 并创建 CastleDbService
        //   2. 加载 HudBinding 并实例化/定位 HUD
        //   3. player.InitializeModules(items, cfg, hudPresenter, hudRefs)
        // GameBootstrap.Start 将执行：
        //   - player.SyncInitialAbilities()
    }

    /// <summary>
    /// [已废弃 - 阶段 3A] 应用Projectile伤害覆盖
    ///
    /// 注意：此方法已废弃，不再使用。
    /// 阶段 3A 使用 prefab 级一次性赋值（在 CastleDB Import 时完成）。
    /// Projectile prefab 的 damage 字段在导入时已被正确设置，运行时不需要再修改。
    ///
    /// 保留此方法仅用于向后兼容，实际运行时不应调用。
    /// </summary>
    [System.Obsolete("使用 prefab 级一次性赋值，此方法已废弃", true)]
    public void ApplyProjectileDamageOverride(GameObject projectileObject, string resourcesPath)
    {
        // 此方法已废弃，不应调用
        Debug.LogWarning("[PlayerController] ApplyProjectileDamageOverride 已废弃，使用 prefab 级赋值");
    }

    // Start：Unity 生命周期回调（当前未使用）
    void Start()
    {

    }

    // Update：Unity 生命周期回调（当前未使用）
    void Update()
    {

    }

    /// <summary>
    /// FixedUpdate生命周期函数
    /// 每个物理帧调用一次，用于更新物理相关的逻辑
    ///
    /// 功能:
    /// - 判断是否在爬墙：IsClimbing
    /// - 如果爬墙: 使用climbInput和climbSpeed控制垂直速度
    /// - 如果正常: 根据CurrentMoveSpeed和水平输入更新速度
    /// - 将Y轴速度同步到Animator，用于控制下落/上升动画
    ///
    /// 爬墙物理:
    /// - X轴速度 = 0 (完全贴住墙壁)
    /// - Y轴速度 = climbInput.y × climbSpeed (由玩家输入控制)
    ///
    /// 正常物理:
    /// - X轴速度 = moveInputHorizontal × CurrentMoveSpeed
    /// - Y轴速度 = 保持不变(由重力和跳跃控制)
    /// </summary>
    private void FixedUpdate()
    {
        // 判断爬墙状态并应用对应的物理
        if (IsClimbing)
        {
            // 爬墙模式: X轴锁定为0(贴在墙上), Y轴由爬墙输入控制
            rb.velocity = new Vector2(0, climbInput.y * climbSpeed);

            // 同步爬墙速度到Animator的climbSpeed参数
            // 用于驱动爬墙动画的混合(向上/停止/向下)
            animator.SetFloat(AnimationStrings.climbSpeed, climbInput.y);
        }
        else if (!damageable.LockVelocity)
        {
            // 正常模式: 使用水平输入和当前速度
            rb.velocity = new Vector2(moveInputHorizontal * CurrentMoveSpeed, rb.velocity.y);
        }

        // 将当前Y轴速度同步到Animator
        // 用于控制下落/上升动画，以及到达最高点时的转换
        animator.SetFloat(AnimationStrings.yVelocity, rb.velocity.y);
    }

    /// <summary>
    /// LateUpdate生命周期函数（Phase 5）
    /// 每帧在所有 Update 完成后调用，用于刷新 AbilitySystem 的待处理状态变更
    ///
    /// 功能:
    /// - 调用 AbilitySystem.FlushPendingChanges() 统一应用本帧累积的 Enable/Disable 操作
    /// - 保证状态变更的保序去重语义（同帧多次操作只应用最后一次）
    /// </summary>
    private void LateUpdate()
    {
        // Phase 5: 刷新能力系统的待处理状态变更
        if (usePlayerConfigFromCastleDb && abilitySystem != null)
        {
            abilitySystem.FlushPendingChanges();
        }
    }

    /// <summary>
    /// 移动输入回调函数（阶段 3B：适配器模式）
    /// 由Input System在输入事件发生时调用
    ///
    /// 适配器职责（阶段 3B）：
    /// - 当 usePlayerConfigFromCastleDb=true: 仅适配输入并 Dispatch 到能力系统，不执行业务逻辑
    /// - 当 usePlayerConfigFromCastleDb=false: 执行原有业务逻辑（回退方案）
    ///
    /// 原有功能（回退模式）:
    /// - 读取移动输入的Vector2值(WASD或摇杆)
    /// - 分离处理水平输入(X轴/A/D)和垂直输入(Y轴/W/S)
    /// - 水平输入驱动行走/奔跑动画和角色朝向
    /// - 垂直输入用于爬墙系统(W/S控制上下爬行)
    /// </summary>
    public void OnMove(InputAction.CallbackContext context)
    {
        // 从输入事件读取Vector2值(来自WASD键或左摇杆)
        moveInput = context.ReadValue<Vector2>();

        // 阶段 3B: 适配器模式分支
        if (usePlayerConfigFromCastleDb && abilitySystem != null)
        {
            // Dispatch 到能力系统（不执行业务逻辑）
            AbilityInput input = AbilityInput.Performed(moveInput, true);
            abilitySystem.Dispatch(AbilityHookType.Move, input);
            return; // 立即返回，不执行下方的原有逻辑
        }

        // 回退模式：执行原有业务逻辑
        if (IsAlive)
        {
            // 分离水平和垂直输入分量
            // 水平输入(A/D): 用于行走/奔跑
            moveInputHorizontal = moveInput.x;

            // 垂直输入(W/S): 用于爬墙系统
            moveInputVertical = moveInput.y;

            // 同时保存给爬墙使用
            climbInput = moveInput;
            // 爬墙逻辑判断
            // 条件: 接触墙壁 && 有垂直输入 && 允许移动
            if (touchingDirections.IsOnWall && moveInputVertical != 0 && CanMove)
            {
                // 进入爬墙状态
                IsClimbing = true;
            }
            else if (!touchingDirections.IsOnWall || moveInputVertical == 0)
            {
                // 退出爬墙状态: 离开墙壁 或 没有垂直输入
                IsClimbing = false;
            }

            // 根据爬墙状态更新行走状态和朝向
            if (!IsClimbing)
            {
                // 正常模式: 判断是否行走，更新朝向
                IsMoving = moveInputHorizontal != 0;
                SetFacingDirection(moveInput);
            }
            else
            {
                // 爬墙模式: 禁止水平移动，保持朝向
                IsMoving = false;
                // 朝向保持不变，不调用SetFacingDirection
            }
        }
        else
        {
            IsMoving = false;
        }
    }

    /// <summary>
    /// 设置角色朝向函数
    ///
    /// 根据水平输入的X分量判断角色应该朝向的方向
    /// - moveInput.x > 0 -> 朝向右侧
    /// - moveInput.x < 0 -> 朝向左侧
    /// - moveInput.x = 0 -> 保持当前朝向
    ///
    /// 说明: 仅检查X分量(水平方向)
    ///       Y分量(垂直方向/W/S)不影响朝向
    ///
    /// 参数:
    /// - moveInput: 移动输入向量
    /// </summary>
    private void SetFacingDirection(Vector2 moveInput)
    {
        // 如果输入向右且当前朝向左侧，则改为朝向右侧
        if (moveInput.x > 0 && !IsFacingRight)
        {
            // 设置朝向为右
            IsFacingRight = true;
        }
        // 如果输入向左且当前朝向右侧，则改为朝向左侧
        else if (moveInput.x < 0 && IsFacingRight)
        {
            // 设置朝向为左
            IsFacingRight = false;
        }
        // 注意: 当moveInput.x = 0(包括只按W/S)时，朝向不变
    }

    /// <summary>
    /// 奔跑输入回调函数（阶段 3B：适配器模式）
    /// 由Input System在按下/释放奔跑键时调用(默认Shift键)
    ///
    /// 适配器职责（阶段 3B）：
    /// - 当 usePlayerConfigFromCastleDb=true: 仅适配输入并 Dispatch 到能力系统
    /// - 当 usePlayerConfigFromCastleDb=false: 执行原有业务逻辑（回退方案）
    ///
    /// 原有功能（回退模式）:
    /// - 按下时设置IsRunning=true，启用奔跑状态
    /// - 释放时设置IsRunning=false，返回行走状态
    /// </summary>
    public void OnRun(InputAction.CallbackContext context)
    {
        // 阶段 3B: 适配器模式分支
        if (usePlayerConfigFromCastleDb && abilitySystem != null)
        {
            // 适配输入阶段
            AbilityInput input;
            if (context.started)
            {
                input = AbilityInput.Started(isPressed: true);
            }
            else if (context.canceled)
            {
                input = AbilityInput.Canceled();
            }
            else
            {
                return; // 忽略其他阶段
            }

            // Dispatch 到能力系统
            abilitySystem.Dispatch(AbilityHookType.Run, input);
            return;
        }

        // 回退模式：执行原有业务逻辑
        if (context.started)
        {
            // 奔跑键按下 - 启用奔跑
            IsRunning = true;
        }
        else if (context.canceled)
        {
            // 奔跑键释放 - 禁用奔跑
            IsRunning = false;
        }
    }

    /// <summary>
    /// 跳跃输入回调函数（阶段 3B：适配器模式）
    /// 由Input System在按下空格键时调用
    ///
    /// 适配器职责（阶段 3B）：
    /// - 当 usePlayerConfigFromCastleDb=true: 仅适配输入并 Dispatch 到能力系统
    /// - 当 usePlayerConfigFromCastleDb=false: 执行原有业务逻辑（回退方案）
    ///
    /// 原有功能（回退模式）:
    /// - 支持地面跳跃和壁跳
    /// - 触发Animator的跳跃动画
    /// - 给予Y轴速度(jumpImpules)实现向上运动
    /// - 壁跳时给予横向冲力
    /// </summary>
    public void OnJump(InputAction.CallbackContext context)
    {
        // 阶段 3B: 适配器模式分支
        if (usePlayerConfigFromCastleDb && abilitySystem != null)
        {
            // 只处理 started 阶段
            if (context.started)
            {
                AbilityInput input = AbilityInput.Started(isPressed: true);
                abilitySystem.Dispatch(AbilityHookType.Jump, input);
            }
            return;
        }

        // 回退模式：执行原有业务逻辑
        if (context.started && CanMove)
        {
            // 支持地面跳跃或壁跳
            bool canJumpFromGround = touchingDirections.IsGrounded;
            bool canJumpFromWall = IsClimbing && touchingDirections.IsOnWall;

            if (canJumpFromGround || canJumpFromWall)
            {
                // 触发Animator的跳跃动画
                animator.SetTrigger(AnimationStrings.jumpTrigger);

                if (canJumpFromWall)
                {
                    // 壁跳逻辑: 给予离墙的横向冲力 + 向上冲力
                    float wallJumpForce = 8f;
                    float horizontalForce = IsFacingRight ? -wallJumpForce : wallJumpForce;

                    // 设置速度: 横向冲力 + 向上冲力
                    rb.velocity = new Vector2(horizontalForce, jumpImpules);

                    // 立即退出爬墙状态
                    IsClimbing = false;
                }
                else
                {
                    // 地面跳跃: 保持X轴速度，只改变Y轴
                    rb.velocity = new Vector2(rb.velocity.x, jumpImpules);
                }
            }
        }
    }

    /// <summary>
    /// 攻击输入回调函数（阶段 3B：适配器模式）
    /// 由Input System在按下攻击键时调用(默认Z键或J键)
    ///
    /// 适配器职责（阶段 3B）：
    /// - 当 usePlayerConfigFromCastleDb=true: 仅适配输入并 Dispatch 到能力系统
    /// - 当 usePlayerConfigFromCastleDb=false: 执行原有业务逻辑（回退方案）
    ///
    /// 原有功能（回退模式）:
    /// - 触发Animator的攻击动画
    /// - 动画系统会自动控制CanMove参数，禁止攻击时的移动
    /// </summary>
    public void OnAttack(InputAction.CallbackContext context)
    {
        // 阶段 3B: 适配器模式分支
        if (usePlayerConfigFromCastleDb && abilitySystem != null)
        {
            // 只处理 started 阶段
            if (context.started)
            {
                AbilityInput input = AbilityInput.Started(isPressed: true);
                abilitySystem.Dispatch(AbilityHookType.Attack, input);
            }
            return;
        }

        // 回退模式：执行原有业务逻辑
        if (context.started)
        {
            // 触发Animator的攻击动画
            animator.SetTrigger(AnimationStrings.attackTrigger);
        }
    }


    /// <summary>
    /// 远程攻击输入回调函数（阶段 3B：适配器模式）
    /// 由Input System在按下远程攻击键时调用
    ///
    /// 适配器职责（阶段 3B）：
    /// - 当 usePlayerConfigFromCastleDb=true: 仅适配输入并 Dispatch 到能力系统
    /// - 当 usePlayerConfigFromCastleDb=false: 执行原有业务逻辑（回退方案）
    ///
    /// 原有功能（回退模式）:
    /// - 触发Animator的远程攻击动画
    /// </summary>
    public void OnRangedAttack(InputAction.CallbackContext context)
    {
        // 阶段 3B: 适配器模式分支
        if (usePlayerConfigFromCastleDb && abilitySystem != null)
        {
            // 只处理 started 阶段
            if (context.started)
            {
                AbilityInput input = AbilityInput.Started(isPressed: true);
                abilitySystem.Dispatch(AbilityHookType.RangedAttack, input);
            }
            return;
        }

        // 回退模式：执行原有业务逻辑
        if (context.started)
        {
            // 触发Animator的攻击动画
            animator.SetTrigger(AnimationStrings.rangedAttackTrigger);
        }
    }



    public void OnHit(int damage, Vector2 knockback)
    {
        rb.velocity = new Vector2(knockback.x, rb.velocity.y + knockback.y);
    }

    // ===== 阶段 3B: 能力系统公开 API =====

    /// <summary>
    /// 应用移动输入（阶段 3B 能力系统专用 API）
    ///
    /// 功能：
    /// - 更新输入缓存（moveInput, moveInputHorizontal, moveInputVertical, climbInput）
    /// - 执行爬墙逻辑判断
    /// - 更新 IsMoving 状态（触发 Animator 同步）
    /// - 更新角色朝向（触发 Transform 翻转）
    ///
    /// 设计原则：
    /// - 能力实现不直接操作私有字段，通过此 API 复用现有逻辑
    /// - 保证 Animator 参数、Transform 翻转、物理输入缓存三者一致
    /// </summary>
    public void ApplyMoveInput(Vector2 move)
    {
        // 更新输入缓存
        moveInput = move;
        moveInputHorizontal = move.x;
        moveInputVertical = move.y;
        climbInput = move;

        if (!IsAlive)
        {
            IsMoving = false;
            return;
        }

        // 爬墙逻辑判断
        if (touchingDirections.IsOnWall && moveInputVertical != 0 && CanMove)
        {
            IsClimbing = true;
        }
        else if (!touchingDirections.IsOnWall || moveInputVertical == 0)
        {
            IsClimbing = false;
        }

        // 根据爬墙状态更新行走状态和朝向
        if (!IsClimbing)
        {
            IsMoving = moveInputHorizontal != 0;
            SetFacingDirection(moveInput);
        }
        else
        {
            IsMoving = false;
        }
    }

    /// <summary>
    /// 设置奔跑状态（阶段 3B 能力系统专用 API）
    ///
    /// 功能：
    /// - 使用 IsRunning 属性 setter，保证 Animator 参数同步
    ///
    /// 设计原则：
    /// - 能力实现不直接操作 _isRunning 字段，通过此 API 保证副作用一致
    /// </summary>
    public void SetRunning(bool running)
    {
        IsRunning = running;
    }
}
