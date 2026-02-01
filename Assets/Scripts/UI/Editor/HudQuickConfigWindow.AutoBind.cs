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
    /// 自动绑定：自动绑定 HudRefs 字段（按固定路径）
    /// </summary>
    private void AutoBind()
    {
        Debug.Log($"{LOG_PREFIX} 开始 AutoBind...");
        // 契约 [C-Tool-1]：自动绑定必须保证 binding.hudPrefab != null
        HudBindingAsset binding = AssetDatabase.LoadAssetAtPath<HudBindingAsset>(BINDING_PATH);
        if (binding == null || binding.hudPrefab == null)
        {
            Debug.LogError($"{LOG_PREFIX} HudBinding.asset 或 hudPrefab 为空，请先 Create Template 或手动指定 hudPrefab");
            EditorUtility.DisplayDialog("失败", "HudBinding.asset 或 hudPrefab 为空\n\n请先执行 Create Template 或手动指定 hudPrefab", "确定");
            return;
        }

        // 必须使用 PrefabContents 工作流修改 Prefab（避免直接改 Prefab Asset 层级导致报错/潜在损坏）
        string prefabPath = AssetDatabase.GetAssetPath(binding.hudPrefab);
        if (string.IsNullOrEmpty(prefabPath))
        {
            Debug.LogError($"{LOG_PREFIX} hudPrefab 不是有效的 Prefab Asset（无法获取路径）");
            EditorUtility.DisplayDialog("失败", "hudPrefab 不是有效的 Prefab Asset（无法获取路径）", "确定");
            return;
        }

        GameObject prefabRoot = null;
        // 按固定路径绑定（符合 [C-UI-1]）
        try
        {
            prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);

            HudRefs hudRefs = prefabRoot.GetComponent<HudRefs>();
            if (hudRefs == null)
            {
                Debug.LogError($"{LOG_PREFIX} HUD Prefab 缺少 HudRefs 组件");
                EditorUtility.DisplayDialog("失败", "HUD Prefab 缺少 HudRefs 组件", "确定");
                return;
            }

            hudRefs.abilitySlotIcons = new Image[4];
            for (int i = 0; i < 4; i++)
            {
                string path = $"BottomLeft/AbilityBar/Slot_{i}/Icon";
                Transform t = prefabRoot.transform.Find(path);
                if (t == null)
                {
                    Debug.LogError($"{LOG_PREFIX} Missing node: {path}");
                    throw new System.Exception($"缺少节点: {path}");
                }
                hudRefs.abilitySlotIcons[i] = t.GetComponent<Image>();
                if (hudRefs.abilitySlotIcons[i] == null)
                {
                    Debug.LogError($"{LOG_PREFIX} Expected Image at {path}, got null");
                    throw new System.Exception($"节点类型错误: {path} 应为 Image");
                }
            }
            // 确保 KeyIcon 节点存在，并绑定引用（避免旧模板缺失导致报错）
            hudRefs.abilitySlotKeyIcons = EnsureAbilitySlotKeyIcons(prefabRoot);
            hudRefs.relicIcon = EnsureRelicIcon(prefabRoot);
            EnsureAbilitySlotCooldownUi(prefabRoot, out hudRefs.abilitySlotCooldownFills, out hudRefs.abilitySlotCooldownTexts);
            hudRefs.debugOverlayText = EnsureDebugOverlayText(prefabRoot);
            hudRefs.statusText = EnsureStatusText(prefabRoot);


            hudRefs.potionCountText = FindAndGetComponent<TMP_Text>(prefabRoot.transform, "BottomLeft/PotionWidget/CountText");
            hudRefs.healthFill = FindAndGetComponent<Image>(prefabRoot.transform, "BottomCenter/HealthBar/Fill");
            hudRefs.energyFill = FindAndGetComponent<Image>(prefabRoot.transform, "BottomCenter/EnergyBar/Fill");
            hudRefs.abilityReplacePanelRoot = FindAndGetComponent<Transform>(prefabRoot.transform, "Overlay/AbilityReplacePanel").gameObject;

            PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
            AssetDatabase.SaveAssets();

            Debug.Log($"{LOG_PREFIX} AutoBind 完成！");
            EditorUtility.DisplayDialog("成功", "AutoBind 完成！所有引用已绑定。", "确定");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"{LOG_PREFIX} AutoBind 失败: {ex.Message}");
            EditorUtility.DisplayDialog("失败", "AutoBind 失败，详见 Console", "确定");
        }
        finally
        {
            if (prefabRoot != null)
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }
    }

}
