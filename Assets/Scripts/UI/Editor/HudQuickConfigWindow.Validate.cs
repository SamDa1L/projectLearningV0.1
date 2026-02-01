using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;
using System;
using System.IO;
using System.Text;

public partial class HudQuickConfigWindow : EditorWindow
{
    /// <summary>
    /// 校验：校验节点完整性（符合 [C-UI-1]/[C-UI-2]）
    /// </summary>
    private void Validate()
    {
        Debug.Log($"{LOG_PREFIX} 开始 Validate...");

        HudBindingAsset binding = AssetDatabase.LoadAssetAtPath<HudBindingAsset>(BINDING_PATH);
        if (binding == null || binding.hudPrefab == null)
        {
            Debug.LogError($"{LOG_PREFIX} HudBinding.asset 或 hudPrefab 为空，请先 Create Template");
            EditorUtility.DisplayDialog("失败", "HudBinding.asset 或 hudPrefab 为空\n\n请先执行 Create Template 或手动指定 hudPrefab", "确定");
            return;
        }

        GameObject prefab = binding.hudPrefab;
        HudRefs hudRefs = prefab.GetComponent<HudRefs>();
        if (hudRefs == null)
        {
            Debug.LogError($"{LOG_PREFIX} HUD Prefab 缺少 HudRefs 组件");
            EditorUtility.DisplayDialog("失败", "HUD Prefab 缺少 HudRefs 组件", "确定");
            return;
        }

        bool hasError = false;

        // 校验 abilitySlotIcons 数量
        if (hudRefs.abilitySlotIcons == null || hudRefs.abilitySlotIcons.Length != 4)
        {
            Debug.LogError($"{LOG_PREFIX}[ERROR] Ability slot icon count must be 4, got {hudRefs.abilitySlotIcons?.Length ?? 0}");
            hasError = true;
        }

        if (hudRefs.abilitySlotKeyIcons == null || hudRefs.abilitySlotKeyIcons.Length != 4)
        {
            Debug.LogError($"{LOG_PREFIX}[ERROR] Ability slot key icon count must be 4, got {hudRefs.abilitySlotKeyIcons?.Length ?? 0}");
            hasError = true;
        }
        else
        {
            for (int i = 0; i < hudRefs.abilitySlotKeyIcons.Length; i++)
            {
                if (hudRefs.abilitySlotKeyIcons[i] == null)
                {
                    Debug.LogError($"{LOG_PREFIX}[ERROR] Missing node: BottomLeft/AbilityBar/Slot_{i}/KeyIcon");
                    hasError = true;
                }
            }
        }

        // 校验必需字段
        if (hudRefs.potionCountText == null)
        {
            Debug.LogError($"{LOG_PREFIX}[ERROR] Missing node: BottomLeft/PotionWidget/CountText");
            hasError = true;
        }

        if (hudRefs.healthFill == null)
        {
            Debug.LogError($"{LOG_PREFIX}[ERROR] Missing node: BottomCenter/HealthBar/Fill");
            hasError = true;
        }

        if (hudRefs.abilityReplacePanelRoot == null)
        {
            Debug.LogError($"{LOG_PREFIX}[ERROR] Missing ReplacePanelRoot: Overlay/AbilityReplacePanel");
            hasError = true;
        }
        else
        {
            // 校验 [C-UI-2] 节点
            ValidateReplacePanelNode<Image>(hudRefs.abilityReplacePanelRoot.transform, "Pending/Icon", ref hasError);
            ValidateReplacePanelNode<TMP_Text>(hudRefs.abilityReplacePanelRoot.transform, "Pending/NameText", ref hasError);

            for (int i = 0; i < 4; i++)
            {
                ValidateReplacePanelNode<Image>(hudRefs.abilityReplacePanelRoot.transform, $"Slots/Slot_{i}/Icon", ref hasError);
                ValidateReplacePanelNode<Transform>(hudRefs.abilityReplacePanelRoot.transform, $"Slots/Slot_{i}/Highlight", ref hasError);
            }
        }

        if (hudRefs.relicIcon == null)
        {
            Debug.LogError($"{LOG_PREFIX}[ERROR] Missing node: TopLeft/RelicWidget/Icon");
            hasError = true;
        }

        if (!hasError)
        {
            Debug.Log($"{LOG_PREFIX} Validate 通过！所有节点完整。");
            EditorUtility.DisplayDialog("成功", "Validate 通过！HUD 结构符合契约要求。", "确定");
        }
        else
        {
            Debug.LogError($"{LOG_PREFIX} Validate 失败，详见 Console");
            EditorUtility.DisplayDialog("失败", "Validate 失败，发现缺失节点或类型错误，详见 Console", "确定");
        }
    }
}
