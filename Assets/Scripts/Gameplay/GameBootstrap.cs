using UnityEngine;
using CastleDB.Runtime;

/// <summary>
/// Runtime 启动装配器（契约 [C-Runtime-0]）
///
/// 核心职责：
/// - 场景启动时统一完成依赖注入与资源装配
/// - 确保各 Runtime 模块在使用前已完成初始化
/// - 按硬契约顺序装配（ItemCatalog → Inventory → EquipmentController）
///
/// 装配顺序（硬契约 [C-Runtime-0]）：
/// 1. 加载 ItemCatalog.asset 并初始化 CastleDbService
/// 2. player.Inventory.Initialize(items, cfg)
/// 3. 加载 HudBinding.asset（Phase 7）
/// 4. hudPresenter.Initialize(...)（Phase 7）
/// 5. replaceController.Initialize(...)（Phase 8）
/// 6. equipmentController.Initialize(...) + SyncAllSlotsToAbilities()
///
/// 依赖定位（硬契约）：
/// - PlayerContext 通过序列化字段 player 持有（不使用 FindObjectOfType）
/// - 缺失必需资源时 Error 并中止后续装配
/// </summary>
[DefaultExecutionOrder(100)]
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
    private GameObject _hudRoot;  // Phase 7: HUD 实例根节点
    private HudPresenter _hudPresenter;  // Phase 8: HudPresenter 缓存（ReplaceController 需要）

    // ===== 生命周期 =====

    private void Awake()
    {
        Debug.Log("[GameBootstrap] ========== 开始 Runtime 装配 ==========");

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

        // Step 1: 加载 ItemCatalog 并初始化 CastleDbService
        success = success && LoadItemCatalog();

        // Step 2: 初始化 PlayerInventory
        success = success && InitializePlayerInventory();

        // Step 3-4: HUD 初始化（Phase 7）
        success = success && LoadHudBinding();
        success = success && InitializeHudPresenter();

        // Step 5: ReplaceController 初始化（Phase 8）
        success = success && InitializeReplaceController();

        // Step 6: 初始化 PlayerEquipmentController
        success = success && InitializePlayerEquipmentController();

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
        // 契约 [C-Runtime-0] Step 6: 在 Start 时执行初始同步
        // 确保在所有组件 Awake 完成后执行（避免 Awake 时序不确定）
        if (player != null && player.EquipmentController != null)
        {
            Debug.Log("[GameBootstrap] 执行 EquipmentController 初始同步...");
            player.EquipmentController.SyncAllSlotsToAbilities();
        }
    }

    // ===== 装配步骤 =====

    /// <summary>
    /// Step 1: 加载 ItemCatalog 并初始化 CastleDbService
    /// </summary>
    private bool LoadItemCatalog()
    {
        Debug.Log("[GameBootstrap] [1/6] 加载 ItemCatalog...");

        // 加载 ItemCatalog.asset
        var itemCatalog = Resources.Load<ItemCatalog>("Config/ItemCatalog");
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
        _gameplayConfig = Resources.Load<GameplayConfig>("Config/GameplayConfig");
        if (_gameplayConfig == null)
        {
            Debug.LogWarning("[GameBootstrap] GameplayConfig 未找到，Inventory 将使用默认配置", this);
        }

        return true;
    }

    /// <summary>
    /// Step 2: 初始化 PlayerInventory
    /// </summary>
    private bool InitializePlayerInventory()
    {
        Debug.Log("[GameBootstrap] [2/6] 初始化 PlayerInventory...");

        if (player.Inventory == null)
        {
            Debug.LogError("[GameBootstrap] PlayerContext.Inventory 为空，中止装配", this);
            return false;
        }

        // 初始化 PlayerInventory（必须先于任何拾取/装备/显示逻辑）
        player.Inventory.Initialize(_castleDbService, _gameplayConfig);
        Debug.Log("[GameBootstrap] PlayerInventory 初始化完成");

        return true;
    }

    // ===== Phase 7 & 8 预留方法 =====

    /// <summary>
    /// Step 3: 加载 HudBinding 并实例化/定位 HUD（Phase 7）
    /// 契约 [C-Runtime-0]:
    /// - 若 hudRootOverride 存在则复用
    /// - 否则从 HudBinding.hudPrefab 实例化
    /// - 验证 HUD 实例上的 HudRefs 组件
    /// </summary>
    private bool LoadHudBinding()
    {
        Debug.Log("[GameBootstrap] [3/6] 加载 HudBinding...");

        // 加载 HudBinding.asset
        var binding = Resources.Load<HudBindingAsset>("Config/HudBinding");
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
    /// Step 4: 初始化 HudPresenter（Phase 7）
    /// 契约 [C-Runtime-0]:
    /// - 从 HUD 实例获取 HudPresenter 和 HudRefs
    /// - 验证组件完整性
    /// - 调用 hudPresenter.Initialize(items, refs, inv, dmg)
    /// </summary>
    private bool InitializeHudPresenter()
    {
        Debug.Log("[GameBootstrap] [4/6] 初始化 HudPresenter...");

        if (_hudRoot == null)
        {
            Debug.LogError("[GameBootstrap] HUD 实例为空，无法初始化 HudPresenter", this);
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

        // 验证 PlayerContext 必需依赖
        if (player.Inventory == null)
        {
            Debug.LogError("[GameBootstrap] PlayerContext.Inventory 为空，无法初始化 HudPresenter", this);
            return false;
        }

        if (player.Damageable == null)
        {
            Debug.LogError("[GameBootstrap] PlayerContext.Damageable 为空，无法初始化 HudPresenter", this);
            return false;
        }

        // 初始化 HudPresenter
        hudPresenter.Initialize(_castleDbService, hudRefs, player.Inventory, player.Damageable);
        Debug.Log("[GameBootstrap] HudPresenter 初始化完成");

        // 缓存 HudPresenter（Phase 8 ReplaceController 需要）
        _hudPresenter = hudPresenter;

        return true;
    }

    /// <summary>
    /// Step 5: 初始化 ReplaceController（Phase 8）
    /// 契约 [C-Runtime-0]:
    /// - 从 PlayerContext 获取 ReplaceController
    /// - 验证组件完整性
    /// - 调用 replaceController.Initialize(items, refs, ctx, hud)
    /// </summary>
    private bool InitializeReplaceController()
    {
        Debug.Log("[GameBootstrap] [5/6] 初始化 ReplaceController...");

        // 验证 HudPresenter 已初始化（Phase 8 依赖）
        if (_hudPresenter == null)
        {
            Debug.LogError("[GameBootstrap] HudPresenter 未初始化，无法初始化 ReplaceController", this);
            return false;
        }

        // 获取 ReplaceController（可选依赖）
        if (player.ReplaceController == null)
        {
            Debug.LogWarning("[GameBootstrap] PlayerContext.ReplaceController 为空（可选依赖），跳过初始化");
            return true; // 不影响整体装配
        }

        // 获取 HudRefs（已在 Step 4 验证）
        var hudRefs = _hudRoot.GetComponentInChildren<HudRefs>(true);
        if (hudRefs == null)
        {
            Debug.LogError("[GameBootstrap] HUD 实例缺少 HudRefs 组件，无法初始化 ReplaceController", this);
            return false;
        }

        // 初始化 ReplaceController
        player.ReplaceController.Initialize(_castleDbService, hudRefs, player, _hudPresenter);
        Debug.Log("[GameBootstrap] ReplaceController 初始化完成");

        return true;
    }

    /// <summary>
    /// Step 6: 初始化 PlayerEquipmentController（Phase 6）
    /// </summary>
    private bool InitializePlayerEquipmentController()
    {
        Debug.Log("[GameBootstrap] [6/6] 初始化 PlayerEquipmentController...");

        // PlayerEquipmentController 可选依赖
        if (player.EquipmentController == null)
        {
            Debug.LogWarning("[GameBootstrap] PlayerContext.EquipmentController 为空（可选依赖），跳过初始化");
            return true; // 不影响整体装配
        }

        // 验证 AbilitySystem 已初始化（必需依赖）
        if (player.AbilitySystem == null)
        {
            Debug.LogError("[GameBootstrap] PlayerContext.AbilitySystem 为空，无法初始化 EquipmentController", this);
            return false;
        }

        // 初始化 EquipmentController
        player.EquipmentController.Initialize(_castleDbService, player.AbilitySystem, player.Inventory);
        Debug.Log("[GameBootstrap] PlayerEquipmentController 初始化完成");

        return true;
    }
}
