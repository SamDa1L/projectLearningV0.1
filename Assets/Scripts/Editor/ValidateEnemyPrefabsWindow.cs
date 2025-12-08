using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using System.Linq;

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
    private bool showDetailedLog = true;

    // ===== 统计数据 =====
    private int prefabCount = 0;
    private int validPrefabCount = 0;
    private int sceneEnemyCount = 0;
    private int validSceneEnemyCount = 0;
    private List<string> issuesList = new List<string>();

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

        Debug.Log("\n========== Stage1 敌人迁移验证开始 ==========\n");

        if (validatePrefabs)
        {
            ValidatePrefabs();
        }

        if (validateScenes)
        {
            ValidateScenes();
        }

        Debug.Log("\n========== 验证完成 ==========\n");

        if (prefabCount == 0 && sceneEnemyCount == 0)
        {
            Debug.LogWarning("[ValidateEnemyPrefabs] 未找到任何敌人Prefab或实例");
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

        if (issueList.Count > 0)
        {
            problems = string.Join(", ", issueList);
            return false;
        }

        return true;
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
}
