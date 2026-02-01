using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CastleDB.Runtime;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Prefab 检测工具（0.5 阶段10）。
///
/// 设计目标：
/// - 只读检查：不修改任何 Prefab/资源。
/// - 一键输出：阻塞/警告/建议三档结果，并支持复制/导出 Markdown。
///
/// 注意：
/// - 首版规则以【地面敌人模板 + KnightEnemy】为基准。
/// </summary>
public class PrefabValidationWindow : EditorWindow
{
    private enum Severity
    {
        Blocker,
        Warning,
        Suggestion,
    }

    [Serializable]
    private sealed class Issue
    {
        public Severity severity;
        public string title;
        public string details;
        public string fix;
        public UnityEngine.Object context;
    }

    [SerializeField] private GameObject prefabAsset;

    private readonly List<Issue> _issues = new List<Issue>();
    private Vector2 _scroll;
    private string _prefabPath;
    private string _reportMarkdown;
    private bool _hasReport;

    // ===== 0.5 阶段10：EnemyAbilityCatalog（用于 SecondaryAttack「按需必填」判定） =====
    private const string EnemyAbilityCatalogResourcePath = "Config/EnemyAbilityCatalog";
    private const string EnemyAbilityCatalogAssetPath = "Assets/Resources/Config/EnemyAbilityCatalog.asset";
    private static AbilityCatalog _enemyAbilityCatalogCache;
    private static Dictionary<string, AbilityKind> _enemyAbilityKindByIdCache;

    [MenuItem("Tools/Tests/prefab检测")]
    public static void Open()
    {
        var window = GetWindow<PrefabValidationWindow>("prefab检测");
        window.minSize = new Vector2(520, 420);
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("prefab 检测（只读）", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        prefabAsset = (GameObject)EditorGUILayout.ObjectField("Prefab", prefabAsset, typeof(GameObject), false);

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(prefabAsset == null))
            {
                if (GUILayout.Button("检测", GUILayout.Height(26)))
                {
                    RunValidation();
                }
            }

            using (new EditorGUI.DisabledScope(!_hasReport))
            {
                if (GUILayout.Button("复制Markdown", GUILayout.Height(26)))
                {
                    EditorGUIUtility.systemCopyBuffer = _reportMarkdown ?? string.Empty;
                }

                if (GUILayout.Button("导出Markdown", GUILayout.Height(26)))
                {
                    ExportMarkdown();
                }
            }
        }

        if (!_hasReport)
        {
            EditorGUILayout.HelpBox(
                "拖入一个 Prefab 资产，然后点击【检测】。\n工具只做只读检查，不会修改任何资产。",
                MessageType.Info);
            return;
        }

        int blockerCount = _issues.Count(i => i.severity == Severity.Blocker);
        int warningCount = _issues.Count(i => i.severity == Severity.Warning);
        int suggestionCount = _issues.Count(i => i.severity == Severity.Suggestion);

