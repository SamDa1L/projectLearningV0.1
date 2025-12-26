using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// HUD 实例引用组件（Phase 7）
/// 契约 [C-UI-1]: 挂在 HUDCanvas 根或 HudPresenter 同级，持有所有必需 UI 节点引用
///
/// 挂载位置：HUDCanvas GameObject（prefab 根节点）
///
/// 字段规则（硬契约）：
/// - abilitySlotIcons[4]: Ability 槽位图标（索引 0~3 映射到 Slot_0~3）
/// - potionCountText: 血瓶计数文本
/// - healthFill: 血条 fillAmount Image
/// - energyFill: 能量条 fillAmount Image（可选，0.4 版本隐藏）
/// - abilityReplacePanelRoot: 替换面板根节点（必需，默认 inactive）
///
/// 索引-路径映射（硬契约）：
/// - abilitySlotIcons[0] → BottomLeft/AbilityBar/Slot_0/Icon
/// - abilitySlotIcons[1] → BottomLeft/AbilityBar/Slot_1/Icon
/// - abilitySlotIcons[2] → BottomLeft/AbilityBar/Slot_2/Icon
/// - abilitySlotIcons[3] → BottomLeft/AbilityBar/Slot_3/Icon
///
/// 绑定方式：
/// - 由 HUD Quick Config 工具（Phase 10）自动绑定（AutoBind 功能）
/// - 禁止手动拖拽引用（工具会覆盖）
///
/// Runtime 使用：
/// - GameBootstrap 加载 HUD 实例后，通过 GetComponent<HudRefs> 获取此组件
/// - 注入到 HudPresenter.Initialize(items, refs, inv, dmg)
/// - HudPresenter 通过 refs 访问所有 UI 节点
/// </summary>
public class HudRefs : MonoBehaviour
{
    [Header("Ability Slots")]
    [Tooltip("能力槽图标数组（长度必须为 4，索引 0~3）")]
    public Image[] abilitySlotIcons = new Image[4];

    [Header("Potion Widget")]
    [Tooltip("血瓶计数文本（TMPro）")]
    public TMP_Text potionCountText;

    [Header("Health Bar")]
    [Tooltip("血条填充 Image（使用 fillAmount）")]
    public Image healthFill;

    [Header("Energy Bar (Optional)")]
    [Tooltip("能量条填充 Image（可选，0.4 版本隐藏）")]
    public Image energyFill;

    [Header("Replace Panel")]
    [Tooltip("替换面板根节点（必需，默认 inactive）")]
    public GameObject abilityReplacePanelRoot;

    /// <summary>
    /// OnValidate: 校验引用完整性（Editor-only）
    /// - abilitySlotIcons 长度必须为 4
    /// - 必需字段不为空（potionCountText, healthFill, abilityReplacePanelRoot）
    /// </summary>
    private void OnValidate()
    {
        #if UNITY_EDITOR
        // 校验 abilitySlotIcons 长度
        if (abilitySlotIcons == null || abilitySlotIcons.Length != 4)
        {
            Debug.LogError("[HudRefs] abilitySlotIcons 长度必须为 4", this);
        }

        // 校验必需字段
        if (potionCountText == null)
        {
            Debug.LogWarning("[HudRefs] potionCountText 未设置", this);
        }

        if (healthFill == null)
        {
            Debug.LogWarning("[HudRefs] healthFill 未设置", this);
        }

        if (abilityReplacePanelRoot == null)
        {
            Debug.LogWarning("[HudRefs] abilityReplacePanelRoot 未设置", this);
        }
        #endif
    }
}
