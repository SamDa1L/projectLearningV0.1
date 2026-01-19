using System.Collections.Generic;
using CastleDB.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 玩家上下文组件
/// 统一暴露玩家相关的所有服务引用（单人项目口径）
///
/// 规范（契约 [C-Runtime-4a]）：
/// - 挂载位置：Player 根 GameObject
/// - 装配时机：Awake/OnEnable 完成自身引用缓存（仅 GetComponent，不查询 CastleDbService）
/// - 必需依赖：Inventory、Damageable、AbilitySystem（缺失则 Error 并设置 InteractionEnabled=false）
/// - 建议依赖：ReplaceController（缺失则 Error 一次性，仅禁用 Replace 功能）
/// - 可选依赖：EquipmentController、PlayerInput、PlayerRelicController（Phase 7）
/// - HudRefs/HudPresenter 由 GameBootstrap 负责加载/实例化，并通过 InitializeModules 注入
/// </summary>
public class PlayerContext : MonoBehaviour
{
    // ===== 必需依赖 =====
    /// <summary>
    /// 玩家背包系统（必需）
    /// </summary>
    public PlayerInventory Inventory { get; private set; }

    /// <summary>
    /// 伤害系统（必需）
    /// </summary>
    public Damageable Damageable { get; private set; }

    /// <summary>
    /// 能力调度系统（必需）
    /// </summary>
    public AbilitySystem AbilitySystem { get; private set; }

    // ===== 建议依赖 =====
    /// <summary>
    /// 替换控制器（建议存在，缺失时仅禁用 Replace 功能）
    /// </summary>
    public ReplaceController ReplaceController { get; private set; }

    // ===== 可选依赖 =====
    /// <summary>
    /// 装备控制器（可选）
    /// </summary>
    public PlayerEquipmentController EquipmentController { get; private set; }

    /// <summary>
    /// 输入系统（可选）
    /// </summary>
    public PlayerInput PlayerInput { get; private set; }

    /// <summary>
    /// 遗物控制器（可选，Phase 7）
    /// 负责遗物拾取后的常驻被动效果（例如护盾）
    /// </summary>
    public PlayerRelicController RelicController { get; private set; }

    // ===== 状态标记 =====
    /// <summary>
    /// 交互是否启用
    /// 仅当必需依赖（Inventory/Damageable/AbilitySystem）缺失时置为 false
    /// </summary>
    public bool InteractionEnabled { get; private set; } = true;

    // ===== Runtime 装配状态（Phase 3）=====
    private bool _runtimeModulesInitialized = false;

    // ===== 一次性日志去重 =====
    private static readonly HashSet<string> _loggedWarnings = new HashSet<string>();

    // ===== 生命周期 =====
    private void Awake()
    {
        CacheDependencies();
    }

    /// <summary>
    /// 缓存所有依赖引用
    /// </summary>
    private void CacheDependencies()
    {
        // 必需依赖
        Inventory = FindSingleInHierarchy<PlayerInventory>();
        Damageable = FindSingleInHierarchy<Damageable>();

        // AbilitySystem 由 PlayerController 构建，并通过 SetAbilitySystem 注入到此处

        // 建议依赖
        ReplaceController = FindSingleInHierarchy<ReplaceController>();

        // 可选依赖
        EquipmentController = FindSingleInHierarchy<PlayerEquipmentController>();
        PlayerInput = FindSingleInHierarchy<PlayerInput>();
        RelicController = FindSingleInHierarchy<PlayerRelicController>();

        // 校验必需依赖
        ValidateDependencies();
    }

    private T FindSingleInHierarchy<T>() where T : Component
    {
        // 向后兼容：优先使用挂在当前 GameObject 上的组件
        T onSelf = GetComponent<T>();

        // 包含 inactive 子节点，支持按 Prefab 层级分区放置模块
        T[] all = GetComponentsInChildren<T>(true);
        if (all == null || all.Length == 0)
        {
            return onSelf;
        }

        if (all.Length > 1)
        {
            string key = $"PlayerContext_Multiple_{typeof(T).Name}";
            if (!_loggedWarnings.Contains(key))
            {
                Debug.LogError($"[PlayerContext] 发现多个 {typeof(T).Name} 组件，期望唯一（count={all.Length}）", this);
                _loggedWarnings.Add(key);
            }
        }

        return onSelf != null ? onSelf : all[0];
    }

