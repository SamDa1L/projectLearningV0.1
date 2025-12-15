using UnityEngine;
using System;

/// <summary>
/// 遗留敌人适配器
/// 为尚未迁移的旧敌人脚本提供过渡方案，支持边上线边迁移
///
/// 设计思路：
/// - 新的敌人直接继承EnemyAgentBase
/// - 旧的Knight/FlyingEye等敌人通过此适配器包装
/// - 适配器代理旧的Update逻辑到TickState()中
/// - 逐个迁移完成后可无缝替换，无需重新配置Prefab
///
/// 使用步骤：
/// 1. 在旧敌人脚本中提供更新逻辑的委托接口
/// 2. 创建LegacyEnemyAdapter并设置delegated逻辑
/// 3. 测试通过后，将旧逻辑重构为TickState/TickPhysics实现
/// 4. 移除适配器，直接继承EnemyAgentBase
/// </summary>
public class LegacyEnemyAdapter : EnemyAgentBase
{
    // ===== 遗留逻辑委托 =====
    /// <summary>
    /// 代理旧脚本的Update逻辑
    /// </summary>
    private Action<float> legacyTickLogic;

    /// <summary>
    /// 代理旧脚本的FixedUpdate逻辑（可选）
    /// </summary>
    private Action<float> legacyPhysicsLogic;

    // ===== 旧脚本引用 =====
    /// <summary>
    /// 保持对旧脚本的引用，用于过渡期间的兼容性
    /// </summary>
    private MonoBehaviour legacyComponent;

    // ===== 配置 =====
    [Header("过渡适配器")]
    [SerializeField] protected bool useLegacyLogic = true;
    [SerializeField] protected bool debugLegacyMigration = true;

    // ===== 公开接口 =====

    /// <summary>
    /// 设置遗留的状态更新逻辑
    /// 在Initialize()中调用，或在Awake后设置
    /// </summary>
    /// <param name="updateAction">旧脚本的Update委托</param>
    public void SetLegacyTickLogic(Action<float> updateAction)
    {
        legacyTickLogic = updateAction;
        if (debugLegacyMigration)
            Debug.Log($"[LegacyEnemyAdapter] {gameObject.name} - 已设置TickLogic委托", gameObject);
    }

    /// <summary>
    /// 设置遗留的物理更新逻辑
    /// 可选，用于迁移过程中逐步分离物理操作
    /// </summary>
    /// <param name="physicsAction">旧脚本的FixedUpdate委托</param>
    public void SetLegacyPhysicsLogic(Action<float> physicsAction)
    {
        legacyPhysicsLogic = physicsAction;
        if (debugLegacyMigration)
            Debug.Log($"[LegacyEnemyAdapter] {gameObject.name} - 已设置PhysicsLogic委托", gameObject);
    }

    /// <summary>
    /// 设置旧脚本的引用（用于后续清理）
    /// </summary>
    public void SetLegacyComponent(MonoBehaviour component)
    {
        legacyComponent = component;
    }

    /// <summary>
    /// 启用/禁用遗留逻辑的执行
    /// 在迁移过程中可用于A/B测试（对比旧新行为）
    /// </summary>
    public void SetLegacyLogicEnabled(bool enabled)
    {
        useLegacyLogic = enabled;
        if (debugLegacyMigration)
            Debug.Log($"[LegacyEnemyAdapter] {gameObject.name} - LegacyLogic {(enabled ? "启用" : "禁用")}", gameObject);
    }

    // ===== 生命周期覆盖 =====

    protected override void Initialize()
    {
        // 子类或外部调用者应在此之前设置委托
        // 例如：在Awake中设置，或通过SetLegacyTickLogic()设置
        base.Initialize();

        if (debugLegacyMigration && legacyTickLogic == null)
        {
            Debug.LogWarning($"[LegacyEnemyAdapter] {gameObject.name} - 未设置legacyTickLogic，TickState将为空操作", gameObject);
        }
    }

    /// <summary>
    /// 应用调参配置（覆盖基类以读取 useLegacyLogicFallback）
    /// Step 4: 从 Profile 读取 useLegacyLogicFallback 并应用到适配器
    /// </summary>
    protected override void ApplyTuningProfile()
    {
        base.ApplyTuningProfile();

        // 从 Profile 读取 useLegacyLogicFallback 并覆盖 Inspector 配置
        // 优先级：Profile > Inspector（数据驱动）
        if (TuningProfile != null)
        {
            bool profileFallback = _useLegacyLogicFallback;

            // 如果 Profile 的值与当前 Inspector 值不同，则应用 Profile 的值
            if (useLegacyLogic != profileFallback)
            {
                if (debugLegacyMigration)
                {
                    Debug.Log(
                        $"[LegacyEnemyAdapter] {gameObject.name} - 从 Profile 应用 useLegacyLogicFallback: {profileFallback}\n" +
                        $"  (Inspector 原值: {useLegacyLogic})",
                        gameObject
                    );
                }
                useLegacyLogic = profileFallback;
            }
        }
    }

