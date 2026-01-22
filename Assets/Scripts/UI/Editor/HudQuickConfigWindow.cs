using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;
using System;
using System.IO;
using System.Text;

/// <summary>
/// HUD Quick Config 工具（Phase 10）
/// 契约 [C-Tool-1]：创建模板 / 自动绑定 / 校验
///
/// 功能：
/// - 创建模板：生成 HUD 模板 Prefab（非 Resources：Assets/_Generated/HUDTemplates）并更新 HudBinding.asset
/// - 自动绑定：自动绑定 HudRefs 字段（按固定路径）
/// - 校验：校验节点完整性（符合 [C-UI-1]/[C-UI-2]）
///
/// 菜单路径：Tools/UI/HUD Quick Config
/// </summary>
public class HudQuickConfigWindow : EditorWindow
{
    // 资源路径（硬契约）
    private const string PREFAB_PATH = "Assets/Resources/Prefabs/UI/HUDCanvas.prefab";
    private const string BINDING_PATH = "Assets/Resources/Config/HudBinding.asset";
    private const string TEMPLATE_DIR = "Assets/Editor/HUDTemplates";
    private const string CURRENT_TEMPLATE_PATH = "Assets/Editor/HUDTemplates/HUDCanvas_Snapshot.prefab";
    private const string CURRENT_TEMPLATE_NAME = "当前HUDCanvas";
    // Templates are editor-generated snapshots; keep them out of Resources to avoid bloating builds.
    private const string TEMPLATE_OUTPUT_DIR = "Assets/_Generated/HUDTemplates";

    // 日志前缀
    private const string LOG_PREFIX = "[HUDQuickConfig]";

    private enum TemplateKind
    {
        PrefabAsset,
        CodeDefault
    }

    private struct TemplateOption
    {
        public TemplateKind kind;
        public string displayName;
        public string prefabPath;
    }

    private readonly System.Collections.Generic.List<TemplateOption> templateOptions = new System.Collections.Generic.List<TemplateOption>();
    private int selectedTemplateIndex = 0;

    [MenuItem("Tools/UI/HUD Quick Config")]
    public static void ShowWindow()
    {
        var window = GetWindow<HudQuickConfigWindow>("HUD Quick Config");
        window.minSize = new Vector2(400, 300);
    }

    private void OnEnable()
    {
        RefreshTemplateOptions();
    }

    private void OnGUI()
    {
        GUILayout.Label("HUD Quick Config (Phase 10)", EditorStyles.boldLabel);
        GUILayout.Space(10);

        EditorGUILayout.HelpBox(
            "此工具生成 HUD 模板与绑定资源：\n" +
            "- HUD 模板 Prefab (Assets/_Generated/HUDTemplates/)\n" +
            "- HudBinding.asset (Assets/Resources/Config/)\n\n" +
            "不会覆盖现有 HUDCanvas.prefab",
            MessageType.Info
        );

        GUILayout.Space(10);

        // 模板选择（Create Template 使用）
        EditorGUILayout.LabelField("模板选择", EditorStyles.boldLabel);
        selectedTemplateIndex = EditorGUILayout.Popup("Create Template 模板", selectedTemplateIndex, GetTemplateDisplayNames());

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("保存当前HUDCanvas为模板", GUILayout.Height(28)))
        {
            SaveCurrentTemplate();
        }

        if (GUILayout.Button("刷新模板列表", GUILayout.Height(28)))
        {
            RefreshTemplateOptions();
        }
        GUILayout.EndHorizontal();

        EditorGUILayout.HelpBox(
            "模板目录：\n" +
            "- " + TEMPLATE_DIR + "\n" +
            "当前HUDCanvas快照（保存后生成）：\n" +
            "- " + CURRENT_TEMPLATE_PATH + "\n",
            MessageType.None
        );

        GUILayout.Space(10);

        if (GUILayout.Button("Create Template", GUILayout.Height(40)))
        {
            CreateTemplate();
        }

        GUILayout.Space(5);

        // 自动绑定按钮
        if (GUILayout.Button("AutoBind", GUILayout.Height(40)))
        {
            AutoBind();
        }

