using UnityEngine;

/// <summary>
/// HUD 资源入口 ScriptableObject（Phase 7）
/// 契约 [C-UI-1]: 提供 HUDCanvas prefab 引用，作为 Runtime 加载的唯一入口
///
/// 资源路径（硬契约）：Assets/Resources/Config/HudBinding.asset
///
/// 字段规则：
/// - hudPrefab: 必需，指向 HUDCanvas.prefab
/// - 可选纯配置字段（不得包含 Image/TMP_Text/GameObject 等实例引用）
///
/// Runtime 使用规则：
/// - GameBootstrap 通过资源提供器加载此资产
/// - 通过 hudPrefab 实例化或复用 HUD（取决于 hudRootOverride）
/// - HudPresenter/ReplaceController 只能使用 HUD 实例上的 HudRefs 引用，禁止从此资产读取组件引用
///
/// 生成方式：
/// - 由 HUD Quick Config 工具（Phase 10）生成/覆盖
/// - Runtime 禁止手改，仅允许工具覆盖生成
/// </summary>
[CreateAssetMenu(fileName = "HudBinding", menuName = "Config/HudBindingAsset", order = 0)]
public class HudBindingAsset : ScriptableObject
{
    /// <summary>
    /// HUDCanvas prefab 引用（必需）
    /// 路径（硬契约）：Assets/Resources/Prefabs/UI/HUDCanvas.prefab
    /// 由 HUD Quick Config 工具生成并回填
    /// </summary>
    [Header("HUD Prefab Reference")]
    [Tooltip("HUDCanvas prefab，由 HUD Quick Config 工具生成/覆盖")]
    public GameObject hudPrefab;

    // Phase 10: 可选添加纯配置字段（如是否启用能量条等），但不得包含实例引用
}
