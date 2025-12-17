using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Animations;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using CastleDB.Runtime;

/// <summary>
/// 敌人Prefab与场景验证工具（v0.2 Plan A版本）
/// 双层验证架构：Prefab资源检查 + Scene场景实例检查
///
/// 设计思路（基于Plan A - zoneBindings单一数据源）：
/// - 第1层：扫描Assets/Resources/Prefabs中的所有敌人Prefab
/// - 第2层：加载所有已保存的Scene，检查敌人实例配置
/// - 核心检查项：
///   1. 是否继承EnemyAgentBase
///   2. 是否分配了EnemyTuningProfile
///   3. 是否在zoneBindings中配置了PrimaryAttack检测区（Plan A强制要求）
///   4. zoneBindings中的所有zone引用是否有效（非空）
///   5. 是否有Animator组件
///   6. 是否有Rigidbody2D组件
///   7. 是否有Damageable组件
/// - 防止遗漏Prefab中的配置或场景中的敌人实例
///
/// v0.2改动说明：
/// - 移除了对primaryDetectionZone字段的检查（该字段已删除）
/// - 强制检查zoneBindings必须包含至少一个PrimaryAttack
/// - 新增对zoneBindings配置有效性的验证
///
/// 使用步骤：
/// 1. Tools菜单 → Stage1 → Validate Enemy Prefabs
/// 2. 查看Console输出的验证结果
/// 3. 根据警告修复配置问题（重点：配置zoneBindings）
/// 4. 每次迁移敌人后运行此工具进行验证
/// </summary>
public class ValidateEnemyPrefabsWindow : EditorWindow
{
    // ===== 验证配置 =====
    private bool validatePrefabs = true;
    private bool validateScenes = true;
    private bool validateOldScripts = true;
    private bool validateCastleDbZones = true;  // 2B: 校验 CastleDB 检测区
    private bool showDetailedLog = true;

    // ===== 统计数据 =====
    private int prefabCount = 0;
    private int validPrefabCount = 0;
    private int sceneEnemyCount = 0;
    private int validSceneEnemyCount = 0;
    private int castleDbZoneMismatchCount = 0;  // 2B: CastleDB 检测区不匹配数
    private List<string> issuesList = new List<string>();

    // ===== CastleDB 检测区缓存 =====
    private Dictionary<string, List<DetectionZoneEntry>> _castleDbZonesCache = null;

    // ===== 窗口菜单 =====

    [MenuItem("Tools/Stage1/Validate Enemy Prefabs")]
    public static void ShowValidationWindow()
    {
        GetWindow<ValidateEnemyPrefabsWindow>("Enemy Validation");
    }

    // ===== GUI =====

    private void OnGUI()
    {
        GUILayout.Label("敌人Prefab与Scene验证工具", EditorStyles.boldLabel);
        GUILayout.Space(10);

        // 选项组
        GUILayout.Label("验证选项", EditorStyles.boldLabel);
        validatePrefabs = EditorGUILayout.Toggle("验证Prefab资源", validatePrefabs);
        validateScenes = EditorGUILayout.Toggle("验证Scene场景", validateScenes);
        validateOldScripts = EditorGUILayout.Toggle("检查旧脚本", validateOldScripts);
        validateCastleDbZones = EditorGUILayout.Toggle("校验CastleDB检测区 (2B)", validateCastleDbZones);
        showDetailedLog = EditorGUILayout.Toggle("详细日志", showDetailedLog);

        GUILayout.Space(10);

        if (GUILayout.Button("开始验证", GUILayout.Height(30)))
        {
            PerformValidation();
        }

        GUILayout.Space(10);

        // 统计信息
        GUILayout.Label("验证结果", EditorStyles.boldLabel);
        if (prefabCount > 0)
        {
            GUILayout.Label($"Prefab: {validPrefabCount}/{prefabCount} 通过验证");
        }
        if (sceneEnemyCount > 0)
        {
            GUILayout.Label($"Scene敌人实例: {validSceneEnemyCount}/{sceneEnemyCount} 通过验证");
        }
        if (validateCastleDbZones && castleDbZoneMismatchCount > 0)
        {
            GUILayout.Label($"CastleDB检测区不匹配: {castleDbZoneMismatchCount} 个", EditorStyles.boldLabel);
        }

        // 问题列表
        if (issuesList.Count > 0)
        {
            GUILayout.Space(10);
            GUILayout.Label("检测到的问题", EditorStyles.boldLabel);
            GUILayout.BeginScrollView(Vector2.zero, GUILayout.Height(200));
            foreach (var issue in issuesList)
            {
                GUILayout.Label(issue, EditorStyles.wordWrappedLabel);
            }
            GUILayout.EndScrollView();
        }
    }