    /// <summary>
    /// 状态更新 - 代理旧逻辑或调用新逻辑
    /// </summary>
    protected override void TickState(float deltaTime)
    {
        if (!useLegacyLogic)
        {
            // 新逻辑路径：调用虚函数供子类覆盖
            OnNewTickState(deltaTime);
            return;
        }

        // 遗留逻辑路径：执行代理委托
        if (legacyTickLogic != null)
        {
            try
            {
                legacyTickLogic(deltaTime);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[LegacyEnemyAdapter] {gameObject.name} - LegacyTickLogic执行异常: {ex.Message}", gameObject);
            }
        }
        else
        {
            // 降级方案：调用虚函数
            OnNewTickState(deltaTime);
        }
    }

    /// <summary>
    /// 物理更新 - 代理旧逻辑或调用新逻辑
    /// </summary>
    protected override void TickPhysics(float fixedDeltaTime)
    {
        if (!useLegacyLogic)
        {
            OnNewTickPhysics(fixedDeltaTime);
            return;
        }

        // 遗留逻辑路径
        if (legacyPhysicsLogic != null)
        {
            try
            {
                legacyPhysicsLogic(fixedDeltaTime);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[LegacyEnemyAdapter] {gameObject.name} - LegacyPhysicsLogic执行异常: {ex.Message}", gameObject);
            }
        }
        else
        {
            // 降级方案：调用虚函数
            OnNewTickPhysics(fixedDeltaTime);
        }
    }

    // ===== 虚函数钩子 - 子类可覆盖以实现新逻辑 =====

    /// <summary>
    /// 新的状态更新逻辑虚函数
    /// 子类可覆盖此方法以逐步迁移到新架构
    /// </summary>
    protected virtual void OnNewTickState(float deltaTime)
    {
        // 子类实现新逻辑
    }

    /// <summary>
    /// 新的物理更新逻辑虚函数
    /// 子类可覆盖此方法以逐步迁移到新架构
    /// </summary>
    protected virtual void OnNewTickPhysics(float fixedDeltaTime)
    {
        // 子类实现新逻辑
    }

    // ===== 迁移工具方法 =====

    /// <summary>
    /// 检查迁移状态
    /// 返回true表示此敌人已完全迁移到新架构
    /// </summary>
    public bool IsMigrated()
    {
        return !useLegacyLogic || legacyTickLogic == null;
    }

    /// <summary>
    /// 获取迁移进度信息（用于编辑器验证工具）
    /// </summary>
    public string GetMigrationStatus()
    {
        if (IsMigrated())
            return "完全迁移";

        string status = "使用遗留逻辑";
        if (legacyTickLogic != null)
            status += " (TickLogic)";
        if (legacyPhysicsLogic != null)
            status += " (PhysicsLogic)";

        return status;
    }

    /// <summary>
    /// 清理遗留组件和委托
    /// 完全迁移后调用此方法进行清理
    /// </summary>
    public void RemoveLegacyComponent()
    {
        if (debugLegacyMigration)
            Debug.Log($"[LegacyEnemyAdapter] {gameObject.name} - 清理遗留组件", gameObject);

        legacyTickLogic = null;
        legacyPhysicsLogic = null;

        if (legacyComponent != null)
        {
            if (debugLegacyMigration)
                Debug.Log($"[LegacyEnemyAdapter] {gameObject.name} - 移除旧脚本: {legacyComponent.GetType().Name}", gameObject);

            Destroy(legacyComponent);
            legacyComponent = null;
        }
    }

#if UNITY_EDITOR

    /// <summary>
    /// 编辑器调试显示
    /// </summary>
    protected override void OnDrawGizmosSelected()
    {
        base.OnDrawGizmosSelected();

        if (!debugLegacyMigration)
            return;

        // 绘制迁移状态指示
        if (cacheTransform == null)
            cacheTransform = transform;

        if (useLegacyLogic && legacyTickLogic != null)
        {
            // 红色圆圈表示使用遗留逻辑
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(cacheTransform.position, 0.15f);
        }
        else
        {
            // 绿色圆圈表示使用新逻辑
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(cacheTransform.position, 0.15f);
        }
    }

#endif
}
