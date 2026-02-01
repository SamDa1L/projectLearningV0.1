using UnityEngine;
using CastleDB.Runtime;

/// <summary>
/// Runtime 启动装配器（契约 [C-Runtime-0]）
///
/// 核心职责：
/// - 场景启动时统一完成依赖注入与资源装配
/// - 确保各 Runtime 模块在使用前已完成初始化
/// - 按硬契约顺序装配（ItemCatalog(+RelicCatalog) → HUD → PlayerModules）
///
/// 装配顺序（硬契约 [C-Runtime-0]）：
/// 1. 加载 ItemCatalog.asset 并初始化 CastleDbService
/// 2. 加载 HudBinding.asset 并实例化/定位 HUD
/// 3. 统一初始化玩家模块：player.InitializeModules(items, cfg, relicCatalog, hudPresenter, hudRefs)
///    - 内部顺序保持不变：Inventory → HUD → Replace(可选) → Equipment(可选)
///    - Phase 0.5：额外注入 IGameAssetProvider（统一资源访问入口）
/// 4. Start：player.SyncInitialAbilities()（初次同步）
///
/// 依赖定位（硬契约）：
/// - PlayerContext 通过序列化字段 player 持有（不使用 FindObjectOfType）
/// - 缺失必需资源时 Error 并中止后续装配
/// </summary>
public class GameBootstrap : MonoBehaviour
{
    // ===== 序列化字段（Inspector 配置）=====
    [Header("必需依赖")]
    [Tooltip("玩家上下文（单人项目）")]
    [SerializeField] private PlayerContext player;

    [Header("可选配置")]
    [Tooltip("HUD 根节点覆盖（若为空则实例化 HudBinding.hudPrefab）")]
    [SerializeField] private Transform hudRootOverride;

    // ===== 运行时状态 =====
    private CastleDbService _castleDbService;
    private GameplayConfig _gameplayConfig;
    private RelicCatalog _relicCatalog;
    private IGameAssetProvider _assets;
    private GameObject _hudRoot;  // Phase 7: HUD 实例根节点

    // ===== 生命周期 =====

    private void Awake()
    {
        Debug.Log("[GameBootstrap] ========== 开始 Runtime 装配 ==========");

        // Phase 0.5 / P1-1: 统一资源访问入口（避免到处散落 Resources.Load）
        _assets = new ResourcesGameAssetProvider();

        // 验证必需依赖
        if (player == null)
        {
            Debug.LogError("[GameBootstrap] 缺少必需依赖：PlayerContext（需在 Inspector 中配置 player 字段），中止装配", this);
            return;
        }

        // 验证 AbilitySystem（必须由 PlayerController 在 Awake 中创建并注入）
        if (player.AbilitySystem == null)
        {
            Debug.LogError("[GameBootstrap] PlayerContext.AbilitySystem 为空（应由 PlayerController.BuildAbilitySystem 创建），中止装配", this);
            return;
        }

        // 装配顺序（硬契约 [C-Runtime-0]）
        bool success = true;

        // Step 1: 加载 ItemCatalog 并初始化 CastleDbService（统一走 IGameAssetProvider）
        success = success && LoadItemCatalog();

        // Step 2: 加载 HudBinding 并实例化/定位 HUD（Phase 7）
        success = success && LoadHudBinding();

        // Step 3: 统一初始化玩家模块（Phase 3：收拢入口）
        success = success && InitializePlayerModules();

        if (success)
        {
            Debug.Log("[GameBootstrap] ========== Runtime 装配完成 ==========");
        }
        else
        {
            Debug.LogError("[GameBootstrap] ========== Runtime 装配失败（部分模块未初始化）==========", this);
        }
    }

    private void Start()
    {
        // 契约 [C-Runtime-0] Step 4: 在 Start 时执行初始同步
        // 确保在所有组件 Awake 完成后执行（避免 Awake 时序不确定）
        if (player != null)
        {
            if (player.SyncInitialAbilities())
            {
                Debug.Log("[GameBootstrap] 玩家能力初始同步已执行");
            }
        }
    }

    // ===== 装配步骤 =====

