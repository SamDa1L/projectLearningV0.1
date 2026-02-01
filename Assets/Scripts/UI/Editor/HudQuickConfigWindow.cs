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
public partial class HudQuickConfigWindow : EditorWindow
{
    // 资源路径（硬契约）
    private const string PREFAB_PATH = "Assets/Resources/Prefabs/UI/HUDCanvas.prefab";
    private const string BINDING_PATH = "Assets/Resources/Config/HudBinding.asset";
    private const string TEMPLATE_DIR = "Assets/Editor/HUDTemplates";
    private const string CURRENT_TEMPLATE_PATH = "Assets/Editor/HUDTemplates/HUDCanvas_Snapshot.prefab";
    private const string CURRENT_TEMPLATE_NAME = "当前HUDCanvas";
    // 模板属于 Editor 侧生成快照：不要放进 Resources，避免打包膨胀。
    private const string TEMPLATE_OUTPUT_DIR = "Assets/_Generated/HUDTemplates";
    // 模板归档目录：用于清理历史输出，避免目录长期膨胀。
    private const string TEMPLATE_ARCHIVE_DIR = "Assets/_Legacy/HUDTemplates_Archive";

    // 日志前缀
    private const string LOG_PREFIX = "[HUDQuickConfig]";

    // 模板保留策略（R1-5）：使用 EditorPrefs 持久化，避免每次打开窗口都要重设。
    private const string PREFS_AUTO_PRUNE_KEY = "HudQuickConfigWindow.AutoPruneGeneratedTemplates";
    private const string PREFS_KEEP_COUNT_KEY = "HudQuickConfigWindow.GeneratedTemplateKeepCount";
    private const int DEFAULT_KEEP_COUNT = 10;

    private bool _autoPruneGeneratedTemplates = true;
    private int _generatedTemplateKeepCount = DEFAULT_KEEP_COUNT;

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
        _autoPruneGeneratedTemplates = EditorPrefs.GetBool(PREFS_AUTO_PRUNE_KEY, true);
        _generatedTemplateKeepCount = Mathf.Clamp(EditorPrefs.GetInt(PREFS_KEEP_COUNT_KEY, DEFAULT_KEEP_COUNT), 1, 200);
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

        // ===== 模板保留策略（R1-5） =====

        EditorGUILayout.LabelField("模板保留策略", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        _autoPruneGeneratedTemplates = EditorGUILayout.Toggle("Create Template 后自动整理输出模板", _autoPruneGeneratedTemplates);
        _generatedTemplateKeepCount = EditorGUILayout.IntField("保留最近 N 个输出模板", _generatedTemplateKeepCount);
        _generatedTemplateKeepCount = Mathf.Clamp(_generatedTemplateKeepCount, 1, 200);
        if (EditorGUI.EndChangeCheck())
        {
            EditorPrefs.SetBool(PREFS_AUTO_PRUNE_KEY, _autoPruneGeneratedTemplates);
            EditorPrefs.SetInt(PREFS_KEEP_COUNT_KEY, _generatedTemplateKeepCount);
        }

        if (GUILayout.Button("立即整理输出模板", GUILayout.Height(28)))
        {
            PruneGeneratedTemplates(_generatedTemplateKeepCount, true);
        }

        EditorGUILayout.HelpBox(
            "说明：输出模板位于：\n" +
            "- " + TEMPLATE_OUTPUT_DIR + "\n\n" +
            "为避免历史模板累计导致项目膨胀，可仅保留最近 N 个，其余会被归档到：\n" +
            "- " + TEMPLATE_ARCHIVE_DIR,
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

}
