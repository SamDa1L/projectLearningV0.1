using UnityEngine;
using CastleDB.Runtime;

/// <summary>
/// 基于 Resources.Load 的 IGameAssetProvider 实现。
/// - 相同资源多次访问时做一次性缓存（避免到处散落 Resources.Load）
/// - GameBootstrap 应持有单一实例并注入到需要的运行时模块
/// </summary>
public sealed class ResourcesGameAssetProvider : IGameAssetProvider
{
    /// <summary>
    /// 兜底共享实例：用于“无法被注入”的调用方（例如独立 Prefab / 编辑模式测试）。
    /// 建议尽量少用；优先使用由 GameBootstrap 创建并注入的实例（避免静态依赖扩散）。
    /// </summary>
    public static readonly ResourcesGameAssetProvider Shared = new ResourcesGameAssetProvider();

    // 必需资源
    private ItemCatalog _itemCatalog;
    private bool _itemCatalogLoaded;

    private AbilityCatalog _abilityCatalog;
    private bool _abilityCatalogLoaded;

    private HudBindingAsset _hudBinding;
    private bool _hudBindingLoaded;

    // 可选资源
    private GameplayConfig _gameplayConfig;
    private bool _gameplayConfigLoaded;

    private RelicCatalog _relicCatalog;
    private bool _relicCatalogLoaded;

    private InputIconCatalog _inputIconCatalog;
    private bool _inputIconCatalogLoaded;

    private StatusCatalog _statusCatalog;
    private bool _statusCatalogLoaded;

    public ItemCatalog ItemCatalog => LoadCached("Config/ItemCatalog", ref _itemCatalog, ref _itemCatalogLoaded);
    public AbilityCatalog AbilityCatalog => LoadCached("Config/AbilityCatalog", ref _abilityCatalog, ref _abilityCatalogLoaded);
    public HudBindingAsset HudBinding => LoadCached("Config/HudBinding", ref _hudBinding, ref _hudBindingLoaded);

    public GameplayConfig GameplayConfig => LoadCached("Config/GameplayConfig", ref _gameplayConfig, ref _gameplayConfigLoaded);
    public RelicCatalog RelicCatalog => LoadCached("Config/RelicCatalog", ref _relicCatalog, ref _relicCatalogLoaded);
    public InputIconCatalog InputIconCatalog => LoadCached("Config/InputIconCatalog", ref _inputIconCatalog, ref _inputIconCatalogLoaded);
    public StatusCatalog StatusCatalog => LoadCached("Config/StatusCatalog", ref _statusCatalog, ref _statusCatalogLoaded);

    public T Load<T>(string resourcesPath) where T : Object
    {
        if (string.IsNullOrWhiteSpace(resourcesPath))
        {
            return null;
        }

        return Resources.Load<T>(resourcesPath);
    }

    private static T LoadCached<T>(string resourcesPath, ref T cache, ref bool loaded) where T : Object
    {
        if (loaded)
        {
            return cache;
        }

        loaded = true;
        cache = Resources.Load<T>(resourcesPath);
        return cache;
    }
}