    /// <summary>
    /// 校验必需依赖
    /// </summary>
    private void ValidateDependencies()
    {
        // 硬缺失（组件层面）：Inventory 和 Damageable 应该在 Awake 时就存在
        bool hasHardMissingDependencies = false;

        // 校验 Inventory（硬缺失）
        if (Inventory == null)
        {
            Debug.LogError($"[PlayerContext] 缺少必需依赖：PlayerInventory", this);
            hasHardMissingDependencies = true;
        }

        // 校验 Damageable（硬缺失）
        if (Damageable == null)
        {
            Debug.LogError($"[PlayerContext] 缺少必需依赖：Damageable", this);
            hasHardMissingDependencies = true;
        }

        // 校验 AbilitySystem（等待注入）
        // AbilitySystem 不是 MonoBehaviour，需要外部通过 SetAbilitySystem 注入
        // Awake 阶段为 null 是预期行为，仅 Warning 不报 Error
        bool abilitySystemNotReady = false;
        if (AbilitySystem == null)
        {
            string key = "PlayerContext_MissingAbilitySystem_Awake";
            if (!_loggedWarnings.Contains(key))
            {
                Debug.LogWarning($"[PlayerContext] AbilitySystem 尚未注入（需要外部调用 SetAbilitySystem），交互功能将在注入前被禁用", this);
                _loggedWarnings.Add(key);
            }
            abilitySystemNotReady = true;
        }

        // 校验 ReplaceController（建议依赖）
        if (ReplaceController == null)
        {
            string key = "PlayerContext_MissingReplaceController";
            if (!_loggedWarnings.Contains(key))
            {
                Debug.LogError($"[PlayerContext] 缺少建议依赖：ReplaceController（Replace 功能将被禁用）", this);
                _loggedWarnings.Add(key);
            }
        }

        // 设置 InteractionEnabled
        // 只有硬缺失或 AbilitySystem 未就绪时才禁用交互
        if (hasHardMissingDependencies || abilitySystemNotReady)
        {
            InteractionEnabled = false;

            // 仅硬缺失时输出 Error，等待注入时不输出
            if (hasHardMissingDependencies)
            {
                Debug.LogError($"[PlayerContext] 必需组件缺失（Inventory/Damageable），交互功能已禁用", this);
            }
        }
    }

    /// <summary>
    /// 外部设置 AbilitySystem
    /// 由于 AbilitySystem 不是 MonoBehaviour，需要外部注入
    /// </summary>
    public void SetAbilitySystem(AbilitySystem abilitySystem)
    {
        if (AbilitySystem != null)
        {
            Debug.LogWarning($"[PlayerContext] AbilitySystem 已存在，不允许重复设置");
            return;
        }

        AbilitySystem = abilitySystem;

        // 重新校验并更新 InteractionEnabled
        if (AbilitySystem == null)
        {
            Debug.LogError($"[PlayerContext] SetAbilitySystem 传入空引用，交互功能已禁用", this);
            InteractionEnabled = false;
        }
        else
        {
            // 检查所有必需依赖是否都已满足
            bool allDependenciesMet = Inventory != null && Damageable != null && AbilitySystem != null;
            if (allDependenciesMet)
            {
                InteractionEnabled = true;
                Debug.Log($"[PlayerContext] AbilitySystem 注入成功，所有必需依赖已满足，交互功能已启用");
            }
            else
            {
                Debug.LogWarning($"[PlayerContext] AbilitySystem 已注入，但其他必需依赖仍缺失，交互功能保持禁用");
            }
        }
    }