    /// <summary>
    /// Step 1: 加载 ItemCatalog 并初始化 CastleDbService
    /// </summary>
    private bool LoadItemCatalog()
    {
        Debug.Log("[GameBootstrap] [1/3] 加载 ItemCatalog...");

        // 加载 ItemCatalog.asset
        var itemCatalog = _assets != null ? _assets.ItemCatalog : null;
        if (itemCatalog == null)
        {
            Debug.LogError("[GameBootstrap] 缺失必需资源：ItemCatalog (Resources/Config/ItemCatalog.asset)，中止装配", this);
            return false;
        }

        // 创建 CastleDbService 实例
        _castleDbService = new CastleDbService();
        _castleDbService.SetItemCatalog(itemCatalog);

        // 验证资源有效性
        if (!_castleDbService.IsValid)
        {
            Debug.LogError("[GameBootstrap] ItemCatalog 资源损坏（ID 重复等），中止装配", this);
            return false;
        }

        Debug.Log($"[GameBootstrap] ItemCatalog 加载成功（{_castleDbService.GetAllItems().Count} 个 Item）");

        // 加载 GameplayConfig（可选）
        _gameplayConfig = _assets != null ? _assets.GameplayConfig : null;
        if (_gameplayConfig == null)
        {
            Debug.LogWarning("[GameBootstrap] GameplayConfig 未找到，Inventory 将使用默认配置", this);
        }

        // Phase 7：加载 RelicCatalog（可选，缺失则禁用遗物系统）
        _relicCatalog = _assets != null ? _assets.RelicCatalog : null;
        if (_relicCatalog == null)
        {
            Debug.LogWarning("[GameBootstrap] 未找到 RelicCatalog（Resources/Config/RelicCatalog.asset），遗物功能将被禁用", this);
        }
        else if (!_relicCatalog.IsValid)
        {
            Debug.LogError("[GameBootstrap] RelicCatalog 数据无效（ID 重复等），遗物功能将被禁用", this);
            _relicCatalog = null;
        }

        return true;
    }

    // ===== Phase 7：HUD 装配 =====

    /// <summary>
    /// Step 3: 加载 HudBinding 并实例化/定位 HUD（Phase 7）
    /// 契约 [C-Runtime-0]:
    /// - 若 hudRootOverride 存在则复用
    /// - 否则从 HudBinding.hudPrefab 实例化
    /// - 验证 HUD 实例上的 HudRefs 组件
    /// </summary>
    private bool LoadHudBinding()
    {
        Debug.Log("[GameBootstrap] [2/3] 加载 HudBinding...");

        // 加载 HudBinding.asset
        var binding = _assets != null ? _assets.HudBinding : null;
        if (binding == null)
        {
            Debug.LogError("[GameBootstrap] 缺失必需资源：HudBinding (Resources/Config/HudBinding.asset)，中止装配", this);
            return false;
        }

        // 验证 hudPrefab
        if (binding.hudPrefab == null)
        {
            Debug.LogError("[GameBootstrap] HudBinding.hudPrefab 为空（需通过 HUD Quick Config 重新生成/回填），中止装配", this);
            return false;
        }

        // 实例化或复用 HUD
        if (hudRootOverride != null)
        {
            _hudRoot = hudRootOverride.gameObject;
            Debug.Log("[GameBootstrap] 复用 hudRootOverride");
        }
        else
        {
            _hudRoot = Instantiate(binding.hudPrefab);
            Debug.Log("[GameBootstrap] 实例化 HudBinding.hudPrefab");
        }

        return true;
    }

    /// <summary>
    /// Step 3: 统一初始化玩家模块（Phase 3）
    /// 契约 [C-Runtime-0]：
    /// - 从 HUD 实例获取 HudPresenter 和 HudRefs
    /// - 调用 player.InitializeModules(items, cfg, relicCatalog, hudPresenter, hudRefs)
    /// </summary>
    private bool InitializePlayerModules()
    {
        Debug.Log("[GameBootstrap] [3/3] 初始化玩家模块...");

        if (_hudRoot == null)
        {
            Debug.LogError("[GameBootstrap] HUD 实例为空，无法初始化玩家模块", this);
            return false;
        }

        // 获取 HudPresenter 组件
        var hudPresenter = _hudRoot.GetComponentInChildren<HudPresenter>(true);
        if (hudPresenter == null)
        {
            Debug.LogError("[GameBootstrap] HUD 实例缺少 HudPresenter 组件，中止装配", this);
            return false;
        }

        // 获取 HudRefs 组件
        var hudRefs = _hudRoot.GetComponentInChildren<HudRefs>(true);
        if (hudRefs == null)
        {
            Debug.LogError("[GameBootstrap] HUD 实例缺少 HudRefs 组件，中止装配", this);
            return false;
        }

        var keyIconPresenter = _hudRoot.GetComponentInChildren<HudSlotKeyIconPresenter>(true);
        if (keyIconPresenter == null)
        {
            keyIconPresenter = _hudRoot.AddComponent<HudSlotKeyIconPresenter>();
        }

        var inputIconCatalog = _assets != null ? _assets.InputIconCatalog : null;
        if (inputIconCatalog == null)
        {
            Debug.LogWarning("[GameBootstrap] 未找到 InputIconCatalog（Resources/Config/InputIconCatalog.asset），按键图标不会更新", this);
        }
        else
        {
            InputModeSwitcher switcher = null;
            if (player.PlayerInput != null)
            {
                switcher = player.PlayerInput.GetComponent<InputModeSwitcher>();
            }

            keyIconPresenter.Initialize(hudRefs, player.PlayerInput, switcher, inputIconCatalog);
        }

        // 统一初始化入口（Inventory/HUD/Replace/Equipment）
        if (!player.InitializeModules(_castleDbService, _gameplayConfig, _relicCatalog, hudPresenter, hudRefs, _assets))
        {
            Debug.LogError("[GameBootstrap] PlayerContext.InitializeModules 执行失败，中止装配", this);
            return false;
        }

        return true;
    }
}