    // ===== 核心验证逻辑 =====

    /// <summary>
    /// 执行完整的验证流程
    /// </summary>
    private void PerformValidation()
    {
        issuesList.Clear();
        prefabCount = 0;
        validPrefabCount = 0;
        sceneEnemyCount = 0;
        validSceneEnemyCount = 0;
        castleDbZoneMismatchCount = 0;
        _castleDbZonesCache = null;

        Debug.Log("\n========== Stage1 敌人迁移验证开始 ==========\n");

        // 2B: 加载 CastleDB 检测区数据
        if (validateCastleDbZones)
        {
            LoadCastleDbDetectionZones();
        }

        if (validatePrefabs)
        {
            ValidatePrefabs();
        }

        if (validateScenes)
        {
            ValidateScenes();
        }

        // 2B: 输出检测区校验报告到日志文件
        if (validateCastleDbZones)
        {
            WriteDetectionZoneValidationReport();
        }

        Debug.Log("\n========== 验证完成 ==========\n");

        if (prefabCount == 0 && sceneEnemyCount == 0)
        {
            Debug.LogWarning("[ValidateEnemyPrefabs] 未找到任何敌人Prefab或实例");
        }
    }

    /// <summary>
    /// 2B: 加载 CastleDB 检测区数据
    /// </summary>
    private void LoadCastleDbDetectionZones()
    {
        Debug.Log("\n[0/2] 加载 CastleDB 检测区数据...");

        _castleDbZonesCache = CastleDbImporter.LoadDetectionZonesGroupedByNpcId();

        if (_castleDbZonesCache == null || _castleDbZonesCache.Count == 0)
        {
            Debug.LogWarning("  ⚠ CastleDB 中未找到检测区数据，跳过检测区校验");
            _castleDbZonesCache = new Dictionary<string, List<DetectionZoneEntry>>();
        }
        else
        {
            Debug.Log($"  ✓ 已加载 {_castleDbZonesCache.Count} 个 NPC 的检测区定义");
        }
    }

