using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;
using System.IO;

/// <summary>
/// HUD Quick Config 工具（Phase 10）
/// 契约 [C-Tool-1]: Create Template / AutoBind / Validate
///
/// 功能：
/// - Create Template: 生成 HUDCanvas.prefab 和 HudBinding.asset
/// - AutoBind: 自动绑定 HudRefs 字段（按固定路径）
/// - Validate: 校验节点完整性（符合 [C-UI-1]/[C-UI-2]）
///
/// 菜单路径：Tools/UI/HUD Quick Config
/// </summary>
public class HudQuickConfigWindow : EditorWindow
{
    // 资源路径（硬契约）
    private const string PREFAB_PATH = "Assets/Resources/Prefabs/UI/HUDCanvas.prefab";
    private const string BINDING_PATH = "Assets/Resources/Config/HudBinding.asset";

    // 日志前缀
    private const string LOG_PREFIX = "[HUDQuickConfig]";

    [MenuItem("Tools/UI/HUD Quick Config")]
    public static void ShowWindow()
    {
        var window = GetWindow<HudQuickConfigWindow>("HUD Quick Config");
        window.minSize = new Vector2(400, 300);
    }

    private void OnGUI()
    {
        GUILayout.Label("HUD Quick Config (Phase 10)", EditorStyles.boldLabel);
        GUILayout.Space(10);

        EditorGUILayout.HelpBox(
            "此工具生成 HUD 必需的资源产物：\n" +
            "- HUDCanvas.prefab (Assets/Resources/Prefabs/UI/)\n" +
            "- HudBinding.asset (Assets/Resources/Config/)\n\n" +
            "符合契约 [C-UI-1]/[C-UI-2]",
            MessageType.Info
        );

        GUILayout.Space(10);

        // Create Template 按钮
        if (GUILayout.Button("Create Template", GUILayout.Height(40)))
        {
            CreateTemplate();
        }

        GUILayout.Space(5);

        // AutoBind 按钮
        if (GUILayout.Button("AutoBind", GUILayout.Height(40)))
        {
            AutoBind();
        }

        GUILayout.Space(5);

        // Validate 按钮
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
    /// Create Template: 生成 HUD 模板 prefab 和 HudBinding.asset
    /// 契约 [C-Tool-1]: 符合 [C-UI-1]/[C-UI-2] 节点结构
    /// </summary>
    private void CreateTemplate()
    {
        Debug.Log($"{LOG_PREFIX} 开始 Create Template...");

        try
        {
            // 确保目录存在
            Directory.CreateDirectory(Path.GetDirectoryName(PREFAB_PATH));
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
            GameObject bottomLeft = CreateChild(hudRoot, "BottomLeft");
            GameObject bottomCenter = CreateChild(hudRoot, "BottomCenter");
            GameObject overlay = CreateChild(hudRoot, "Overlay");

            // 创建 AbilityBar（4 槽）
            GameObject abilityBar = CreateChild(bottomLeft, "AbilityBar");
            SetRectTransform(abilityBar, new Vector2(10, 10), new Vector2(0, 0), new Vector2(0, 0));

            Image[] slotIcons = new Image[4];
            for (int i = 0; i < 4; i++)
            {
                GameObject slot = CreateChild(abilityBar, $"Slot_{i}");
                SetRectTransform(slot, new Vector2(i * 70, 0), new Vector2(0, 0), new Vector2(60, 60));

                GameObject iconObj = CreateChild(slot, "Icon");
                slotIcons[i] = iconObj.AddComponent<Image>();
                slotIcons[i].enabled = false; // 默认禁用（空槽）
                SetRectTransform(iconObj, Vector2.zero, new Vector2(0.5f, 0.5f), new Vector2(50, 50));
            }

            // 创建 PotionWidget
            GameObject potionWidget = CreateChild(bottomLeft, "PotionWidget");
            SetRectTransform(potionWidget, new Vector2(10, 80), new Vector2(0, 0), new Vector2(80, 60));

            GameObject countTextObj = CreateChild(potionWidget, "CountText");
            TMP_Text potionCountText = countTextObj.AddComponent<TextMeshProUGUI>();
            potionCountText.text = "0";
            potionCountText.fontSize = 24;
            potionCountText.alignment = TextAlignmentOptions.Center;
            SetRectTransform(countTextObj, Vector2.zero, new Vector2(0.5f, 0.5f), new Vector2(80, 60));

            // 创建 HealthBar
            GameObject healthBar = CreateChild(bottomCenter, "HealthBar");
            SetRectTransform(healthBar, new Vector2(0, 10), new Vector2(0.5f, 0), new Vector2(200, 30));

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
            SetRectTransform(healthFillObj, Vector2.zero, new Vector2(0, 0.5f), new Vector2(200, 30));

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

            // 绑定 HudRefs 字段
            hudRefs.abilitySlotIcons = slotIcons;
            hudRefs.potionCountText = potionCountText;
            hudRefs.healthFill = healthFill;
            hudRefs.energyFill = energyFill;
            hudRefs.abilityReplacePanelRoot = replacePanel;

            // 保存 Prefab
            GameObject prefabAsset = PrefabUtility.SaveAsPrefabAsset(hudRoot, PREFAB_PATH);
            DestroyImmediate(hudRoot);

            Debug.Log($"{LOG_PREFIX} HUDCanvas.prefab 创建成功: {PREFAB_PATH}");

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

            Debug.Log($"{LOG_PREFIX} ✅ Create Template 完成！");
            EditorUtility.DisplayDialog("成功", "HUD 模板创建完成！\n\n产物：\n- " + PREFAB_PATH + "\n- " + BINDING_PATH, "确定");
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
    /// AutoBind: 自动绑定 HudRefs 字段（按固定路径）
    /// </summary>
    private void AutoBind()
    {
        Debug.Log($"{LOG_PREFIX} 开始 AutoBind...");

        // 契约 [C-Tool-1]: AutoBind 必须保证 binding.hudPrefab != null
        HudBindingAsset binding = AssetDatabase.LoadAssetAtPath<HudBindingAsset>(BINDING_PATH);
        if (binding == null || binding.hudPrefab == null)
        {
            Debug.LogError($"{LOG_PREFIX} HudBinding.asset 或 hudPrefab 为空，请先 Create Template 或手动指定 hudPrefab");
            EditorUtility.DisplayDialog("失败", "HudBinding.asset 或 hudPrefab 为空\n\n请先执行 Create Template 或手动指定 hudPrefab", "确定");
            return;
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
        if (prefab == null)
        {
            Debug.LogError($"{LOG_PREFIX} HUDCanvas.prefab 不存在，请先 Create Template");
            EditorUtility.DisplayDialog("失败", "HUDCanvas.prefab 不存在，请先 Create Template", "确定");
            return;
        }

        HudRefs hudRefs = prefab.GetComponent<HudRefs>();
        if (hudRefs == null)
        {
            Debug.LogError($"{LOG_PREFIX} HUDCanvas.prefab 缺少 HudRefs 组件");
            EditorUtility.DisplayDialog("失败", "HUDCanvas.prefab 缺少 HudRefs 组件", "确定");
            return;
        }

        // 按固定路径绑定（符合 [C-UI-1]）
        try
        {
            hudRefs.abilitySlotIcons = new Image[4];
            for (int i = 0; i < 4; i++)
            {
                string path = $"BottomLeft/AbilityBar/Slot_{i}/Icon";
                Transform t = prefab.transform.Find(path);
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

            hudRefs.potionCountText = FindAndGetComponent<TMP_Text>(prefab.transform, "BottomLeft/PotionWidget/CountText");
            hudRefs.healthFill = FindAndGetComponent<Image>(prefab.transform, "BottomCenter/HealthBar/Fill");
            hudRefs.energyFill = FindAndGetComponent<Image>(prefab.transform, "BottomCenter/EnergyBar/Fill");
            hudRefs.abilityReplacePanelRoot = FindAndGetComponent<Transform>(prefab.transform, "Overlay/AbilityReplacePanel").gameObject;

            EditorUtility.SetDirty(prefab);
            AssetDatabase.SaveAssets();

            Debug.Log($"{LOG_PREFIX} ✅ AutoBind 完成！");
            EditorUtility.DisplayDialog("成功", "AutoBind 完成！所有引用已绑定。", "确定");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"{LOG_PREFIX} AutoBind 失败: {ex.Message}");
            EditorUtility.DisplayDialog("失败", "AutoBind 失败，详见 Console", "确定");
        }
    }

    /// <summary>
    /// Validate: 校验节点完整性（符合 [C-UI-1]/[C-UI-2]）
    /// </summary>
    private void Validate()
    {
        Debug.Log($"{LOG_PREFIX} 开始 Validate...");

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
        if (prefab == null)
        {
            Debug.LogError($"{LOG_PREFIX} HUDCanvas.prefab 不存在");
            EditorUtility.DisplayDialog("失败", "HUDCanvas.prefab 不存在，请先 Create Template", "确定");
            return;
        }

        HudRefs hudRefs = prefab.GetComponent<HudRefs>();
        if (hudRefs == null)
        {
            Debug.LogError($"{LOG_PREFIX} HUDCanvas.prefab 缺少 HudRefs 组件");
            EditorUtility.DisplayDialog("失败", "HUDCanvas.prefab 缺少 HudRefs 组件", "确定");
            return;
        }

        bool hasError = false;

        // 校验 abilitySlotIcons
        if (hudRefs.abilitySlotIcons == null || hudRefs.abilitySlotIcons.Length != 4)
        {
            Debug.LogError($"{LOG_PREFIX}[ERROR] Ability slot icon count must be 4, got {hudRefs.abilitySlotIcons?.Length ?? 0}");
            hasError = true;
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

        if (!hasError)
        {
            Debug.Log($"{LOG_PREFIX} ✅ Validate 通过！所有节点完整。");
            EditorUtility.DisplayDialog("成功", "Validate 通过！HUD 结构符合契约要求。", "确定");
        }
        else
        {
            Debug.LogError($"{LOG_PREFIX} ❌ Validate 失败，详见 Console");
            EditorUtility.DisplayDialog("失败", "Validate 失败，发现缺失节点或类型错误，详见 Console", "确定");
        }
    }

    // ===== 辅助方法 =====

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
}