    /// <summary>
    /// 统一初始化玩家 Runtime 模块（Phase 3）
    /// 由 GameBootstrap 在完成全局资源装配后调用，用于收拢 Inventory/HUD/Replace/Equipment 的初始化入口。
    ///
    /// 装配顺序（硬契约 [C-Runtime-0]）：
    /// 1) Inventory.Initialize(items, cfg)
    /// 2) RelicController.Initialize(items, relicCatalog, dmg)（可选，Phase 7）
    /// 3) hudPresenter.Initialize(items, hudRefs, inv, dmg, relicCtrl)
    /// 4) ReplaceController.Initialize(items, hudRefs, ctx, hudPresenter)（可选）
    /// 5) EquipmentController.Initialize(items, abilitySystem, inv)（可选）
    /// </summary>
    public bool InitializeModules(ICastleDbService items, GameplayConfig cfg, RelicCatalog relicCatalog, HudPresenter hudPresenter, HudRefs hudRefs)
    {
        if (_runtimeModulesInitialized)
        {
            const string key = "PlayerContext_InitializeModules_AlreadyInitialized";
            if (!_loggedWarnings.Contains(key))
            {
                Debug.LogWarning("[PlayerContext] InitializeModules 已执行，忽略重复调用", this);
                _loggedWarnings.Add(key);
            }
            return true;
        }

        // 兜底：不假设 PlayerContext.Awake 已先执行，初始化前再次缓存依赖
        CacheDependencies();

        if (items == null)
        {
            Debug.LogError("[PlayerContext] InitializeModules 失败：items 为空", this);
            return false;
        }

        if (Inventory == null || Damageable == null)
        {
            Debug.LogError("[PlayerContext] InitializeModules 失败：必需组件缺失（Inventory/Damageable）", this);
            return false;
        }

        if (AbilitySystem == null)
        {
            Debug.LogError("[PlayerContext] InitializeModules 失败：AbilitySystem 未注入（请检查 PlayerController.BuildAbilitySystem）", this);
            return false;
        }

        if (hudPresenter == null || hudRefs == null)
        {
            Debug.LogError("[PlayerContext] InitializeModules 失败：HUD 引用为空（HudPresenter/HudRefs）", this);
            return false;
        }

        // 1) Inventory
        Inventory.Initialize(items, cfg);

        // 2) Relic（Phase 7，可选）
        // 为了确保护盾等拦截器能被 Damageable.Hit 命中（GetComponents<IDamageInterceptor>），优先挂在 Damageable 同一 GameObject 上。
        if (RelicController == null && relicCatalog != null)
        {
            RelicController = Damageable.GetComponent<PlayerRelicController>();
            if (RelicController == null)
            {
                RelicController = Damageable.gameObject.AddComponent<PlayerRelicController>();
                Debug.LogWarning("[PlayerContext] 未找到 PlayerRelicController，已在运行时自动添加（Phase 7）", this);
            }
        }

        if (RelicController != null)
        {
            RelicController.Initialize(items, relicCatalog, Damageable);
        }

        // 3) HUD
        hudPresenter.Initialize(items, hudRefs, Inventory, Damageable, RelicController, AbilitySystem);

        // 4) Replace（建议依赖）
        if (ReplaceController != null)
        {
            ReplaceController.Initialize(items, hudRefs, this, hudPresenter);
        }

        // 5) Equipment（可选）
        if (EquipmentController != null)
        {
            EquipmentController.Initialize(items, AbilitySystem, Inventory);
        }

        _runtimeModulesInitialized = true;
        return true;
    }

    /// <summary>
    /// 初次同步能力槽位到 AbilitySystem（契约 [C-Runtime-0]）
    /// 建议在 GameBootstrap.Start 调用，确保所有 Awake 完成后再执行。
    /// </summary>
    public bool SyncInitialAbilities()
    {
        if (!_runtimeModulesInitialized)
        {
            return false;
        }

        if (EquipmentController == null)
        {
            return false;
        }

        EquipmentController.SyncAllSlotsToAbilities();
        return true;
    }
}