        GUILayout.Space(5);

        // 校验按钮
        if (GUILayout.Button("Validate", GUILayout.Height(40)))
        {
            Validate();
        }

        GUILayout.Space(10);

        EditorGUILayout.HelpBox(
            "推荐流程：\n" +
            "1. Create Template - 生成 HUD 模板\n" +
            "2. (可选) 手动调整布局\n" +
            "3. AutoBind - 自动绑定引用\n" +
            "4. Validate - 校验完整性",
            MessageType.None
        );
    }

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

                // Cooldown overlay (Phase 8): mask + countdown text
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

            // DebugOverlay (Phase 8, optional)
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

            // StatusText (Phase 8, optional)
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
    // ===== 辅助方法 =====

    private Image[] EnsureAbilitySlotKeyIcons(GameObject hudRoot)
    {
        Image[] keyIcons = new Image[4];

            for (int i = 0; i < 4; i++)
            {
                string slotPath = $"BottomLeft/AbilityBar/Slot_{i}";
            Transform slot = hudRoot.transform.Find(slotPath);
            if (slot == null)
            {
                throw new System.Exception($"缺少节点: {slotPath}");
            }

            Transform keyIcon = slot.Find("KeyIcon");
            if (keyIcon == null)
            {
                GameObject keyIconObj = new GameObject("KeyIcon");
                keyIconObj.transform.SetParent(slot, false);

                RectTransform keyRt = keyIconObj.AddComponent<RectTransform>();
                RectTransform iconRt = slot.Find("Icon")?.GetComponent<RectTransform>();
                if (iconRt != null)
                {
                    keyRt.anchorMin = iconRt.anchorMin;
                    keyRt.anchorMax = iconRt.anchorMax;
                    keyRt.pivot = iconRt.pivot;
                    keyRt.sizeDelta = iconRt.sizeDelta * 0.6f;
                    keyRt.anchoredPosition = iconRt.anchoredPosition + new Vector2(0f, -Mathf.Max(30f, iconRt.sizeDelta.y + 10f));
                    keyIconObj.transform.localScale = iconRt.localScale;
                }
                else
                {
                    keyRt.anchorMin = new Vector2(0.5f, 0.5f);
                    keyRt.anchorMax = new Vector2(0.5f, 0.5f);
                    keyRt.pivot = new Vector2(0.5f, 0.5f);
                    keyRt.sizeDelta = new Vector2(30f, 30f);
                    keyRt.anchoredPosition = new Vector2(0f, -40f);
                }

                Image image = keyIconObj.AddComponent<Image>();
                image.enabled = false;
                keyIcons[i] = image;
            }
            else
            {
                Image image = keyIcon.GetComponent<Image>();
                if (image == null)
                {
                    image = keyIcon.gameObject.AddComponent<Image>();
                }
                keyIcons[i] = image;
            }
        }

        return keyIcons;
    }

    private Image EnsureRelicIcon(GameObject hudRoot)
    {
        if (hudRoot == null)
        {
            throw new System.Exception("hudRoot 为空");
        }

        Transform topLeft = hudRoot.transform.Find("TopLeft");
        if (topLeft == null)
        {
            GameObject topLeftObj = new GameObject("TopLeft");
            topLeftObj.transform.SetParent(hudRoot.transform, false);

            RectTransform rt = topLeftObj.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;

            topLeft = topLeftObj.transform;
        }
        else
        {
            RectTransform rt = topLeft.GetComponent<RectTransform>();
            if (rt == null)
            {
                rt = topLeft.gameObject.AddComponent<RectTransform>();
            }
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
        }

        Transform relicWidget = topLeft.Find("RelicWidget");
        if (relicWidget == null)
        {
            GameObject widgetObj = new GameObject("RelicWidget");
            widgetObj.transform.SetParent(topLeft, false);

            RectTransform rt = widgetObj.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(30f, -30f);
            rt.sizeDelta = new Vector2(80f, 80f);

            relicWidget = widgetObj.transform;
        }

        Transform relicIcon = relicWidget.Find("Icon");
        if (relicIcon == null)
        {
            GameObject iconObj = new GameObject("Icon");
            iconObj.transform.SetParent(relicWidget, false);

            RectTransform rt = iconObj.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(64f, 64f);

            relicIcon = iconObj.transform;
        }

        Image image = relicIcon.GetComponent<Image>();
        if (image == null)
        {
            image = relicIcon.gameObject.AddComponent<Image>();
        }

        // 默认隐藏，交由运行时按“是否装备遗物”控制显示
        image.enabled = false;
        return image;
    }


    // Phase 8: optional cooldown widgets (mask + countdown text) for each ability slot.
    private void EnsureAbilitySlotCooldownUi(GameObject hudRoot, out Image[] cooldownFills, out TMP_Text[] cooldownTexts)
    {
        cooldownFills = new Image[4];
        cooldownTexts = new TMP_Text[4];

        if (hudRoot == null)
        {
            throw new System.Exception("hudRoot is null");
        }

        for (int i = 0; i < 4; i++)
        {
            string slotPath = $"BottomLeft/AbilityBar/Slot_{i}";
            Transform slot = hudRoot.transform.Find(slotPath);
            if (slot == null)
            {
                throw new System.Exception($"Missing node: {slotPath}");
            }

            RectTransform iconRt = slot.Find("Icon")?.GetComponent<RectTransform>();

            // Cooldown mask
            Transform maskTf = slot.Find("CooldownMask");
            Image maskImg;
            if (maskTf == null)
            {
                GameObject maskObj = new GameObject("CooldownMask");
                maskObj.transform.SetParent(slot, false);

                RectTransform rt = maskObj.AddComponent<RectTransform>();
                if (iconRt != null)
                {
                    rt.anchorMin = iconRt.anchorMin;
                    rt.anchorMax = iconRt.anchorMax;
                    rt.pivot = iconRt.pivot;
                    rt.anchoredPosition = iconRt.anchoredPosition;
                    rt.sizeDelta = iconRt.sizeDelta;
                    maskObj.transform.localScale = iconRt.localScale;
                }
                else
                {
                    rt.anchorMin = new Vector2(0.5f, 0.5f);
                    rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = Vector2.zero;
                    rt.sizeDelta = new Vector2(70f, 70f);
                }

                maskImg = maskObj.AddComponent<Image>();
                maskImg.enabled = false;
            }
            else
            {
                maskImg = maskTf.GetComponent<Image>();
                if (maskImg == null)
                {
                    maskImg = maskTf.gameObject.AddComponent<Image>();
                }
            }

            maskImg.raycastTarget = false;
            maskImg.color = new Color(0f, 0f, 0f, 0.65f);
            maskImg.type = Image.Type.Filled;
            maskImg.fillMethod = Image.FillMethod.Radial360;
            maskImg.fillAmount = 0f;
            cooldownFills[i] = maskImg;

            // Cooldown countdown text
            Transform textTf = slot.Find("CooldownText");
            TMP_Text tmpText;
            if (textTf == null)
            {
                GameObject textObj = new GameObject("CooldownText");
                textObj.transform.SetParent(slot, false);

                RectTransform rt = textObj.AddComponent<RectTransform>();
                if (iconRt != null)
                {
                    rt.anchorMin = iconRt.anchorMin;
                    rt.anchorMax = iconRt.anchorMax;
                    rt.pivot = iconRt.pivot;
                    rt.anchoredPosition = iconRt.anchoredPosition;
                    rt.sizeDelta = iconRt.sizeDelta;
                    textObj.transform.localScale = iconRt.localScale;
                }
                else
                {
                    rt.anchorMin = new Vector2(0.5f, 0.5f);
                    rt.anchorMax = new Vector2(0.5f, 0.5f);
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = Vector2.zero;
                    rt.sizeDelta = new Vector2(70f, 70f);
                }

                TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
                text.text = string.Empty;
                text.fontSize = 22;
                text.alignment = TextAlignmentOptions.Center;
                text.enabled = false;
                text.raycastTarget = false;
                tmpText = text;
            }
            else
            {
                tmpText = textTf.GetComponent<TMP_Text>();
                if (tmpText == null)
                {
                    TextMeshProUGUI text = textTf.gameObject.AddComponent<TextMeshProUGUI>();
                    text.text = string.Empty;
                    text.fontSize = 22;
                    text.alignment = TextAlignmentOptions.Center;
                    text.enabled = false;
                    text.raycastTarget = false;
                    tmpText = text;
                }
                else
                {
                    tmpText.alignment = TextAlignmentOptions.Center;
                    tmpText.raycastTarget = false;
                }
            }

            cooldownTexts[i] = tmpText;
        }
    }


    private TMP_Text EnsureDebugOverlayText(GameObject hudRoot)
    {
        if (hudRoot == null)
        {
            throw new System.Exception("hudRoot is null");
        }

        Transform overlay = hudRoot.transform.Find("Overlay");
        if (overlay == null)
        {
            throw new System.Exception("Missing node: Overlay");
        }

        Transform node = overlay.Find("DebugOverlay");
        if (node == null)
        {
            GameObject obj = new GameObject("DebugOverlay");
            obj.transform.SetParent(overlay, false);
            RectTransform rt = obj.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(10f, -10f);
            rt.sizeDelta = new Vector2(600f, 400f);
            node = obj.transform;
        }

        TMP_Text text = node.GetComponent<TMP_Text>();
        if (text == null)
        {
            text = node.gameObject.AddComponent<TextMeshProUGUI>();
        }

        text.alignment = TextAlignmentOptions.TopLeft;
        text.raycastTarget = false;
        if (text.fontSize <= 0f)
        {
            text.fontSize = 18;
        }
        text.enabled = false;
        return text;
    }

    private TMP_Text EnsureStatusText(GameObject hudRoot)
    {
        if (hudRoot == null)
        {
            throw new System.Exception("hudRoot is null");
        }

        Transform overlay = hudRoot.transform.Find("Overlay");
        if (overlay == null)
        {
            throw new System.Exception("Missing node: Overlay");
        }

        Transform node = overlay.Find("StatusText");
        if (node == null)
        {
            GameObject obj = new GameObject("StatusText");
            obj.transform.SetParent(overlay, false);
            RectTransform rt = obj.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -10f);
            rt.sizeDelta = new Vector2(500f, 60f);
            node = obj.transform;
        }

        TMP_Text text = node.GetComponent<TMP_Text>();
        if (text == null)
        {
            text = node.gameObject.AddComponent<TextMeshProUGUI>();
        }

        text.alignment = TextAlignmentOptions.Top;
        text.raycastTarget = false;
        if (text.fontSize <= 0f)
        {
            text.fontSize = 22;
        }
        text.enabled = false;
        return text;
    }
    private GameObject CreateChild(GameObject parent, string name)
    {
        GameObject child = new GameObject(name);
        child.transform.SetParent(parent.transform, false);
        RectTransform rt = child.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(0, 0);
        rt.pivot = new Vector2(0.5f, 0.5f);
        return child;
    }

    private void SetRectTransform(GameObject obj, Vector2 anchoredPosition, Vector2 anchor, Vector2 size)
    {
        RectTransform rt = obj.GetComponent<RectTransform>();
        if (rt == null) rt = obj.AddComponent<RectTransform>();
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.anchoredPosition = anchoredPosition;
        rt.sizeDelta = size;
    }

    private T FindAndGetComponent<T>(Transform root, string path) where T : Component
    {
        Transform t = root.Find(path);
        if (t == null)
        {
            Debug.LogError($"{LOG_PREFIX} Missing node: {path}");
            throw new System.Exception($"缺少节点: {path}");
        }

        if (typeof(T) == typeof(Transform))
        {
            return t as T;
        }

        T component = t.GetComponent<T>();
        if (component == null)
        {
            Debug.LogError($"{LOG_PREFIX} Expected {typeof(T).Name} at {path}, got null");
            throw new System.Exception($"节点类型错误: {path} 应为 {typeof(T).Name}");
        }
        return component;
    }

    private void ValidateReplacePanelNode<T>(Transform panelRoot, string relativePath, ref bool hasError) where T : Component
    {
        Transform t = panelRoot.Find(relativePath);
        if (t == null)
        {
            Debug.LogError($"{LOG_PREFIX}[ERROR] Missing ReplacePanel node: {relativePath}");
            hasError = true;
            return;
        }

        if (typeof(T) == typeof(Transform))
        {
            return; // Transform 类型只检查存在性（任何 GameObject 都有 Transform）
        }

        T component = t.GetComponent<T>();
        if (component == null)
        {
            Debug.LogError($"{LOG_PREFIX}[ERROR] Expected {typeof(T).Name} at {relativePath}, got {t.GetComponent<Component>()?.GetType().Name ?? "null"}");
            hasError = true;
        }
    }

    // ===== 模板选项 =====

    private void RefreshTemplateOptions()
    {
        templateOptions.Clear();

        if (File.Exists(CURRENT_TEMPLATE_PATH))
        {
            templateOptions.Add(new TemplateOption
            {
                kind = TemplateKind.PrefabAsset,
                displayName = CURRENT_TEMPLATE_NAME,
                prefabPath = CURRENT_TEMPLATE_PATH
            });
        }

        if (Directory.Exists(TEMPLATE_DIR))
        {
            string[] prefabFiles = Directory.GetFiles(TEMPLATE_DIR, "*.prefab", SearchOption.TopDirectoryOnly);
            System.Array.Sort(prefabFiles);
            foreach (string path in prefabFiles)
            {
                if (path.Replace("\\", "/").Equals(CURRENT_TEMPLATE_PATH))
                {
                    continue;
                }

                templateOptions.Add(new TemplateOption
                {
                    kind = TemplateKind.PrefabAsset,
                    displayName = Path.GetFileNameWithoutExtension(path),
                    prefabPath = path.Replace("\\", "/")
                });
            }
        }

        templateOptions.Add(new TemplateOption
        {
            kind = TemplateKind.CodeDefault,
            displayName = "默认代码模板",
            prefabPath = string.Empty
        });

        if (selectedTemplateIndex < 0 || selectedTemplateIndex >= templateOptions.Count)
        {
            selectedTemplateIndex = 0;
        }
    }

    private string[] GetTemplateDisplayNames()
    {
        if (templateOptions.Count == 0)
        {
            return new[] { "默认代码模板" };
        }

        string[] names = new string[templateOptions.Count];
        for (int i = 0; i < templateOptions.Count; i++)
        {
            names[i] = templateOptions[i].displayName;
        }
        return names;
    }

    private TemplateOption GetSelectedTemplate()
    {
        if (templateOptions.Count == 0)
        {
            return new TemplateOption
            {
                kind = TemplateKind.CodeDefault,
                displayName = "默认代码模板",
                prefabPath = string.Empty
            };
        }

        int index = Mathf.Clamp(selectedTemplateIndex, 0, templateOptions.Count - 1);
        return templateOptions[index];
    }

    /// <summary>
    /// 生成输出 HUD 模板 Prefab 路径
    /// </summary>
    private string GetOutputPrefabPath(TemplateOption option)
    {
        string baseName = option.kind == TemplateKind.CodeDefault ? "HUDTemplate_Default" : option.displayName;
        baseName = MakeSafeFileName(baseName);
        if (string.IsNullOrEmpty(baseName))
        {
            baseName = "HUDTemplate";
        }

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string fileName = $"{baseName}_{timestamp}.prefab";
        return Path.Combine(TEMPLATE_OUTPUT_DIR, fileName).Replace("\\", "/");
    }

    /// <summary>
    /// 生成安全的文件名（ASCII）
    /// </summary>
    private string MakeSafeFileName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        StringBuilder builder = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            if (c <= 127)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                {
                    builder.Append(c);
                }
                else if (c == ' ' )
                {
                    builder.Append('_');
                }
            }
        }

        return builder.ToString().Trim('_');
    }

    /// <summary>
    /// 保存当前 HUDCanvas 为模板
    /// </summary>
    private void SaveCurrentTemplate()
    {
        Debug.Log($"{LOG_PREFIX} 开始保存当前HUDCanvas为模板...");

        try
        {
            EnsureCurrentSnapshotTemplate();
            RefreshTemplateOptions();
            Debug.Log($"{LOG_PREFIX} 当前HUDCanvas已保存为模板: {CURRENT_TEMPLATE_PATH}");
            EditorUtility.DisplayDialog("成功", "已保存当前HUDCanvas为模板\n\n路径：\n- " + CURRENT_TEMPLATE_PATH, "确定");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"{LOG_PREFIX} 保存当前HUDCanvas模板失败: {ex.Message}\n{ex.StackTrace}");
            EditorUtility.DisplayDialog("失败", "保存当前HUDCanvas模板失败，详情见 Console", "确定");
        }
    }

    private void EnsureCurrentSnapshotTemplate()
    {
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
        if (source == null)
        {
            throw new System.Exception("HUDCanvas.prefab 不存在，无法保存模板");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(CURRENT_TEMPLATE_PATH));

        // IMPORTANT: Do not delete HUDCanvas_Snapshot.prefab. Deleting regenerates its .meta GUID,
        // which will break any Prefab Variants that use it as their parent.
        if (!File.Exists(CURRENT_TEMPLATE_PATH))
        {
            if (!AssetDatabase.CopyAsset(PREFAB_PATH, CURRENT_TEMPLATE_PATH))
            {
                throw new System.Exception("复制 HUDCanvas 模板失败");
            }

            AssetDatabase.Refresh();
            return;
        }

        string src = File.ReadAllText(PREFAB_PATH, Encoding.UTF8);
        File.WriteAllText(CURRENT_TEMPLATE_PATH, src, Encoding.UTF8);
        AssetDatabase.ImportAsset(CURRENT_TEMPLATE_PATH, ImportAssetOptions.ForceUpdate);
    }

    private HudBindingAsset EnsureBindingAsset()
    {
        HudBindingAsset binding = AssetDatabase.LoadAssetAtPath<HudBindingAsset>(BINDING_PATH);
        if (binding == null)
        {
            binding = CreateInstance<HudBindingAsset>();
            AssetDatabase.CreateAsset(binding, BINDING_PATH);
            Debug.Log($"{LOG_PREFIX} HudBinding.asset 创建成功: {BINDING_PATH}");
        }

        return binding;
    }

    private GameObject CreateTemplateFromPrefab(TemplateOption option, string outputPath)
    {
        if (string.IsNullOrEmpty(option.prefabPath))
        {
            throw new System.Exception("模板路径为空");
        }

        GameObject templateAsset = AssetDatabase.LoadAssetAtPath<GameObject>(option.prefabPath);
        if (templateAsset == null)
        {
            throw new System.Exception($"模板 prefab 不存在: {option.prefabPath}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
        GameObject instance = PrefabUtility.InstantiatePrefab(templateAsset) as GameObject;
        if (instance == null)
        {
            instance = UnityEngine.Object.Instantiate(templateAsset);
        }

        // Save as a regular prefab (not a variant) to avoid depending on a parent prefab.
        // This prevents issues when templates are created from snapshot prefabs.
        if (PrefabUtility.IsPartOfPrefabInstance(instance))
        {
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        }

        try
        {
            HudRefs hudRefs = instance.GetComponent<HudRefs>();
            if (hudRefs == null)
            {
                throw new System.Exception("模板 Prefab 缺少 HudRefs 组件");
            }

            GameObject prefabAsset = PrefabUtility.SaveAsPrefabAsset(instance, outputPath);
            if (prefabAsset == null)
            {
                throw new System.Exception("保存 HUD 模板 Prefab 失败");
            }

            Debug.Log($"{LOG_PREFIX} HUD 模板已保存: {outputPath}");
            return prefabAsset;
        }
        finally
        {
            DestroyImmediate(instance);
        }
    }
}
