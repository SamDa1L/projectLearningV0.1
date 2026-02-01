using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

public partial class HudQuickConfigWindow : EditorWindow
{
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


    // Phase 8：每个技能槽的可选冷却 UI（遮罩 + 倒计时文本）。
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

            // 冷却遮罩
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

            // 冷却倒计时文本
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

        // 重要：不要删除 HUDCanvas_Snapshot.prefab；删除会导致 .meta 的 GUID 重新生成，
        // 从而打断所有以它为父 Prefab 的 Prefab Variant 引用关系。
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

        // 以“普通 Prefab”（非 Variant）方式保存，避免依赖父 Prefab；
        // 这样模板即使由 snapshot prefab 创建，也不会出现 Variant 继承链问题。
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

    /// <summary>
    /// 清理输出模板目录：保留最近 N 个，其余移动到归档目录（R1-5）。
    /// </summary>
    private void PruneGeneratedTemplates(int keepCount, bool showDialog)
    {
        keepCount = Mathf.Clamp(keepCount, 1, 200);

        if (!Directory.Exists(TEMPLATE_OUTPUT_DIR))
        {
            Debug.Log($"{LOG_PREFIX} 输出模板目录不存在，无需整理: {TEMPLATE_OUTPUT_DIR}");
            if (showDialog)
            {
                EditorUtility.DisplayDialog("提示", "输出模板目录不存在，无需整理。\n\n" + TEMPLATE_OUTPUT_DIR, "确定");
            }
            return;
        }

        // 保护当前 HudBinding 绑定的 hudPrefab，避免误归档。
        string boundPrefabPath = null;
        HudBindingAsset binding = AssetDatabase.LoadAssetAtPath<HudBindingAsset>(BINDING_PATH);
        if (binding != null && binding.hudPrefab != null)
        {
            boundPrefabPath = AssetDatabase.GetAssetPath(binding.hudPrefab);
        }

        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { TEMPLATE_OUTPUT_DIR });
        List<string> prefabPaths = new List<string>(guids.Length);
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrWhiteSpace(path) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            prefabPaths.Add(path);
        }

        prefabPaths.Sort((a, b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));

        HashSet<string> keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < prefabPaths.Count && i < keepCount; i++)
        {
            keep.Add(prefabPaths[i]);
        }

        if (!string.IsNullOrWhiteSpace(boundPrefabPath))
        {
            keep.Add(boundPrefabPath);
        }

        List<string> toArchive = new List<string>();
        for (int i = 0; i < prefabPaths.Count; i++)
        {
            string path = prefabPaths[i];
            if (!keep.Contains(path))
            {
                toArchive.Add(path);
            }
        }

        if (toArchive.Count == 0)
        {
            Debug.Log($"{LOG_PREFIX} 模板数量未超过保留阈值（{keepCount}），无需整理。");
            if (showDialog)
            {
                EditorUtility.DisplayDialog("提示", $"无需整理：当前输出模板数量未超过保留阈值（{keepCount}）。", "确定");
            }
            return;
        }

        EnsureAssetFolder(TEMPLATE_ARCHIVE_DIR);

        int archivedCount = 0;
        foreach (string srcPath in toArchive)
        {
            string dstPath = Path.Combine(TEMPLATE_ARCHIVE_DIR, Path.GetFileName(srcPath)).Replace("\\", "/");
            dstPath = AssetDatabase.GenerateUniqueAssetPath(dstPath);

            string err = AssetDatabase.MoveAsset(srcPath, dstPath);
            if (!string.IsNullOrEmpty(err))
            {
                Debug.LogWarning($"{LOG_PREFIX} 归档失败: {srcPath} -> {dstPath}\n原因: {err}");
                continue;
            }

            archivedCount++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"{LOG_PREFIX} 模板整理完成：保留最近 {keepCount} 个，归档 {archivedCount} 个。归档目录: {TEMPLATE_ARCHIVE_DIR}");

        if (showDialog)
        {
            EditorUtility.DisplayDialog(
                "完成",
                $"模板整理完成。\n\n保留最近 {keepCount} 个输出模板。\n已归档 {archivedCount} 个模板。\n\n归档目录：\n- {TEMPLATE_ARCHIVE_DIR}",
                "确定"
            );
        }
    }

    /// <summary>
    /// 确保 Unity 的 Folder 资产存在（支持递归创建多级目录）。
    /// </summary>
    private static void EnsureAssetFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return;
        }

        folderPath = folderPath.Replace("\\", "/");

        if (AssetDatabase.IsValidFolder(folderPath))
        {
            return;
        }

        string parent = Path.GetDirectoryName(folderPath)?.Replace("\\", "/");
        if (string.IsNullOrWhiteSpace(parent))
        {
            return;
        }

        EnsureAssetFolder(parent);

        string folderName = Path.GetFileName(folderPath);
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            AssetDatabase.CreateFolder(parent, folderName);
        }
    }
}
