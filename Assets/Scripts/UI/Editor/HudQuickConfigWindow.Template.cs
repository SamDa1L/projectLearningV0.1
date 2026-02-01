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
    /// 创建模板：生成 HUD 模板 Prefab 并更新 HudBinding.asset
    /// 契约 [C-Tool-1]: 符合 [C-UI-1]/[C-UI-2] 节点结构
    /// </summary>
    private void CreateTemplate()
    {
        Debug.Log($"{LOG_PREFIX} 开始 Create Template...");

        TemplateOption option = GetSelectedTemplate();
        string outputPath = GetOutputPrefabPath(option);
        if (option.kind != TemplateKind.CodeDefault)
        {
            try
            {
                GameObject prefabAsset = CreateTemplateFromPrefab(option, outputPath);
                HudBindingAsset binding = EnsureBindingAsset();
                binding.hudPrefab = prefabAsset;
                EditorUtility.SetDirty(binding);
                AssetDatabase.SaveAssets();

                if (_autoPruneGeneratedTemplates)
                {
                    PruneGeneratedTemplates(_generatedTemplateKeepCount, false);
                }

                Debug.Log($"{LOG_PREFIX} Create Template 完成: {outputPath}");
                EditorUtility.DisplayDialog("成功", "HUD 模板创建完成！\n\n产物：\n- " + outputPath + "\n- " + BINDING_PATH, "确定");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"{LOG_PREFIX} Create Template 失败: {ex.Message}\n{ex.StackTrace}");
                EditorUtility.DisplayDialog("失败", "Create Template 失败，详见 Console", "确定");
            }
            return;
        }

        try
        {
            // 确保目录存在
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            Directory.CreateDirectory(Path.GetDirectoryName(BINDING_PATH));

            // 创建 HUD Canvas 根节点
            GameObject hudRoot = new GameObject("HUDCanvas");
            Canvas canvas = hudRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = hudRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            hudRoot.AddComponent<GraphicRaycaster>();

            // 添加 HudRefs 组件
            HudRefs hudRefs = hudRoot.AddComponent<HudRefs>();

            // 创建节点结构（符合 [C-UI-1]/[C-UI-2]）
            GameObject topLeft = CreateChild(hudRoot, "TopLeft");
            GameObject bottomLeft = CreateChild(hudRoot, "BottomLeft");
            GameObject bottomCenter = CreateChild(hudRoot, "BottomCenter");
            GameObject overlay = CreateChild(hudRoot, "Overlay");

            RectTransform topLeftRt = topLeft.GetComponent<RectTransform>();
            topLeftRt.anchorMin = new Vector2(0, 1);
            topLeftRt.anchorMax = new Vector2(0, 1);
            topLeftRt.pivot = new Vector2(0, 1);
            topLeftRt.anchoredPosition = Vector2.zero;
            topLeftRt.sizeDelta = Vector2.zero;

            // 创建 RelicWidget（Phase 7：拾取遗物后显示在左上角）
            GameObject relicWidget = CreateChild(topLeft, "RelicWidget");
            SetRectTransform(relicWidget, new Vector2(30, -30), new Vector2(0, 1), new Vector2(80, 80));
            RectTransform relicWidgetRt = relicWidget.GetComponent<RectTransform>();
            relicWidgetRt.pivot = new Vector2(0, 1);

            GameObject relicIconObj = CreateChild(relicWidget, "Icon");
            Image relicIcon = relicIconObj.AddComponent<Image>();
            relicIcon.enabled = false; // 默认隐藏
            SetRectTransform(relicIconObj, Vector2.zero, new Vector2(0.5f, 0.5f), new Vector2(64, 64));

            // 创建 AbilityBar（4 槽）
            GameObject abilityBar = CreateChild(bottomLeft, "AbilityBar");
            SetRectTransform(abilityBar, new Vector2(10, 10), new Vector2(0, 0), new Vector2(0, 0));

            Image[] slotIcons = new Image[4];
            Image[] slotKeyIcons = new Image[4];
            Image[] slotCooldownFills = new Image[4];
            TMP_Text[] slotCooldownTexts = new TMP_Text[4];
            // 根据报告，每个槽位的 Icon/background AnchoredPosition
            Vector2[] iconPositions = new Vector2[]
            {
                new Vector2(1155.0f, 116.0f), // Slot_0
                new Vector2(1241.0f, 116.0f), // Slot_1
                new Vector2(1330.0f, 116.0f), // Slot_2
                new Vector2(1419.0f, 116.0f)  // Slot_3
            };
            Vector2 keyIconOffset = new Vector2(0f, -60f);

            for (int i = 0; i < 4; i++)
            {
                GameObject slot = CreateChild(abilityBar, $"Slot_{i}");
                SetRectTransform(slot, new Vector2(i * 70, 0), new Vector2(0, 0), new Vector2(60, 60));
                slot.transform.localScale = Vector3.one;

                // 创建 background（固定底板，小写命名匹配现有数据）
                GameObject backgroundObj = CreateChild(slot, "background");
                Image backgroundImg = backgroundObj.AddComponent<Image>();
                backgroundImg.color = new Color(0.3f, 0.3f, 0.3f, 0.8f); // 深灰色底板
                SetRectTransform(backgroundObj, iconPositions[i], new Vector2(0.5f, 0.5f), new Vector2(70, 70));
                backgroundObj.transform.localScale = new Vector3(2.0f, 2.0f, 2.0f);

                // 创建 Icon（运行时替换）
                GameObject iconObj = CreateChild(slot, "Icon");
                slotIcons[i] = iconObj.AddComponent<Image>();
                slotIcons[i].enabled = false; // 默认禁用（空槽）
                SetRectTransform(iconObj, iconPositions[i], new Vector2(0.5f, 0.5f), new Vector2(50, 50));
                iconObj.transform.localScale = new Vector3(2.0f, 2.0f, 2.0f);

                GameObject keyIconObj = CreateChild(slot, "KeyIcon");
                slotKeyIcons[i] = keyIconObj.AddComponent<Image>();
                slotKeyIcons[i].enabled = false;
                SetRectTransform(keyIconObj, iconPositions[i] + keyIconOffset, new Vector2(0.5f, 0.5f), new Vector2(30, 30));
                keyIconObj.transform.localScale = new Vector3(2.0f, 2.0f, 2.0f);

                // 冷却层（Phase 8）：遮罩 + 倒计时文本
                GameObject cooldownMaskObj = CreateChild(slot, "CooldownMask");
                slotCooldownFills[i] = cooldownMaskObj.AddComponent<Image>();
                slotCooldownFills[i].enabled = false;
                slotCooldownFills[i].color = new Color(0f, 0f, 0f, 0.65f);
                slotCooldownFills[i].type = Image.Type.Filled;
                slotCooldownFills[i].fillMethod = Image.FillMethod.Radial360;
                slotCooldownFills[i].fillAmount = 0f;
                slotCooldownFills[i].raycastTarget = false;
                SetRectTransform(cooldownMaskObj, iconPositions[i], new Vector2(0.5f, 0.5f), new Vector2(70, 70));
                cooldownMaskObj.transform.localScale = new Vector3(2.0f, 2.0f, 2.0f);

                GameObject cooldownTextObj = CreateChild(slot, "CooldownText");
                TextMeshProUGUI cooldownText = cooldownTextObj.AddComponent<TextMeshProUGUI>();
                cooldownText.text = "";
                cooldownText.fontSize = 22;
                cooldownText.alignment = TextAlignmentOptions.Center;
                cooldownText.enabled = false;
                cooldownText.raycastTarget = false;
                slotCooldownTexts[i] = cooldownText;
                SetRectTransform(cooldownTextObj, iconPositions[i], new Vector2(0.5f, 0.5f), new Vector2(70, 70));
                cooldownTextObj.transform.localScale = new Vector3(2.0f, 2.0f, 2.0f);
            }

            // 创建 PotionWidget
            GameObject potionWidget = CreateChild(bottomLeft, "PotionWidget");
            SetRectTransform(potionWidget, new Vector2(191, 185), new Vector2(0, 0), new Vector2(80, 60));

            GameObject countTextObj = CreateChild(potionWidget, "CountText");
            TMP_Text potionCountText = countTextObj.AddComponent<TextMeshProUGUI>();
            potionCountText.text = "0";
            potionCountText.fontSize = 24;
            potionCountText.alignment = TextAlignmentOptions.Center;
            SetRectTransform(countTextObj, Vector2.zero, new Vector2(0.5f, 0.5f), new Vector2(80, 60));

            // 创建 HealthBar
            GameObject healthBar = CreateChild(bottomCenter, "HealthBar");
            RectTransform healthBarRt = healthBar.GetComponent<RectTransform>();
            healthBarRt.anchorMin = new Vector2(0, 0);
            healthBarRt.anchorMax = new Vector2(0, 0);
            healthBarRt.pivot = new Vector2(0, 0); // 根据报告设置为 (0, 0)
            healthBarRt.anchoredPosition = new Vector2(55, 45);
            healthBarRt.sizeDelta = new Vector2(200, 30);
            healthBar.transform.localScale = new Vector3(5.0f, 5.0f, 5.0f);

            GameObject healthBg = CreateChild(healthBar, "Background");
            Image healthBgImg = healthBg.AddComponent<Image>();
            healthBgImg.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);
            SetRectTransform(healthBg, Vector2.zero, new Vector2(0.5f, 0.5f), new Vector2(200, 30));

            GameObject healthFillObj = CreateChild(healthBar, "Fill");
            Image healthFill = healthFillObj.AddComponent<Image>();
            healthFill.color = Color.red;
            healthFill.type = Image.Type.Filled;
            healthFill.fillMethod = Image.FillMethod.Horizontal;
            healthFill.fillAmount = 1f;
            SetRectTransform(healthFillObj, new Vector2(99.7f, 0), new Vector2(0, 0.5f), new Vector2(200, 30));

            // 创建 EnergyBar（默认隐藏）
            GameObject energyBar = CreateChild(bottomCenter, "EnergyBar");
            energyBar.SetActive(false); // 0.4 版本隐藏
            SetRectTransform(energyBar, new Vector2(0, 45), new Vector2(0.5f, 0), new Vector2(200, 20));

            GameObject energyBg = CreateChild(energyBar, "Background");
            Image energyBgImg = energyBg.AddComponent<Image>();
            energyBgImg.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);
            SetRectTransform(energyBg, Vector2.zero, new Vector2(0.5f, 0.5f), new Vector2(200, 20));

            GameObject energyFillObj = CreateChild(energyBar, "Fill");
            Image energyFill = energyFillObj.AddComponent<Image>();
            energyFill.color = Color.blue;
            energyFill.type = Image.Type.Filled;
            energyFill.fillMethod = Image.FillMethod.Horizontal;
            energyFill.fillAmount = 1f;
            SetRectTransform(energyFillObj, Vector2.zero, new Vector2(0, 0.5f), new Vector2(200, 20));

            // 创建 AbilityReplacePanel（Phase 8，默认隐藏）
            GameObject replacePanel = CreateAbilityReplacePanel(overlay);

            // DebugOverlay（Phase 8，可选）
            GameObject debugOverlay = CreateChild(overlay, "DebugOverlay");
            TextMeshProUGUI debugOverlayText = debugOverlay.AddComponent<TextMeshProUGUI>();
            debugOverlayText.text = "";
            debugOverlayText.fontSize = 18;
            debugOverlayText.alignment = TextAlignmentOptions.TopLeft;
            debugOverlayText.enabled = false;
            debugOverlayText.raycastTarget = false;
            RectTransform debugRt = debugOverlay.GetComponent<RectTransform>();
            debugRt.anchorMin = new Vector2(0f, 1f);
            debugRt.anchorMax = new Vector2(0f, 1f);
            debugRt.pivot = new Vector2(0f, 1f);
            debugRt.anchoredPosition = new Vector2(10f, -10f);
            debugRt.sizeDelta = new Vector2(600f, 400f);

            // StatusText（Phase 8，可选）
            GameObject statusTextObj = CreateChild(overlay, "StatusText");
            TextMeshProUGUI statusText = statusTextObj.AddComponent<TextMeshProUGUI>();
            statusText.text = "";
            statusText.fontSize = 22;
            statusText.alignment = TextAlignmentOptions.Top;
            statusText.enabled = false;
            statusText.raycastTarget = false;
            RectTransform statusRt = statusTextObj.GetComponent<RectTransform>();
            statusRt.anchorMin = new Vector2(0.5f, 1f);
            statusRt.anchorMax = new Vector2(0.5f, 1f);
            statusRt.pivot = new Vector2(0.5f, 1f);
            statusRt.anchoredPosition = new Vector2(0f, -10f);
            statusRt.sizeDelta = new Vector2(500f, 60f);

            // 绑定 HudRefs 字段
            hudRefs.abilitySlotIcons = slotIcons;
            hudRefs.abilitySlotKeyIcons = slotKeyIcons;
            hudRefs.abilitySlotCooldownFills = slotCooldownFills;
            hudRefs.abilitySlotCooldownTexts = slotCooldownTexts;
            hudRefs.potionCountText = potionCountText;
            hudRefs.healthFill = healthFill;
            hudRefs.energyFill = energyFill;
            hudRefs.abilityReplacePanelRoot = replacePanel;
            hudRefs.relicIcon = relicIcon;
            hudRefs.debugOverlayText = debugOverlayText;
            hudRefs.statusText = statusText;

            // 保存 Prefab
            GameObject prefabAsset = PrefabUtility.SaveAsPrefabAsset(hudRoot, outputPath);
            DestroyImmediate(hudRoot);

            Debug.Log($"{LOG_PREFIX} HUD 模板创建成功: {outputPath}");

            // 创建/更新 HudBinding.asset
            HudBindingAsset binding = AssetDatabase.LoadAssetAtPath<HudBindingAsset>(BINDING_PATH);
            if (binding == null)
            {
                binding = CreateInstance<HudBindingAsset>();
                AssetDatabase.CreateAsset(binding, BINDING_PATH);
                Debug.Log($"{LOG_PREFIX} HudBinding.asset 创建成功: {BINDING_PATH}");
            }

            binding.hudPrefab = prefabAsset;
            EditorUtility.SetDirty(binding);
            AssetDatabase.SaveAssets();

            if (_autoPruneGeneratedTemplates)
            {
                PruneGeneratedTemplates(_generatedTemplateKeepCount, false);
            }

            Debug.Log($"{LOG_PREFIX} Create Template 完成: {outputPath}");
            EditorUtility.DisplayDialog("成功", "HUD 模板创建完成！\n\n产物：\n- " + outputPath + "\n- " + BINDING_PATH, "确定");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"{LOG_PREFIX} Create Template 失败: {ex.Message}\n{ex.StackTrace}");
            EditorUtility.DisplayDialog("失败", "Create Template 失败，详见 Console", "确定");
        }
    }

    /// <summary>
    /// 创建 AbilityReplacePanel 节点结构（符合 [C-UI-2]）
    /// </summary>
    private GameObject CreateAbilityReplacePanel(GameObject parent)
    {
        GameObject panel = CreateChild(parent, "AbilityReplacePanel");
        panel.SetActive(false); // 默认隐藏
        SetRectTransform(panel, Vector2.zero, new Vector2(0.5f, 0.5f), new Vector2(600, 400));

        // 添加背景
        Image panelBg = panel.AddComponent<Image>();
        panelBg.color = new Color(0, 0, 0, 0.8f);

        // Pending Item 显示区域
        GameObject pending = CreateChild(panel, "Pending");
        SetRectTransform(pending, new Vector2(0, 100), new Vector2(0.5f, 0.5f), new Vector2(150, 150));

        GameObject pendingIcon = CreateChild(pending, "Icon");
        Image pendingIconImg = pendingIcon.AddComponent<Image>();
        pendingIconImg.enabled = false;
        SetRectTransform(pendingIcon, new Vector2(0, 20), new Vector2(0.5f, 0.5f), new Vector2(100, 100));

        GameObject pendingName = CreateChild(pending, "NameText");
        TMP_Text pendingNameText = pendingName.AddComponent<TextMeshProUGUI>();
        pendingNameText.text = "";
        pendingNameText.fontSize = 18;
        pendingNameText.alignment = TextAlignmentOptions.Center;
        SetRectTransform(pendingName, new Vector2(0, -50), new Vector2(0.5f, 0.5f), new Vector2(150, 30));

        // Slots 容器
        GameObject slots = CreateChild(panel, "Slots");
        SetRectTransform(slots, new Vector2(0, -80), new Vector2(0.5f, 0.5f), new Vector2(500, 120));

        // 创建 4 个槽位（符合 [C-UI-2]）
        for (int i = 0; i < 4; i++)
        {
            GameObject slot = CreateChild(slots, $"Slot_{i}");
            SetRectTransform(slot, new Vector2(-180 + i * 120, 0), new Vector2(0.5f, 0.5f), new Vector2(100, 100));

            // 创建 Background（固定底板）
            GameObject slotBg = CreateChild(slot, "Background");
            Image slotBgImg = slotBg.AddComponent<Image>();
            slotBgImg.color = new Color(0.3f, 0.3f, 0.3f, 0.8f); // 深灰色底板
            SetRectTransform(slotBg, Vector2.zero, new Vector2(0.5f, 0.5f), new Vector2(100, 100));

            // 创建 Icon（运行时替换）
            GameObject slotIcon = CreateChild(slot, "Icon");
            Image slotIconImg = slotIcon.AddComponent<Image>();
            slotIconImg.enabled = false;
            SetRectTransform(slotIcon, Vector2.zero, new Vector2(0.5f, 0.5f), new Vector2(80, 80));

            GameObject highlight = CreateChild(slot, "Highlight");
            highlight.SetActive(false); // 默认隐藏
            Image highlightImg = highlight.AddComponent<Image>();
            highlightImg.color = new Color(1, 1, 0, 0.5f);
            SetRectTransform(highlight, Vector2.zero, new Vector2(0.5f, 0.5f), new Vector2(100, 100));
        }

        // 可选：提示文本
        GameObject hintText = CreateChild(panel, "HintText");
        TMP_Text hintTextComp = hintText.AddComponent<TextMeshProUGUI>();
        hintTextComp.text = "按 1~4 选择槽位，ESC 取消";
        hintTextComp.fontSize = 16;
        hintTextComp.alignment = TextAlignmentOptions.Center;
        SetRectTransform(hintText, new Vector2(0, -160), new Vector2(0.5f, 0.5f), new Vector2(500, 30));

        return panel;
    }

}
