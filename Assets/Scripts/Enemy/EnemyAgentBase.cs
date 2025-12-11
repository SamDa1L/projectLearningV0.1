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

        // ===== 解决检测区依赖 =====
        ResolveDetectionZone();

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
    ///
    /// 与GetDetectedTargetsForRole()不同，此方法返回DetectionZone组件本身
    /// 常用于需要访问zone的其他属性，或者Gizmos可视化
    ///
    /// 使用示例：
    /// - Gizmos可视化中获取zone组件来绘制
    /// - 编辑器工具中检查zone的配置
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

        // 在编辑器中显示标签
        #if UNITY_EDITOR
        UnityEditor.Handles.Label(labelPos, label);
        #endif
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
