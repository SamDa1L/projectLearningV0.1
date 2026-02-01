using UnityEngine;
using CastleDB.Runtime;

/// <summary>
/// 运行时资源访问抽象（0.5 阶段 / P1-1）
/// - 统一收口 Resources.Load 的调用点（尽量只允许“资源提供器”内部调用）
/// - 由 GameBootstrap 创建并通过依赖注入下发
/// </summary>
public interface IGameAssetProvider
{
    ItemCatalog ItemCatalog { get; }
    AbilityCatalog AbilityCatalog { get; }
    HudBindingAsset HudBinding { get; }

    // 可选资源（功能关闭或资源缺失时可能为 null）
    GameplayConfig GameplayConfig { get; }
    RelicCatalog RelicCatalog { get; }
    InputIconCatalog InputIconCatalog { get; }
    StatusCatalog StatusCatalog { get; }

    /// <summary>
    /// 通用 Resources.Load 包装。核心资产优先使用上方的强类型属性（便于统一校验与注入）。
    /// </summary>
    T Load<T>(string resourcesPath) where T : Object;
}