        EditorGUILayout.HelpBox(
            $"结果：阻塞 {blockerCount} / 警告 {warningCount} / 建议 {suggestionCount}\nPrefab: {_prefabPath}",
            blockerCount > 0 ? MessageType.Error : MessageType.Info);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        foreach (var issue in _issues)
        {
            DrawIssue(issue);
        }
        EditorGUILayout.EndScrollView();
    }

    private void RunValidation()
    {
        _issues.Clear();
        _prefabPath = string.Empty;
        _reportMarkdown = string.Empty;
        _hasReport = false;

        // 缓存可能跨多次导入变化，这里每次检测前都重建一次，避免误报。
        _enemyAbilityCatalogCache = null;
        _enemyAbilityKindByIdCache = null;

        if (prefabAsset == null)
        {
            return;
        }

        string path = AssetDatabase.GetAssetPath(prefabAsset);
        if (string.IsNullOrWhiteSpace(path))
        {
            Add(
                Severity.Blocker,
                "请选择 Project 里的 Prefab 资产",
                "当前对象没有 AssetDatabase 路径，可能是场景对象。",
                "请从 Project 窗口拖入 Prefab 资产。",
                prefabAsset);
            FinalizeReport();
            return;
        }

        if (!PrefabUtility.IsPartOfPrefabAsset(prefabAsset))
        {
            Add(
                Severity.Blocker,
                "目标不是 Prefab 资产",
                $"路径: {path}",
                "请拖入 Prefab 资产（而不是材质/纹理/脚本等）。",
                prefabAsset);
            FinalizeReport();
            return;
        }

        _prefabPath = path;

        GameObject root = null;
        try
        {
            root = PrefabUtility.LoadPrefabContents(path);
            ValidatePrefabRoot(root);
        }
        catch (Exception ex)
        {
            Add(
                Severity.Blocker,
                "检测过程发生异常",
                ex.Message,
                "请检查 Prefab 是否损坏，或把异常堆栈发出来定位。",
                prefabAsset);
        }
        finally
        {
            if (root != null)
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        FinalizeReport();
    }

    private void ValidatePrefabRoot(GameObject root)
    {
        if (root == null)
        {
            Add(
                Severity.Blocker,
                "无法加载 Prefab 内容",
                "LoadPrefabContents 返回空。",
                "请检查该 Prefab 资产是否可被 Unity 正常加载。",
                prefabAsset);
            return;
        }

        var agent = root.GetComponent<EnemyAgentBase>();
        if (agent == null)
        {
            Add(
                Severity.Blocker,
                "根对象缺少 EnemyAgentBase",
                "该工具首版以【地面敌人模板】为口径。",
                "请确认该 Prefab 是否为敌人 Prefab，或在根对象挂载 EnemyAgentBase 子类脚本。",
                root);
            return;
        }

        RequireComponent<Rigidbody2D>(root, "Rigidbody2D（移动/受击/物理）", Severity.Blocker);
        RequireComponent<Collider2D>(root, "Collider2D（地面碰撞）", Severity.Blocker);
        RequireComponent<Animator>(root, "Animator（动画/事件链路）", Severity.Blocker);
        RequireComponent<Damageable>(root, "Damageable（受击/扣血）", Severity.Blocker);
        RequireComponent<TouchingDirections>(root, "TouchingDirections（贴墙/落地等状态）", Severity.Blocker);

        var profile = agent.TuningProfile;
        if (profile == null)
        {
            Add(
                Severity.Blocker,
                "tuningProfile 未分配",
                "EnemyAgentBase.tuningProfile 为空，会导致敌人参数与能力无法下发。",
                "请在根对象的 EnemyAgentBase 上分配对应的 EnemyTuningProfile。",
                agent);
        }

        ValidateZoneBindings(agent, profile);
        ValidateFirePoint(root.transform);
        ValidateOptionalZoneEvents(root.transform);
        ValidateLayers(root, agent);
        ValidateOptionalPreplacedComponents(root);
    }

    private void ValidateZoneBindings(EnemyAgentBase agent, EnemyTuningProfile profile)
    {
        var so = new SerializedObject(agent);
        var zoneBindingsProp = so.FindProperty("zoneBindings");
        if (zoneBindingsProp == null || zoneBindingsProp.arraySize == 0)
        {
            Add(
                Severity.Blocker,
                "zoneBindings 为空",
                "EnemyAgentBase 依赖 zoneBindings 作为唯一检测区数据源。",
                "请在 Inspector 中配置至少一个 PrimaryAttack 或 SecondaryAttack 绑定，并拖拽子物体的 DetectionZone。",
                agent);
            return;
        }

        bool hasValidPrimary = false;
        bool hasValidSecondary = false;

        for (int i = 0; i < zoneBindingsProp.arraySize; i++)
        {
            var element = zoneBindingsProp.GetArrayElementAtIndex(i);
            var roleProp = element.FindPropertyRelative("role");
            var zoneProp = element.FindPropertyRelative("zone");

            var role = (DetectionZoneBinding.Role)roleProp.enumValueIndex;
            var zone = zoneProp.objectReferenceValue as DetectionZone;

            if (zone == null)
            {
                Add(
                    Severity.Blocker,
                    $"zoneBindings[{i}] 的 zone 为空",
                    $"role={role}",
                    "请删除该条空 binding，或拖拽对应子物体上的 DetectionZone 到 zone 字段。",
                    agent);
                continue;
            }

            var col = zone.GetComponent<Collider2D>();
            if (col == null)
            {
                Add(
                    Severity.Blocker,
                    "检测区缺少 Collider2D",
                    $"role={role}, obj='{zone.gameObject.name}'",
                    "请在该检测区物体上添加 Collider2D，并勾选 isTrigger。",
                    zone);
            }
            else if (!col.isTrigger)
            {
                Add(
                    Severity.Blocker,
                    "检测区 Collider2D 未勾选 isTrigger",
                    $"role={role}, obj='{zone.gameObject.name}'",
                    "请把 Collider2D.isTrigger 设为 true。",
                    zone);
            }

            if (role == DetectionZoneBinding.Role.PrimaryAttack)
            {
                hasValidPrimary = true;
                if (!string.Equals(zone.gameObject.name, "DZ_Attack", StringComparison.Ordinal))
                {
                    Add(
                        Severity.Suggestion,
                        "PrimaryAttack 检测区命名不符合约定",
                        $"当前: '{zone.gameObject.name}'，建议: 'DZ_Attack'。",
                        "建议按约定命名，降低同步/排查成本。",
                        zone);
                }
            }
            else if (role == DetectionZoneBinding.Role.SecondaryAttack)
            {
                hasValidSecondary = true;
                if (!string.Equals(zone.gameObject.name, "DZ_Ability", StringComparison.Ordinal))
                {
                    Add(
                        Severity.Suggestion,
                        "SecondaryAttack 检测区命名不符合约定",
                        $"当前: '{zone.gameObject.name}'，建议: 'DZ_Ability'。",
                        "建议按约定命名，降低同步/排查成本。",
                        zone);
                }
            }
        }

        if (!hasValidPrimary && !hasValidSecondary)
        {
            Add(
                Severity.Blocker,
                "PrimaryAttack / SecondaryAttack 至少需要一个有效绑定",
                "两者都缺失或都无效会导致敌人无法获得目标。",
                "请配置至少一个可用的检测区绑定。",
                agent);
        }

        if (profile != null)
        {
            if (IsSecondaryAttackRequired(profile, out var reason) && !hasValidSecondary)
            {
                Add(
                    Severity.Blocker,
                    "存在 SecondaryAttack 依赖但未配置 SecondaryAttack 检测区",
                    reason,
                    "请创建并绑定 SecondaryAttack（建议子物体名 DZ_Ability），或移除/调整所有 SecondaryAttack 依赖。",
                    agent);
            }
        }
    }

    /// <summary>
    /// 判断 SecondaryAttack 是否为【按需必填】。
    ///
    /// 目标：
    /// - 当 NPC 只配置 PrimaryAttack 且无 Secondary 依赖时，不应报错。
    ///
    /// 判定口径（首版）：
    /// 1) 启用的 NpcAbility 使用 triggerRole=SecondaryAttack 且 EnemyAbility.kind=Projectile。
    /// 2) 启用的被动能力条件包含 HasTargetInRole(role=SecondaryAttack)。
    /// 3) 被动能力 targetMode=CurrentTarget 且没有任何 HasTargetInRole 条件时，会回退到 triggerRole 找目标。
    /// </summary>
    private static bool IsSecondaryAttackRequired(EnemyTuningProfile profile, out string reason)
    {
        reason = string.Empty;
        if (profile == null)
        {
            return false;
        }

        var enabledNpcAbilities = new Dictionary<string, NpcAbilityEntry>();
        if (profile.npcAbilities != null)
        {
            foreach (var a in profile.npcAbilities)
            {
                if (a == null || !a.enabled || string.IsNullOrWhiteSpace(a.id))
                {
                    continue;
                }
                enabledNpcAbilities[a.id] = a;
            }
        }

        if (enabledNpcAbilities.Count == 0)
        {
            return false;
        }

        // 规则1：投射物/施法：NpcAbility(triggerRole=SecondaryAttack) 且 EnemyAbility.kind=Projectile
        foreach (var a in enabledNpcAbilities.Values)
        {
            if (a.triggerRole != (int)DetectionZoneBinding.Role.SecondaryAttack)
            {
                continue;
            }

            if (!TryGetEnemyAbilityKind(a.abilityId, out var kind))
            {
                // kind 无法解析时保守处理为必填，避免漏报（正常情况下导入会生成 EnemyAbilityCatalog）
                reason = $"NpcAbility '{a.id}' triggerRole=SecondaryAttack，但无法解析 EnemyAbility.kind（abilityId='{a.abilityId}'）";
                return true;
            }

            if (kind == AbilityKind.Projectile)
            {
                reason = $"存在投射物类 NpcAbility(triggerRole=SecondaryAttack)：{a.id}";
                return true;
            }
        }

        // 规则2：启用的被动能力条件使用 HasTargetInRole(SecondaryAttack)
        if (profile.npcPassiveAbilityConditions != null)
        {
            foreach (var cond in profile.npcPassiveAbilityConditions)
            {
                if (cond == null
                    || cond.conditionType != (int)NpcPassiveAbilityConditionType.HasTargetInRole
                    || cond.role != (int)DetectionZoneBinding.Role.SecondaryAttack
                    || string.IsNullOrWhiteSpace(cond.bindingId))
                {
                    continue;
                }

                if (enabledNpcAbilities.ContainsKey(cond.bindingId))
                {
                    reason = $"存在启用的被动条件 HasTargetInRole(role=SecondaryAttack)：bindingId={cond.bindingId}";
                    return true;
                }
            }
        }

        // 规则3：被动能力目标为 CurrentTarget 且没有任何 HasTargetInRole 条件时，会回退到 triggerRole 找目标
        var hasAnyHasTargetInRole = new HashSet<string>();
        if (profile.npcPassiveAbilityConditions != null)
        {
            foreach (var cond in profile.npcPassiveAbilityConditions)
            {
                if (cond == null
                    || cond.conditionType != (int)NpcPassiveAbilityConditionType.HasTargetInRole
                    || string.IsNullOrWhiteSpace(cond.bindingId))
                {
                    continue;
                }
                hasAnyHasTargetInRole.Add(cond.bindingId);
            }
        }

        if (profile.npcPassiveAbilityBindings != null)
        {
            foreach (var pb in profile.npcPassiveAbilityBindings)
            {
                if (pb == null || string.IsNullOrWhiteSpace(pb.bindingId))
                {
                    continue;
                }

                if (pb.targetMode != (int)NpcPassiveAbilityTargetMode.CurrentTarget)
                {
                    continue;
                }

                if (hasAnyHasTargetInRole.Contains(pb.bindingId))
                {
                    continue;
                }

                if (!enabledNpcAbilities.TryGetValue(pb.bindingId, out var ability))
                {
                    continue;
                }

                if (ability.triggerRole == 1)
                {
                    reason = $"被动能力 targetMode=CurrentTarget 且无 HasTargetInRole 条件，会回退到 triggerRole=SecondaryAttack：bindingId={pb.bindingId}";
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryGetEnemyAbilityKind(string abilityId, out AbilityKind kind)
    {
        kind = default;

        if (string.IsNullOrWhiteSpace(abilityId))
        {
            return false;
        }

        EnsureEnemyAbilityCatalogCache();
        if (_enemyAbilityKindByIdCache == null)
        {
            return false;
        }

        return _enemyAbilityKindByIdCache.TryGetValue(abilityId, out kind);
    }

    private static void EnsureEnemyAbilityCatalogCache()
    {
        if (_enemyAbilityKindByIdCache != null)
        {
            return;
        }

        _enemyAbilityCatalogCache = Resources.Load<AbilityCatalog>(EnemyAbilityCatalogResourcePath);
        if (_enemyAbilityCatalogCache == null)
        {
            _enemyAbilityCatalogCache = AssetDatabase.LoadAssetAtPath<AbilityCatalog>(EnemyAbilityCatalogAssetPath);
        }

        _enemyAbilityKindByIdCache = new Dictionary<string, AbilityKind>();
        if (_enemyAbilityCatalogCache == null || _enemyAbilityCatalogCache.entries == null)
        {
            return;
        }

        foreach (var entry in _enemyAbilityCatalogCache.entries)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.id))
            {
                continue;
            }

            _enemyAbilityKindByIdCache[entry.id] = entry.kind;
        }
    }

    private void ValidateFirePoint(Transform root)
    {
        var firePoint = FindDescendant(root, "FirePoint");
        if (firePoint == null)
        {
            Add(
                Severity.Warning,
                "缺少 FirePoint",
                "未找到名为 'FirePoint' 的子物体。缺失时投射物可能回退到根节点发射。",
                "建议在根下创建空物体 FirePoint 作为发射点。",
                root);
        }
    }

    private void ValidateOptionalZoneEvents(Transform root)
    {
        var cliff = FindDescendant(root, "DZ_Cliff");
        if (cliff != null)
        {
            var dz = cliff.GetComponent<DetectionZone>();
            if (dz != null && dz.NoColliderRemain != null && dz.NoColliderRemain.GetPersistentEventCount() == 0)
            {
                Add(
                    Severity.Warning,
                    "DZ_Cliff 未绑定 NoColliderRemain 事件",
                    "存在 DZ_Cliff 但未配置持久化回调，可能导致悬崖检测无效。",
                    "建议把 NoColliderRemain 绑定到敌人脚本的 OnCliffDetected()（或等价方法）。",
                    cliff);
            }
        }

        var wall = FindDescendant(root, "DZ_Wall");
        if (wall != null)
        {
            var dz = wall.GetComponent<DetectionZone>();
            if (dz != null && dz.OnTargetEnter != null && dz.OnTargetEnter.GetPersistentEventCount() == 0)
            {
                Add(
                    Severity.Warning,
                    "DZ_Wall 未绑定 OnTargetEnter 事件",
                    "存在 DZ_Wall 但未配置持久化回调，可能导致墙检测无效。",
                    "建议把 OnTargetEnter 绑定到敌人脚本的 OnWallDetected()（或等价方法）。",
                    wall);
            }
        }
    }

    private void ValidateLayers(GameObject root, EnemyAgentBase agent)
    {
        // Layer 名称以项目配置为准：这里做建议级提示，避免强绑定数字。
        string rootLayerName = LayerMask.LayerToName(root.layer);
        if (!string.IsNullOrEmpty(rootLayerName) && rootLayerName != "Enemy")
        {
            Add(
                Severity.Warning,
                "根对象 Layer 不符合约定",
                $"当前 Layer='{rootLayerName}'，建议为 'Enemy'。",
                "建议按约定设置 Layer，减少碰撞/过滤类问题。",
                root);
        }

        var primaryZone = agent.GetZone(DetectionZoneBinding.Role.PrimaryAttack);
        if (primaryZone != null)
        {
            string layerName = LayerMask.LayerToName(primaryZone.gameObject.layer);
            if (!string.IsNullOrEmpty(layerName) && layerName != "EnemyHitBox")
            {
                Add(
                    Severity.Suggestion,
                    "PrimaryAttack 检测区 Layer 不符合约定",
                    $"当前 Layer='{layerName}'，建议为 'EnemyHitBox'。",
                    "建议按约定设置检测区 Layer。",
                    primaryZone);
            }
        }

        var secondaryZone = agent.GetZone(DetectionZoneBinding.Role.SecondaryAttack);
        if (secondaryZone != null)
        {
            string layerName = LayerMask.LayerToName(secondaryZone.gameObject.layer);
            if (!string.IsNullOrEmpty(layerName) && layerName != "EnemyHitBox")
            {
                Add(
                    Severity.Suggestion,
                    "SecondaryAttack 检测区 Layer 不符合约定",
                    $"当前 Layer='{layerName}'，建议为 'EnemyHitBox'。",
                    "建议按约定设置检测区 Layer。",
                    secondaryZone);
            }
        }
    }

    private void ValidateOptionalPreplacedComponents(GameObject root)
    {
        if (root.GetComponent<NpcAbilityController>() == null)
        {
            Add(
                Severity.Suggestion,
                "未预置 NpcAbilityController",
                "运行时会自动补挂，但预置更利于调试与 Inspector 可见性。",
                "建议在 Prefab 上预置该组件（可选）。",
                root);
        }

        if (root.GetComponent<StatusEffectController>() == null)
        {
            Add(
                Severity.Suggestion,
                "未预置 StatusEffectController",
                "运行时会自动补挂，但预置更利于调试。",
                "建议在 Prefab 上预置该组件（可选）。",
                root);
        }

        if (root.GetComponent<StatModifierLayer>() == null)
        {
            Add(
                Severity.Suggestion,
                "未预置 StatModifierLayer",
                "运行时会自动补挂，但预置更利于调试。",
                "建议在 Prefab 上预置该组件（可选）。",
                root);
        }
    }

    private void RequireComponent<T>(GameObject root, string label, Severity severity) where T : Component
    {
        if (root.GetComponent<T>() == null)
        {
            Add(
                severity,
                $"缺少 {label}",
                $"根对象 '{root.name}' 未找到组件 {typeof(T).Name}。",
                "请按模板补齐该组件。",
                root);
        }
    }

    private static Transform FindDescendant(Transform root, string childName)
    {
        if (root == null || string.IsNullOrWhiteSpace(childName))
        {
            return null;
        }

        if (string.Equals(root.name, childName, StringComparison.Ordinal))
        {
            return root;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindDescendant(root.GetChild(i), childName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private void Add(Severity severity, string title, string details, string fix, UnityEngine.Object context)
    {
        _issues.Add(new Issue
        {
            severity = severity,
            title = title ?? string.Empty,
            details = details ?? string.Empty,
            fix = fix ?? string.Empty,
            context = context,
        });
    }

    private void FinalizeReport()
    {
        _hasReport = true;
        _reportMarkdown = BuildMarkdown();
    }

    private string BuildMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# prefab检测报告");
        sb.AppendLine();
        sb.AppendLine($"- Prefab: `{_prefabPath}`");
        sb.AppendLine($"- 时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        int blockerCount = _issues.Count(i => i.severity == Severity.Blocker);
        int warningCount = _issues.Count(i => i.severity == Severity.Warning);
        int suggestionCount = _issues.Count(i => i.severity == Severity.Suggestion);

        sb.AppendLine("## 概览");
        sb.AppendLine($"- 阻塞: {blockerCount}");
        sb.AppendLine($"- 警告: {warningCount}");
        sb.AppendLine($"- 建议: {suggestionCount}");
        sb.AppendLine();

        sb.AppendLine("## 详情");
        if (_issues.Count == 0)
        {
            sb.AppendLine("- 未发现问题。");
            return sb.ToString();
        }

        foreach (var issue in _issues)
        {
            sb.AppendLine($"- [{GetSeverityName(issue.severity)}] {issue.title}");

            if (!string.IsNullOrWhiteSpace(issue.details))
            {
                sb.AppendLine($"  - 说明：{issue.details}");
            }

            if (!string.IsNullOrWhiteSpace(issue.fix))
            {
                sb.AppendLine($"  - 建议：{issue.fix}");
            }
        }

        return sb.ToString();
    }

    private static string GetSeverityName(Severity severity)
    {
        switch (severity)
        {
            case Severity.Blocker:
                return "阻塞";
            case Severity.Warning:
                return "警告";
            case Severity.Suggestion:
                return "建议";
            default:
                return "未知";
        }
    }

    private void DrawIssue(Issue issue)
    {
        if (issue == null)
        {
            return;
        }

        using (new EditorGUILayout.VerticalScope("box"))
        {
            EditorGUILayout.LabelField($"{GetSeverityName(issue.severity)}：{issue.title}", EditorStyles.boldLabel);

            if (!string.IsNullOrWhiteSpace(issue.details))
            {
                EditorGUILayout.LabelField(issue.details, EditorStyles.wordWrappedLabel);
            }

            if (!string.IsNullOrWhiteSpace(issue.fix))
            {
                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField("建议修复：", EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField(issue.fix, EditorStyles.wordWrappedLabel);
            }

            if (issue.context != null)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("定位对象", GUILayout.Width(90)))
                    {
                        EditorGUIUtility.PingObject(issue.context);
                        Selection.activeObject = issue.context;
                    }
                }
            }
        }
    }

    private void ExportMarkdown()
    {
        string defaultName = string.IsNullOrWhiteSpace(_prefabPath)
            ? "prefab_report.md"
            : Path.GetFileNameWithoutExtension(_prefabPath) + "_prefab_report.md";

        string savePath = EditorUtility.SaveFilePanel("导出 Markdown 报告", "", defaultName, "md");
        if (string.IsNullOrWhiteSpace(savePath))
        {
            return;
        }

        try
        {
            File.WriteAllText(savePath, _reportMarkdown ?? string.Empty, new UTF8Encoding(true));
            EditorUtility.RevealInFinder(savePath);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[PrefabValidationWindow] 导出失败：{ex.Message}");
        }
    }
}