    /// <summary>
    /// 2B: 输出检测区校验报告到日志文件
    /// </summary>
    private void WriteDetectionZoneValidationReport()
    {
        if (castleDbZoneMismatchCount == 0)
        {
            Debug.Log("\n[检测区校验] 所有 Prefab 的 zoneBindings 与 CastleDB 定义一致 ✓");
            return;
        }

        // 将不匹配信息写入 CastleDbImport.log
        try
        {
            string logDir = "Logs";
            if (!Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir);
            }

            string logPath = Path.Combine(logDir, "CastleDbImport.log");
            var logContent = new System.Text.StringBuilder();

            logContent.AppendLine();
            logContent.AppendLine("════════════════════════════════════════════════════════");
            logContent.AppendLine($"         检测区校验报告 ({System.DateTime.Now:yyyy-MM-dd HH:mm:ss})");
            logContent.AppendLine("════════════════════════════════════════════════════════");
            logContent.AppendLine();
            logContent.AppendLine($"检测到 {castleDbZoneMismatchCount} 个检测区不匹配问题：");
            logContent.AppendLine();

            foreach (var issue in issuesList.Where(i => i.Contains("CastleDB检测区")))
            {
                logContent.AppendLine($"• {issue}");
            }

            logContent.AppendLine();
            logContent.AppendLine("════════════════════════════════════════════════════════");

            File.AppendAllText(logPath, logContent.ToString());
            Debug.Log($"[检测区校验] 校验报告已追加到 {logPath}");
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[检测区校验] 写入日志失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 第1层：验证所有敌人Prefab
    /// </summary>
    private void ValidatePrefabs()
    {
        Debug.Log("\n[1/2] 扫描Prefab资源...");

        var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Resources/Prefabs" });
        prefabCount = 0;
        validPrefabCount = 0;
        List<string> prefabIssues = new List<string>();

        foreach (string guid in prefabGuids)
        {
            string prefabPath = AssetDatabase.GUIDToAssetPath(guid);

            // 简单的敌人Prefab检查（通过命名规范或特定路径）
            if (!prefabPath.Contains("Enemy") && !prefabPath.Contains("enemy"))
                continue;

            prefabCount++;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            if (prefab == null)
            {
                prefabIssues.Add($"  ✗ {prefabPath}: 无法加载Prefab");
                continue;
            }

            bool isValid = ValidatePrefabIntegrity(prefab, prefabPath, out string problems);

            if (isValid)
            {
                validPrefabCount++;
                if (showDetailedLog)
                    Debug.Log($"  ✓ {prefabPath} 通过检查");
            }
            else
            {
                prefabIssues.Add($"  ✗ {prefabPath}: {problems}");
            }
        }

        Debug.Log($"\n[Prefab结果] {validPrefabCount}/{prefabCount} Prefab通过验证");
        if (prefabIssues.Count > 0)
        {
            Debug.LogWarning("检测到Prefab问题:");
            foreach (var issue in prefabIssues)
            {
                Debug.LogWarning(issue);
                issuesList.Add(issue);
            }
        }
    }

    /// <summary>
    /// 第2层：验证所有Scene中的敌人实例
    /// </summary>
    private void ValidateScenes()
    {
        Debug.Log("\n[2/2] 扫描Scene场景中的敌人实例...");

        // 获取项目中所有scene文件
        var sceneGuids = AssetDatabase.FindAssets("t:Scene");
        if (sceneGuids.Length == 0)
        {
            Debug.LogWarning("  ⚠ 项目中未找到任何Scene文件");
            return;
        }

        sceneEnemyCount = 0;
        validSceneEnemyCount = 0;
        List<string> sceneIssues = new List<string>();

        foreach (string guid in sceneGuids)
        {
            string scenePath = AssetDatabase.GUIDToAssetPath(guid);

            // 只处理项目自身的场景，过滤掉包内的模板场景
            if (!scenePath.Contains("Assets/Scenes/NPCTestScenes"))
                continue;

            // 只读模式加载场景（不改变当前编辑器状态）
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            if (!scene.IsValid())
            {
                sceneIssues.Add($"  ⚠ {scenePath}: 场景无效或损坏");
                continue;
            }

            // 扫描此场景中的所有EnemyAgentBase实例
            var sceneEnemies = FindObjectsOfType<EnemyAgentBase>();

            foreach (var enemy in sceneEnemies)
            {
                sceneEnemyCount++;

                bool isValid = ValidateSceneEnemyIntegrity(enemy, scenePath, out string problems);

                if (isValid)
                {
                    validSceneEnemyCount++;
                    if (showDetailedLog)
                        Debug.Log($"    ✓ {scenePath} > {enemy.gameObject.name} ({enemy.GetType().Name}) 配置正确");
                }
                else
                {
                    sceneIssues.Add($"    ✗ {scenePath} > {enemy.gameObject.name}: {problems}");
                }
            }

            // 卸载临时加载的场景
            EditorSceneManager.CloseScene(scene, true);
        }

        Debug.Log($"\n[Scene结果] {validSceneEnemyCount}/{sceneEnemyCount} 场景敌人实例通过验证");
        if (sceneIssues.Count > 0)
        {
            Debug.LogWarning("检测到Scene敌人配置问题:");
            foreach (var issue in sceneIssues)
            {
                Debug.LogWarning(issue);
                issuesList.Add(issue);
            }
        }
    }

    // ===== 验证逻辑 =====

    /// <summary>
    /// 验证单个Prefab的完整性（Plan A版本）
    ///
    /// Plan A核心验证：
    /// - zoneBindings必须包含至少一个PrimaryAttack
    /// - zoneBindings中的所有zone引用必须非空
    /// - 不再检查primaryDetectionZone字段（已删除）
    /// </summary>
    private bool ValidatePrefabIntegrity(GameObject prefab, string prefabPath, out string problems)
    {
        problems = "";
        List<string> issueList = new List<string>();

        // 检查1：是否有EnemyAgentBase脚本
        var baseEnemy = prefab.GetComponent<EnemyAgentBase>();
        if (baseEnemy == null)
        {
            issueList.Add("Missing EnemyAgentBase");
            problems = string.Join(", ", issueList);
            return false;
        }

        // 检查是否是遗留脚本（旧Knight/FlyingEye等）
        string scriptType = baseEnemy.GetType().Name;
        if (validateOldScripts && (scriptType == "Knight" || scriptType == "FlyingEye"))
        {
            if (!typeof(EnemyAgentBase).IsAssignableFrom(baseEnemy.GetType()) ||
                baseEnemy.GetType() == typeof(EnemyAgentBase))
            {
                issueList.Add($"仍使用旧脚本 ({scriptType})");
            }
        }

        // 检查2：是否分配了EnemyTuningProfile
        var serializedObject = new SerializedObject(baseEnemy);
        var tuningProfileProp = serializedObject.FindProperty("tuningProfile");
        if (tuningProfileProp == null || tuningProfileProp.objectReferenceValue == null)
        {
            issueList.Add("Missing TuningProfile");
        }

        // 检查3：Plan A强制要求 - zoneBindings必须包含PrimaryAttack且所有zone有效
        var zoneBindingsProp = serializedObject.FindProperty("zoneBindings");
        if (zoneBindingsProp == null || zoneBindingsProp.arraySize == 0)
        {
            issueList.Add("zoneBindings为空（Plan A强制要求：必须在Inspector中配置检测区）");
        }
        else
        {
            // 检查是否有PrimaryAttack binding
            bool hasPrimaryAttack = false;
            int zoneCount = zoneBindingsProp.arraySize;

            for (int i = 0; i < zoneCount; i++)
            {
                var bindingElement = zoneBindingsProp.GetArrayElementAtIndex(i);
                var roleField = bindingElement.FindPropertyRelative("role");
                var zoneField = bindingElement.FindPropertyRelative("zone");

                // 检查zone是否为空
                if (zoneField.objectReferenceValue == null)
                {
                    issueList.Add($"zoneBindings[{i}]的zone字段为空（请拖拽DetectionZone组件）");
                }

                // 检查是否有PrimaryAttack
                if (roleField.enumValueIndex == (int)DetectionZoneBinding.Role.PrimaryAttack)
                {
                    hasPrimaryAttack = true;
                }
            }

            if (!hasPrimaryAttack)
            {
                issueList.Add("zoneBindings中未找到PrimaryAttack（Plan A强制要求至少一个PrimaryAttack用于GetDetectedTargets()）");
            }
        }

        // 检查4：检查根物体是否有DetectionZone（应该迁至子物体）
        var rootDetectionZone = prefab.GetComponent<DetectionZone>();
        if (rootDetectionZone != null)
        {
            issueList.Add("根物体包含DetectionZone（应该迁至子物体如'DZ_Attack'，配置到zoneBindings）");
        }

        // 检查5：列出所有找到的检测区（仅详细日志）
        var allChildZones = prefab.GetComponentsInChildren<DetectionZone>();
        if (allChildZones.Length > 0 && showDetailedLog)
        {
            var zoneNames = string.Join("、", allChildZones.Select(z => $"'{z.gameObject.name}'"));
            Debug.Log($"  ℹ {prefabPath}：可用的检测区包括：{zoneNames}");
        }

        // 检查6：是否有Animator
        var animator = prefab.GetComponent<Animator>();
        if (animator == null)
        {
            issueList.Add("Missing Animator");
        }
        else
        {
            // 检查6.1：Animator Controller 是否包含 Profile 中配置的 animationTrigger
            if (tuningProfileProp != null && tuningProfileProp.objectReferenceValue != null)
            {
                var profile = tuningProfileProp.objectReferenceValue as EnemyTuningProfile;
                if (profile != null && !string.IsNullOrEmpty(profile.animationTrigger))
                {
                    // 检查 Animator Controller 是否包含该 Trigger 参数
                    if (animator.runtimeAnimatorController != null)
                    {
                        if (!HasTriggerParameterInController(animator.runtimeAnimatorController, profile.animationTrigger))
                        {
                            issueList.Add($"Animator Controller 缺少 Trigger 参数 '{profile.animationTrigger}'（Profile 中配置的 animationTrigger）");
                        }
                    }
                }
            }
        }

        // 检查7：是否有Rigidbody2D
        var rigidbody2d = prefab.GetComponent<Rigidbody2D>();
        if (rigidbody2d == null)
        {
            issueList.Add("Missing Rigidbody2D");
        }

        // 检查8：是否有Damageable
        var damageable = prefab.GetComponent<Damageable>();
        if (damageable == null)
        {
            issueList.Add("Missing Damageable");
        }

        // 检查9 (2B)：CastleDB 检测区与 zoneBindings 一致性校验
        if (validateCastleDbZones && tuningProfileProp != null && tuningProfileProp.objectReferenceValue != null)
        {
            ValidateCastleDbDetectionZones(prefab, zoneBindingsProp, tuningProfileProp, prefabPath, issueList);
        }

        if (issueList.Count > 0)
        {
            problems = string.Join(", ", issueList);
            return false;
        }

        return true;
    }

    /// <summary>
    /// 2B: 校验 CastleDB 检测区与 Prefab zoneBindings 的一致性
    ///
    /// 校验逻辑：
    /// 1. 从 Profile 文件名提取 npcId（Profile_M_Knight.asset → M_Knight）
    /// 2. 查找 CastleDB 中该 NPC 的所有检测区定义
    /// 3. 对比 Prefab 的 zoneBindings：
    ///    - CastleDB 定义的 role+childId 是否都存在于 zoneBindings
    ///    - zoneBindings 中的 zone 子物体名是否与 CastleDB childId 匹配
    /// </summary>
    private void ValidateCastleDbDetectionZones(
        GameObject prefab,
        SerializedProperty zoneBindingsProp,
        SerializedProperty tuningProfileProp,
        string prefabPath,
        List<string> issueList)
    {
        if (_castleDbZonesCache == null || _castleDbZonesCache.Count == 0)
        {
            return;
        }

        // 从 Profile 路径提取 npcId
        var profile = tuningProfileProp.objectReferenceValue as EnemyTuningProfile;
        if (profile == null)
        {
            return;
        }

        string profilePath = AssetDatabase.GetAssetPath(profile);
        string profileFileName = Path.GetFileNameWithoutExtension(profilePath);
        string npcId = profileFileName.StartsWith("Profile_") ? profileFileName.Substring("Profile_".Length) : null;

        if (string.IsNullOrEmpty(npcId))
        {
            if (showDetailedLog)
            {
                Debug.Log($"  ℹ {prefabPath}: 无法从 Profile 文件名提取 npcId，跳过 CastleDB 检测区校验");
            }
            return;
        }

        // 查找 CastleDB 中该 NPC 的检测区定义
        if (!_castleDbZonesCache.TryGetValue(npcId, out var castleDbZones) || castleDbZones.Count == 0)
        {
            if (showDetailedLog)
            {
                Debug.Log($"  ℹ {prefabPath}: CastleDB 中未找到 NPC '{npcId}' 的检测区定义");
            }
            return;
        }

        // 构建 Prefab 的 zoneBindings 映射：role → (childName, zone)
        var prefabBindings = new Dictionary<DetectionZoneBinding.Role, (string childName, DetectionZone zone)>();

        if (zoneBindingsProp != null && zoneBindingsProp.arraySize > 0)
        {
            for (int i = 0; i < zoneBindingsProp.arraySize; i++)
            {
                var bindingElement = zoneBindingsProp.GetArrayElementAtIndex(i);
                var roleField = bindingElement.FindPropertyRelative("role");
                var zoneField = bindingElement.FindPropertyRelative("zone");

                var role = (DetectionZoneBinding.Role)roleField.enumValueIndex;
                var zone = zoneField.objectReferenceValue as DetectionZone;
                string childName = zone != null ? zone.gameObject.name : null;

                // 如果同一 role 有多个 binding，取第一个（或可以扩展为列表）
                if (!prefabBindings.ContainsKey(role))
                {
                    prefabBindings[role] = (childName, zone);
                }
            }
        }

        // 对比：CastleDB 定义 vs Prefab zoneBindings
        foreach (var castleDbZone in castleDbZones)
        {
            var expectedRole = RoleIndexToBindingRole(castleDbZone.role);
            string expectedChildId = castleDbZone.childId;

            if (!prefabBindings.TryGetValue(expectedRole, out var prefabBinding))
            {
                // Prefab 中缺少该 role 的 binding
                string issue = $"CastleDB检测区不匹配: NPC '{npcId}' 缺少 role={expectedRole} 的 zoneBinding（CastleDB 要求 childId='{expectedChildId}'）";
                issueList.Add(issue);
                castleDbZoneMismatchCount++;
                Debug.LogWarning($"  ✗ {prefabPath}: {issue}");
            }
            else if (prefabBinding.zone == null)
            {
                // zoneBinding 存在但 zone 引用为空
                string issue = $"CastleDB检测区不匹配: NPC '{npcId}' 的 role={expectedRole} binding 的 zone 引用为空（CastleDB 要求 childId='{expectedChildId}'）";
                issueList.Add(issue);
                castleDbZoneMismatchCount++;
                Debug.LogWarning($"  ✗ {prefabPath}: {issue}");
            }
            else if (!string.IsNullOrEmpty(expectedChildId) && prefabBinding.childName != expectedChildId)
            {
                // childId 不匹配
                string issue = $"CastleDB检测区不匹配: NPC '{npcId}' 的 role={expectedRole} 子物体名不匹配（Prefab: '{prefabBinding.childName}', CastleDB: '{expectedChildId}'）";
                issueList.Add(issue);
                castleDbZoneMismatchCount++;
                Debug.LogWarning($"  ✗ {prefabPath}: {issue}");
            }
            else if (showDetailedLog)
            {
                Debug.Log($"  ✓ {prefabPath}: CastleDB 检测区 role={expectedRole} childId='{expectedChildId}' 匹配成功");
            }
        }
    }

    /// <summary>
    /// 将 CastleDB DetectionZone.role (int) 映射为 Prefab zoneBindings 使用的 DetectionZoneBinding.Role
    /// 使用统一的 DetectionZoneRoleMapper 避免多处 switch 漂移（P1-3.4）
    /// </summary>
    private static DetectionZoneBinding.Role RoleIndexToBindingRole(int roleIndex)
    {
        return DetectionZoneRoleMapper.ToBindingRole(roleIndex);
    }

    /// <summary>
    /// 验证Scene中单个敌人实例的完整性（Plan A版本）
    ///
    /// Plan A核心验证：
    /// - zoneBindings必须包含至少一个PrimaryAttack
    /// - zoneBindings中的所有zone引用必须非空
    /// - 不再检查primaryDetectionZone字段（已删除）
    /// </summary>
    private bool ValidateSceneEnemyIntegrity(EnemyAgentBase enemy, string scenePath, out string problems)
    {
        problems = "";
        List<string> issueList = new List<string>();

        // 检查1：是否分配了TuningProfile
        var serializedObject = new SerializedObject(enemy);
        var tuningProfileProp = serializedObject.FindProperty("tuningProfile");
        if (tuningProfileProp == null || tuningProfileProp.objectReferenceValue == null)
        {
            issueList.Add("Missing TuningProfile");
        }

        // 检查2：Plan A强制要求 - zoneBindings必须包含PrimaryAttack且所有zone有效
        var zoneBindingsProp = serializedObject.FindProperty("zoneBindings");
        if (zoneBindingsProp == null || zoneBindingsProp.arraySize == 0)
        {
            issueList.Add("zoneBindings为空（Plan A强制要求：必须配置检测区）");
        }
        else
        {
            // 检查是否有PrimaryAttack binding
            bool hasPrimaryAttack = false;
            int zoneCount = zoneBindingsProp.arraySize;

            for (int i = 0; i < zoneCount; i++)
            {
                var bindingElement = zoneBindingsProp.GetArrayElementAtIndex(i);
                var roleField = bindingElement.FindPropertyRelative("role");
                var zoneField = bindingElement.FindPropertyRelative("zone");

                // 检查zone是否为空
                if (zoneField.objectReferenceValue == null)
                {
                    issueList.Add($"zoneBindings[{i}]的zone字段为空");
                }

                // 检查是否有PrimaryAttack
                if (roleField.enumValueIndex == (int)DetectionZoneBinding.Role.PrimaryAttack)
                {
                    hasPrimaryAttack = true;
                }
            }

            if (!hasPrimaryAttack)
            {
                issueList.Add("zoneBindings中未找到PrimaryAttack");
            }
        }

        // 检查3：是否是LegacyEnemyAdapter且尚未完全迁移
        var legacyAdapter = enemy as LegacyEnemyAdapter;
        if (legacyAdapter != null)
        {
            string migrationStatus = legacyAdapter.GetMigrationStatus();
            if (migrationStatus != "完全迁移")
            {
                if (showDetailedLog)
                    Debug.Log($"    ℹ {enemy.gameObject.name}: 迁移进度 - {migrationStatus}");
            }
        }

        // 检查4：是否有Animator
        var animator = enemy.GetComponent<Animator>();
        if (animator == null)
        {
            issueList.Add("Missing Animator");
        }
        else
        {
            // 检查4.1：Animator Controller 是否包含 Profile 中配置的 animationTrigger
            if (tuningProfileProp != null && tuningProfileProp.objectReferenceValue != null)
            {
            var profile = tuningProfileProp.objectReferenceValue as EnemyTuningProfile;
            if (profile != null && !string.IsNullOrEmpty(profile.animationTrigger))
            {
                // 检查 Animator Controller 是否包含该 Trigger 参数
                if (animator.runtimeAnimatorController != null)
                {
                    if (!HasTriggerParameterInController(animator.runtimeAnimatorController, profile.animationTrigger))
                    {
                        issueList.Add($"Animator Controller 缺少 Trigger 参数 '{profile.animationTrigger}'（Profile 中配置的 animationTrigger）");
                    }
                }
            }
        }
        }

        // 检查5：是否有Rigidbody2D
        var rigidbody2d = enemy.GetComponent<Rigidbody2D>();
        if (rigidbody2d == null)
        {
            issueList.Add("Missing Rigidbody2D");
        }

        if (issueList.Count > 0)
        {
            problems = string.Join(", ", issueList);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Editor 校验：检查 Controller 资产是否包含指定 Trigger 参数。
    ///
    /// 注意：在 Prefab 资源对象上读取 Animator.parameters 可能拿到空/缓存数据，导致误报；
    /// 因此这里直接读取 AnimatorController 资产的 parameters。
    /// </summary>
    private static bool HasTriggerParameterInController(RuntimeAnimatorController controller, string triggerName)
    {
        if (controller == null)
        {
            return true;
        }

        triggerName = triggerName?.Trim();
        if (string.IsNullOrEmpty(triggerName))
        {
            return true;
        }

        controller = UnwrapOverrideController(controller);
        if (controller == null)
        {
            return true;
        }

        var animatorController = controller as AnimatorController;
        if (animatorController == null)
        {
            // 兜底：尝试通过 AssetDatabase 加载 AnimatorController（兼容少数非直接引用场景）
            string controllerPath = AssetDatabase.GetAssetPath(controller);
            if (!string.IsNullOrEmpty(controllerPath))
            {
                animatorController = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            }
        }

        if (animatorController == null)
        {
            // 无法解析 Controller 类型时不报错，避免误报（例如自定义/不可解析的 RuntimeAnimatorController）。
            return true;
        }

        foreach (var parameter in animatorController.parameters)
        {
            if (parameter.type == AnimatorControllerParameterType.Trigger && parameter.name == triggerName)
            {
                return true;
            }
        }

        return false;
    }

    private static RuntimeAnimatorController UnwrapOverrideController(RuntimeAnimatorController controller)
    {
        while (controller is AnimatorOverrideController overrideController)
        {
            controller = overrideController.runtimeAnimatorController;
        }

        return controller;
    }
}
