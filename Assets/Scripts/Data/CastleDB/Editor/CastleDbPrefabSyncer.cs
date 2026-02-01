using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CastleDB.Runtime;
using CastleDB.Editor;

/// <summary>
/// CastleDB Prefab 同步工具（P0-3.1 + P0-3.2）
///
/// 功能：
/// - 读取 CastleDB 的 DetectionZone 定义
/// - 自动对齐 NPC Prefab 的 zoneBindings（role + zone 引用）
/// - 同步前自动备份 Prefab
/// - 提供回滚功能
///
/// 设计说明：
/// - 不修改 Collider 的形状/尺寸/偏移（仍以 Prefab 为准）
/// - childId 与 Prefab 子物体 name 必须完全匹配（大小写敏感）
/// - 使用保守对齐策略：更新已存在的 role，新增缺少的 role，不删除多余的 role
/// </summary>
public class CastleDbPrefabSyncer : EditorWindow
{
    // ===== 常量 =====
    private const string PREFAB_SEARCH_PATH = "Assets/Resources/Prefabs/Enemy";
    private const string BACKUP_ROOT_DIR = "Logs/CastleDBSync/Backups";
    private const string SYNC_LOG_FILE = "Logs/CastleDbSync.log";

    // ===== 同步选项 =====
    private bool dryRun = true;
    private bool syncAllNpcs = true;
    private bool strictMode = false;  // 严格模式：删除多余的 role binding
    private bool autoValidate = true; // 同步后自动校验

    // ===== 运行时状态 =====
    private Vector2 scrollPosition;
    private List<SyncResult> syncResults = new List<SyncResult>();
    private bool isSyncing = false;

    // ===== 同步结果数据结构 =====
    private class SyncResult
    {
        public string npcId;
        public string prefabPath;
        public bool success;
        public string message;
        public List<string> changes = new List<string>();
    }

    // ===== 窗口菜单 =====

    [MenuItem("Tools/CastleDB/Sync NPC Prefabs")]
    public static void ShowWindow()
    {
        var window = GetWindow<CastleDbPrefabSyncer>("Sync NPC Prefabs");
        window.minSize = new Vector2(500, 400);
    }

    [MenuItem("Tools/CastleDB/Revert Last Sync")]
    public static void RevertLastSync()
    {
        RevertLastSyncInternal();
    }

    // ===== GUI =====

    private void OnGUI()
    {
        GUILayout.Label("CastleDB Prefab 同步工具 (2B)", EditorStyles.boldLabel);
        GUILayout.Space(10);

        // 选项组
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("同步选项", EditorStyles.boldLabel);
        dryRun = EditorGUILayout.Toggle("Dry Run（仅预览，不修改）", dryRun);
        syncAllNpcs = EditorGUILayout.Toggle("同步所有 NPC", syncAllNpcs);
        strictMode = EditorGUILayout.Toggle("严格模式（删除多余 role）", strictMode);
        autoValidate = EditorGUILayout.Toggle("同步后自动校验", autoValidate);
        EditorGUILayout.EndVertical();

        GUILayout.Space(10);

        // 操作按钮
        EditorGUI.BeginDisabledGroup(isSyncing);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button(dryRun ? "预览同步差异" : "执行同步", GUILayout.Height(30)))
        {
            ExecuteSync();
        }
        if (GUILayout.Button("回滚上次同步", GUILayout.Height(30)))
        {
            RevertLastSyncInternal();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUI.EndDisabledGroup();

        GUILayout.Space(10);

        // 结果显示
        if (syncResults.Count > 0)
        {
            GUILayout.Label("同步结果", EditorStyles.boldLabel);
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(250));
            foreach (var result in syncResults)
            {
                string icon = result.success ? "✓" : "✗";
                string color = result.success ? "green" : "red";
                EditorGUILayout.LabelField($"{icon} {result.npcId}: {result.message}");
                foreach (var change in result.changes)
                {
                    EditorGUILayout.LabelField($"    • {change}", EditorStyles.miniLabel);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        // 帮助信息
        GUILayout.FlexibleSpace();
        EditorGUILayout.HelpBox(
            "同步流程：\n" +
            "1. 从 CastleDB 读取 DetectionZone 定义\n" +
            "2. 匹配 NPC Prefab（通过 prefabName 或命名规则）\n" +
            "3. 对齐 zoneBindings（role + zone 引用）\n" +
            "4. 备份并保存 Prefab",
            MessageType.Info);
    }

    // ===== 核心同步逻辑 =====

    private void ExecuteSync()
    {
        isSyncing = true;
        syncResults.Clear();

        try
        {
            Debug.Log("\n========== CastleDB Prefab 同步开始 ==========\n");

            // 1. 加载 CastleDB 数据
            var castleDbZones = CastleDbImporter.LoadDetectionZonesGroupedByNpcId();
            if (castleDbZones == null || castleDbZones.Count == 0)
            {
                Debug.LogWarning("[CastleDbPrefabSyncer] CastleDB 中未找到检测区数据");
                return;
            }

            // 2. 加载 NPC 数据（用于获取 prefabName）
            var npcData = LoadNpcData();

            // 3. 备份现有 Prefab（非 Dry Run 时）
            string backupDir = null;
            if (!dryRun)
            {
                backupDir = BackupPrefabs();
                if (backupDir == null)
                {
                    Debug.LogError("[CastleDbPrefabSyncer] 备份失败，同步已取消");
                    return;
                }
            }

            // 4. 遍历每个 NPC 进行同步
            foreach (var kvp in castleDbZones)
            {
                string npcId = kvp.Key;
                var zones = kvp.Value;

                var result = SyncNpcPrefab(npcId, zones, npcData);
                syncResults.Add(result);
            }

            // 5. 写入同步日志
            WriteSyncLog(backupDir);

            // 6. 同步后自动校验
            if (!dryRun && autoValidate)
            {
                Debug.Log("\n[CastleDbPrefabSyncer] 执行同步后校验...");
                // 直接调用验证逻辑而不是通过菜单（更可靠）
                ValidateAfterSync();
            }

            Debug.Log("\n========== CastleDB Prefab 同步完成 ==========\n");

            // 显示摘要
            int successCount = syncResults.Count(r => r.success);
            int failCount = syncResults.Count(r => !r.success);
            string modeText = dryRun ? "预览" : "同步";
            EditorUtility.DisplayDialog(
                $"{modeText}完成",
                $"成功: {successCount}\n失败: {failCount}\n\n详情请查看 Console 和 {SYNC_LOG_FILE}",
                "确定");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[CastleDbPrefabSyncer] 同步过程中发生异常: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            isSyncing = false;
            Repaint();
        }
    }

    /// <summary>
    /// 同步单个 NPC 的 Prefab
    /// </summary>
    private SyncResult SyncNpcPrefab(string npcId, List<DetectionZoneEntry> zones, Dictionary<string, NpcEntry> npcData)
    {
        var result = new SyncResult { npcId = npcId };

        try
        {
            // 1. 定位 Prefab
            string prefabPath = FindPrefabPath(npcId, npcData);

            // 阶段4：Prefab 缺失时自动生成
            if (string.IsNullOrEmpty(prefabPath))
            {
                if (!npcData.TryGetValue(npcId, out var npc))
                {
                    result.success = false;
                    result.message = "找不到对应的 NPC 数据";
                    Debug.LogWarning($"[CastleDbPrefabSyncer] NPC '{npcId}' 在 CastleDB 中不存在");
                    return result;
                }

                Debug.Log($"[CastleDbPrefabSyncer] Prefab 不存在，自动创建: {npcId}");
                prefabPath = CreatePrefabForNpc(npc, result);

                if (string.IsNullOrEmpty(prefabPath))
                {
                    // CreatePrefabForNpc 已经设置了 result 的错误信息
                    return result;
                }
            }
            result.prefabPath = prefabPath;

            // 2. 加载 Prefab
            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefabAsset == null)
            {
                result.success = false;
                result.message = $"无法加载 Prefab: {prefabPath}";
                return result;
            }

            // 3. 获取 EnemyAgentBase 组件
            var enemyAgent = prefabAsset.GetComponent<EnemyAgentBase>();
            if (enemyAgent == null)
            {
                result.success = false;
                result.message = "Prefab 缺少 EnemyAgentBase 组件";
                return result;
            }

            // 4. 对齐 zoneBindings
            if (dryRun)
            {
                // Dry Run：只分析差异
                AnalyzeBindingDifferences(prefabAsset, enemyAgent, zones, result);
                result.success = true;
                result.message = result.changes.Count > 0 ? $"发现 {result.changes.Count} 处差异" : "已对齐，无需修改";
            }
            else
            {
                // 实际同步
                bool modified = ApplyBindingChanges(prefabPath, zones, result);
                result.success = true;
                result.message = modified ? $"已同步 {result.changes.Count} 处变更" : "无需修改";
            }
        }
        catch (System.Exception ex)
        {
            result.success = false;
            result.message = $"异常: {ex.Message}";
            Debug.LogError($"[CastleDbPrefabSyncer] 同步 NPC '{npcId}' 时发生异常: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// 分析 zoneBindings 差异（Dry Run 模式）
    /// </summary>
    private void AnalyzeBindingDifferences(GameObject prefab, EnemyAgentBase enemyAgent, List<DetectionZoneEntry> castleDbZones, SyncResult result)
    {
        var serializedObject = new SerializedObject(enemyAgent);
        var zoneBindingsProp = serializedObject.FindProperty("zoneBindings");

        // 构建当前 Prefab 的 binding 映射
        var currentBindings = new Dictionary<DetectionZoneBinding.Role, (string childName, DetectionZone zone)>();
        if (zoneBindingsProp != null && zoneBindingsProp.arraySize > 0)
        {
            for (int i = 0; i < zoneBindingsProp.arraySize; i++)
            {
                var element = zoneBindingsProp.GetArrayElementAtIndex(i);
                var roleField = element.FindPropertyRelative("role");
                var zoneField = element.FindPropertyRelative("zone");

                var role = (DetectionZoneBinding.Role)roleField.enumValueIndex;
                var zone = zoneField.objectReferenceValue as DetectionZone;
                string childName = zone != null ? zone.gameObject.name : null;

                if (!currentBindings.ContainsKey(role))
                {
                    currentBindings[role] = (childName, zone);
                }
            }
        }

        // 对比 CastleDB 定义
        foreach (var castleDbZone in castleDbZones)
        {
            var expectedRole = DetectionZoneRoleMapper.ToBindingRole(castleDbZone.role);
            string expectedChildId = castleDbZone.childId;

            if (!currentBindings.TryGetValue(expectedRole, out var currentBinding))
            {
                result.changes.Add($"[新增] role={expectedRole}, childId='{expectedChildId}'");
            }
            else if (currentBinding.childName != expectedChildId)
            {
                result.changes.Add($"[更新] role={expectedRole}: '{currentBinding.childName}' → '{expectedChildId}'");
            }
        }

        // 检查多余的 role（严格模式）
        if (strictMode)
        {
            var castleDbRoles = castleDbZones.Select(z => DetectionZoneRoleMapper.ToBindingRole(z.role)).ToHashSet();
            foreach (var kvp in currentBindings)
            {
                if (!castleDbRoles.Contains(kvp.Key))
                {
                    result.changes.Add($"[删除] role={kvp.Key}, childId='{kvp.Value.childName}'（严格模式）");
                }
            }
        }
    }

    /// <summary>
    /// 应用 zoneBindings 变更
    /// </summary>
    private bool ApplyBindingChanges(string prefabPath, List<DetectionZoneEntry> castleDbZones, SyncResult result)
    {
        // 使用 PrefabUtility 安全修改 Prefab
        var prefabContents = PrefabUtility.LoadPrefabContents(prefabPath);
        bool modified = false;

        try
        {
            var enemyAgent = prefabContents.GetComponent<EnemyAgentBase>();
            if (enemyAgent == null)
            {
                return false;
            }

            var serializedObject = new SerializedObject(enemyAgent);
            var zoneBindingsProp = serializedObject.FindProperty("zoneBindings");

            // 构建当前 binding 映射（index → role）
            var currentBindingIndices = new Dictionary<DetectionZoneBinding.Role, int>();
            if (zoneBindingsProp != null)
            {
                for (int i = 0; i < zoneBindingsProp.arraySize; i++)
                {
                    var element = zoneBindingsProp.GetArrayElementAtIndex(i);
                    var roleField = element.FindPropertyRelative("role");
                    var role = (DetectionZoneBinding.Role)roleField.enumValueIndex;

                    if (!currentBindingIndices.ContainsKey(role))
                    {
                        currentBindingIndices[role] = i;
                    }
                }
            }

            // 应用 CastleDB 定义
            foreach (var castleDbZone in castleDbZones)
            {
                var expectedRole = DetectionZoneRoleMapper.ToBindingRole(castleDbZone.role);
                string expectedChildId = castleDbZone.childId;

                // 查找或创建子物体
                var childTransform = prefabContents.transform.Find(expectedChildId);
                if (childTransform == null)
                {
                    // 尝试递归查找
                    childTransform = FindChildRecursive(prefabContents.transform, expectedChildId);
                }

                bool childWasCreated = false;
                if (childTransform == null)
                {
                    // 自动创建缺失的子物体（阶段4新增功能）
                    Debug.Log($"[CastleDbPrefabSyncer] {prefabPath}: 自动创建检测区子物体 '{expectedChildId}'");
                    var childObj = new GameObject(expectedChildId);
                    childObj.transform.SetParent(prefabContents.transform, false);
                    childTransform = childObj.transform;
                    childWasCreated = true;
                    result.changes.Add($"[创建] role={expectedRole}: 新建子物体 '{expectedChildId}'");
                }

                // 获取或添加 DetectionZone 组件
                var detectionZone = childTransform.GetComponent<DetectionZone>();
                if (detectionZone == null)
                {
                    Debug.Log($"[CastleDbPrefabSyncer] {prefabPath}: 为 '{expectedChildId}' 添加 DetectionZone 组件");
                    detectionZone = childTransform.gameObject.AddComponent<DetectionZone>();

                    if (!childWasCreated)
                    {
                        result.changes.Add($"[添加组件] role={expectedRole}: 为 '{expectedChildId}' 添加 DetectionZone");
                    }
                }

                // 获取或添加 Collider2D 组件（DetectionZone 需要 Trigger）
                var collider2D = childTransform.GetComponent<Collider2D>();
                if (collider2D == null)
                {
                    Debug.Log($"[CastleDbPrefabSyncer] {prefabPath}: 为 '{expectedChildId}' 添加 BoxCollider2D");
                    var boxCollider = childTransform.gameObject.AddComponent<BoxCollider2D>();
                    boxCollider.isTrigger = true;
                    boxCollider.size = new Vector2(1f, 1f); // 默认尺寸，需要在 Prefab Inspector 中调整

                    result.changes.Add($"[添加碰撞器] role={expectedRole}: 为 '{expectedChildId}' 添加 BoxCollider2D (isTrigger=true)");
                }
                else if (!collider2D.isTrigger)
                {
                    Debug.LogWarning($"[CastleDbPrefabSyncer] {prefabPath}: '{expectedChildId}' 的 Collider2D.isTrigger 为 false，已自动设置为 true");
                    collider2D.isTrigger = true;
                    result.changes.Add($"[修正] role={expectedRole}: '{expectedChildId}' 的 isTrigger 已设置为 true");
                }

                if (currentBindingIndices.TryGetValue(expectedRole, out int existingIndex))
                {
                    // 更新已存在的 binding
                    var element = zoneBindingsProp.GetArrayElementAtIndex(existingIndex);
                    var zoneField = element.FindPropertyRelative("zone");
                    var currentZone = zoneField.objectReferenceValue as DetectionZone;

                    if (currentZone != detectionZone)
                    {
                        zoneField.objectReferenceValue = detectionZone;
                        result.changes.Add($"[更新] role={expectedRole}: → '{expectedChildId}'");
                        modified = true;
                    }
                }
                else
                {
                    // 新增 binding
                    zoneBindingsProp.arraySize++;
                    var newElement = zoneBindingsProp.GetArrayElementAtIndex(zoneBindingsProp.arraySize - 1);
                    newElement.FindPropertyRelative("role").enumValueIndex = (int)expectedRole;
                    newElement.FindPropertyRelative("zone").objectReferenceValue = detectionZone;
                    result.changes.Add($"[新增] role={expectedRole}, childId='{expectedChildId}'");
                    modified = true;
                }
            }

            // 严格模式：删除多余的 role
            if (strictMode)
            {
                var castleDbRoles = castleDbZones.Select(z => DetectionZoneRoleMapper.ToBindingRole(z.role)).ToHashSet();
                for (int i = zoneBindingsProp.arraySize - 1; i >= 0; i--)
                {
                    var element = zoneBindingsProp.GetArrayElementAtIndex(i);
                    var role = (DetectionZoneBinding.Role)element.FindPropertyRelative("role").enumValueIndex;

                    if (!castleDbRoles.Contains(role))
                    {
                        var zone = element.FindPropertyRelative("zone").objectReferenceValue as DetectionZone;
                        string childName = zone != null ? zone.gameObject.name : "null";
                        zoneBindingsProp.DeleteArrayElementAtIndex(i);
                        result.changes.Add($"[删除] role={role}, childId='{childName}'");
                        modified = true;
                    }
                }
            }

            // 阶段4：确保核心组件存在并正确配置
            bool componentModified = EnsureEssentialComponents(prefabContents, enemyAgent, result);
            if (componentModified)
            {
                modified = true;
            }

            if (modified)
            {
                serializedObject.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(prefabContents, prefabPath);
                Debug.Log($"[CastleDbPrefabSyncer] Prefab 已保存: {prefabPath}");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefabContents);
        }

        return modified;
    }

    /// <summary>
    /// 阶段4：确保Prefab具备所有必需组件（自动修复）
    /// </summary>
    private bool EnsureEssentialComponents(GameObject prefabContents, EnemyAgentBase enemyAgent, SyncResult result)
    {
        bool modified = false;

        // 1. 检查并添加 Animator
        var animator = prefabContents.GetComponent<Animator>();
        if (animator == null)
        {
            animator = prefabContents.AddComponent<Animator>();
            result.changes.Add($"[添加组件] 添加 Animator 组件");
            modified = true;
            Debug.Log($"[CastleDbPrefabSyncer] 自动添加 Animator 组件");
        }

        // 2. 检查并添加 Damageable
        var damageable = prefabContents.GetComponent<Damageable>();
        if (damageable == null)
        {
            damageable = prefabContents.AddComponent<Damageable>();
            result.changes.Add($"[添加组件] 添加 Damageable 组件");
            modified = true;
            Debug.Log($"[CastleDbPrefabSyncer] 自动添加 Damageable 组件");
        }

        // 3. 检查并添加 Rigidbody2D
        var rb2d = prefabContents.GetComponent<Rigidbody2D>();
        if (rb2d == null)
        {
            rb2d = prefabContents.AddComponent<Rigidbody2D>();
            rb2d.bodyType = RigidbodyType2D.Dynamic;
            rb2d.gravityScale = 1f;
            rb2d.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb2d.constraints = RigidbodyConstraints2D.FreezeRotation; // 防止敌人旋转
            result.changes.Add($"[添加组件] 添加 Rigidbody2D 组件（Dynamic, Freeze Rotation）");
            modified = true;
            Debug.Log($"[CastleDbPrefabSyncer] 自动添加 Rigidbody2D 组件");
        }

        // 4. 检查并添加主 Collider2D（用于物理碰撞，非检测区）
        var mainColliders = prefabContents.GetComponents<Collider2D>();
        bool hasNonTriggerCollider = false;
        foreach (var col in mainColliders)
        {
            // 检查是否有非 Trigger 的碰撞器（排除检测区）
            if (!col.isTrigger && col.gameObject == prefabContents)
            {
                hasNonTriggerCollider = true;
                break;
            }
        }

        if (!hasNonTriggerCollider)
        {
            var capsuleCollider = prefabContents.AddComponent<CapsuleCollider2D>();
            capsuleCollider.isTrigger = false;
            capsuleCollider.size = new Vector2(0.5f, 1f); // 默认人形尺寸
            result.changes.Add($"[添加组件] 添加 CapsuleCollider2D（主碰撞器，非 Trigger）");
            modified = true;
            Debug.Log($"[CastleDbPrefabSyncer] 自动添加主 CapsuleCollider2D");
        }

        // 5. 检查 EnemyTuningProfile 是否已分配
        var serializedEnemy = new SerializedObject(enemyAgent);
        var profileProp = serializedEnemy.FindProperty("profile");
        if (profileProp != null && profileProp.objectReferenceValue == null)
        {
            // 尝试根据命名规则查找对应的 Profile
            string prefabName = prefabContents.name;
            string profilePath = $"Assets/Resources/Profiles/Profile_{prefabName}.asset";
            var profile = AssetDatabase.LoadAssetAtPath<EnemyTuningProfile>(profilePath);

            if (profile != null)
            {
                profileProp.objectReferenceValue = profile;
                serializedEnemy.ApplyModifiedPropertiesWithoutUndo();
                result.changes.Add($"[配置] 自动关联 EnemyTuningProfile: {profile.name}");
                modified = true;
                Debug.Log($"[CastleDbPrefabSyncer] 自动关联 Profile: {profilePath}");
            }
            else
            {
                result.changes.Add($"[警告] 未找到对应的 EnemyTuningProfile: {profilePath}");
                Debug.LogWarning($"[CastleDbPrefabSyncer] 未找到 Profile: {profilePath}，请手动配置或运行 Import All");
            }
        }

        return modified;
    }

    /// <summary>
    /// 递归查找子物体
    /// </summary>
    private Transform FindChildRecursive(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name)
            {
                return child;
            }
            var found = FindChildRecursive(child, name);
            if (found != null)
            {
                return found;
            }
        }
        return null;
    }

    // ===== Prefab 定位 =====

    /// <summary>
    /// 查找 NPC 对应的 Prefab 路径
    /// </summary>
    private string FindPrefabPath(string npcId, Dictionary<string, NpcEntry> npcData)
    {
        // 1. 优先使用 NpcEntry.prefabName
        if (npcData.TryGetValue(npcId, out var npc) && !string.IsNullOrEmpty(npc.prefabName))
        {
            var guids = AssetDatabase.FindAssets($"t:Prefab {npc.prefabName}", new[] { PREFAB_SEARCH_PATH });
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == npc.prefabName)
                {
                    return path;
                }
            }
        }

        // 2. Fallback：按命名规则查找
        // 尝试 M_Knight → KnightEnemy 或 Knight
        string searchName = npcId;
        if (searchName.StartsWith("M_"))
        {
            searchName = searchName.Substring(2);
        }

        var fallbackGuids = AssetDatabase.FindAssets($"t:Prefab", new[] { PREFAB_SEARCH_PATH });
        foreach (var guid in fallbackGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string fileName = Path.GetFileNameWithoutExtension(path);

            // 匹配规则：KnightEnemy, Knight, knight 等
            if (fileName.IndexOf(searchName, System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>
    /// 加载所有 NPC 数据
    /// </summary>
    private Dictionary<string, NpcEntry> LoadNpcData()
    {
        var result = new Dictionary<string, NpcEntry>();

        try
        {
            if (!CdbEditorModuleLoader.TryCreateServiceByProviderId("Monster", out var service, out var error))
            {
                Debug.LogWarning($"[CastleDbPrefabSyncer] 加载 NPC 数据失败: {error}");
                return result;
            }

            var npcs = service.GetAllNpcs();
            foreach (var npc in npcs)
            {
                if (!string.IsNullOrEmpty(npc.id))
                {
                    result[npc.id] = npc;
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[CastleDbPrefabSyncer] 加载 NPC 数据失败: {ex.Message}");
        }

        return result;
    }

    // ===== 备份与回滚 =====

    /// <summary>
    /// 备份现有 Prefab
    /// </summary>
    private string BackupPrefabs()
    {
        try
        {
            string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string backupDir = Path.Combine(BACKUP_ROOT_DIR, $"Backup_{timestamp}");

            // 获取项目根目录的绝对路径
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string absBackupDir = Path.Combine(projectRoot, backupDir);

            if (!Directory.Exists(absBackupDir))
            {
                Directory.CreateDirectory(absBackupDir);
            }

            // 复制所有敌人 Prefab
            var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { PREFAB_SEARCH_PATH });
            int backupCount = 0;

            foreach (var guid in prefabGuids)
            {
                string prefabPath = AssetDatabase.GUIDToAssetPath(guid);
                string relativePath = prefabPath.Replace("Assets/", "");
                string destPath = Path.Combine(absBackupDir, relativePath);

                string destDir = Path.GetDirectoryName(destPath);
                if (!Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                string absSrcPath = Path.Combine(projectRoot, prefabPath);
                File.Copy(absSrcPath, destPath, true);
                backupCount++;
            }

            Debug.Log($"[CastleDbPrefabSyncer] 已备份 {backupCount} 个 Prefab 到 {backupDir}");
            return backupDir;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[CastleDbPrefabSyncer] 备份失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 回滚上次同步
    /// </summary>
    private static void RevertLastSyncInternal()
    {
        try
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string absBackupRoot = Path.Combine(projectRoot, BACKUP_ROOT_DIR);

            if (!Directory.Exists(absBackupRoot))
            {
                EditorUtility.DisplayDialog("回滚失败", "未找到任何备份目录", "确定");
                return;
            }

            var backupDirs = Directory.GetDirectories(absBackupRoot)
                .OrderByDescending(d => d)
                .ToArray();

            if (backupDirs.Length == 0)
            {
                EditorUtility.DisplayDialog("回滚失败", "未找到任何备份", "确定");
                return;
            }

            string latestBackup = backupDirs[0];
            string backupName = Path.GetFileName(latestBackup);

            if (!EditorUtility.DisplayDialog(
                "确认回滚",
                $"将从备份 {backupName} 恢复 Prefab。\n\n此操作会覆盖当前的 Prefab 文件，是否继续？",
                "确定", "取消"))
            {
                return;
            }

            // 恢复 Prefab 文件
            int restoredCount = 0;
            var backupFiles = Directory.GetFiles(latestBackup, "*.prefab", SearchOption.AllDirectories);

            foreach (var backupFile in backupFiles)
            {
                string relativePath = backupFile.Substring(latestBackup.Length + 1);
                string destPath = Path.Combine(projectRoot, "Assets", relativePath);

                string destDir = Path.GetDirectoryName(destPath);
                if (!Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }

                File.Copy(backupFile, destPath, true);
                restoredCount++;
            }

            AssetDatabase.Refresh();

            Debug.Log($"[CastleDbPrefabSyncer] 回滚完成！已恢复 {restoredCount} 个 Prefab");
            EditorUtility.DisplayDialog(
                "回滚成功",
                $"已从备份 {backupName} 恢复 {restoredCount} 个 Prefab 文件。",
                "确定");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[CastleDbPrefabSyncer] 回滚失败: {ex.Message}\n{ex.StackTrace}");
            EditorUtility.DisplayDialog("回滚失败", $"回滚过程中发生错误：{ex.Message}", "确定");
        }
    }

    // ===== 日志 =====

    /// <summary>
    /// 同步后验证（阶段4：简化的验证流程）
    /// </summary>
    private void ValidateAfterSync()
    {
        int issueCount = 0;
        var validationResults = new System.Text.StringBuilder();
        validationResults.AppendLine("\n======== 同步后验证结果 ========");

        foreach (var syncResult in syncResults)
        {
            if (!syncResult.success || string.IsNullOrEmpty(syncResult.prefabPath))
            {
                continue;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(syncResult.prefabPath);
            if (prefab == null)
            {
                continue;
            }

            var issues = new List<string>();

            // 验证必需组件
            if (prefab.GetComponent<EnemyAgentBase>() == null)
            {
                issues.Add("缺少 EnemyAgentBase 组件");
            }

            if (prefab.GetComponent<Animator>() == null)
            {
                issues.Add("缺少 Animator 组件");
            }

            if (prefab.GetComponent<Damageable>() == null)
            {
                issues.Add("缺少 Damageable 组件");
            }

            if (prefab.GetComponent<Rigidbody2D>() == null)
            {
                issues.Add("缺少 Rigidbody2D 组件");
            }

            // 验证 EnemyTuningProfile
            var enemyAgent = prefab.GetComponent<EnemyAgentBase>();
            if (enemyAgent != null)
            {
                var serializedEnemy = new SerializedObject(enemyAgent);
                var profileProp = serializedEnemy.FindProperty("profile");
                if (profileProp != null && profileProp.objectReferenceValue == null)
                {
                    issues.Add("未分配 EnemyTuningProfile");
                }

                // 验证 zoneBindings
                var zoneBindingsProp = serializedEnemy.FindProperty("zoneBindings");
                if (zoneBindingsProp == null || zoneBindingsProp.arraySize == 0)
                {
                    issues.Add("zoneBindings 为空");
                }
                else
                {
                    bool hasPrimaryAttack = false;
                    bool hasSecondaryAttack = false;
                    for (int i = 0; i < zoneBindingsProp.arraySize; i++)
                    {
                        var element = zoneBindingsProp.GetArrayElementAtIndex(i);
                        var role = (DetectionZoneBinding.Role)element.FindPropertyRelative("role").enumValueIndex;
                        if (role == DetectionZoneBinding.Role.PrimaryAttack)
                        {
                            hasPrimaryAttack = true;
                        }
                        else if (role == DetectionZoneBinding.Role.SecondaryAttack)
                        {
                            hasSecondaryAttack = true;
                        }
                    }

                    if (!hasPrimaryAttack && !hasSecondaryAttack)
                    {
                        issues.Add("zoneBindings 缺少 PrimaryAttack / SecondaryAttack 检测区");
                    }
                }
            }

            if (issues.Count > 0)
            {
                issueCount += issues.Count;
                validationResults.AppendLine($"\n[警告] {syncResult.npcId} ({Path.GetFileName(syncResult.prefabPath)}):");
                foreach (var issue in issues)
                {
                    validationResults.AppendLine($"  • {issue}");
                }
            }
            else
            {
                validationResults.AppendLine($"[通过] {syncResult.npcId} - 所有检查项通过");
            }
        }

        validationResults.AppendLine($"\n总计: {issueCount} 个问题");
        validationResults.AppendLine("================================\n");

        string validationLog = validationResults.ToString();
        Debug.Log(validationLog);

        if (issueCount > 0)
        {
            Debug.LogWarning($"[CastleDbPrefabSyncer] 验证发现 {issueCount} 个问题，请查看详细日志");
        }
        else
        {
            Debug.Log($"[CastleDbPrefabSyncer] 所有 Prefab 验证通过！");
        }
    }

    // ===== 日志 =====

    /// <summary>
    /// 写入同步日志
    /// </summary>
    private void WriteSyncLog(string backupDir)
    {
        try
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string absLogPath = Path.Combine(projectRoot, SYNC_LOG_FILE);

            string logDir = Path.GetDirectoryName(absLogPath);
            if (!Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir);
            }

            var logContent = new System.Text.StringBuilder();
            logContent.AppendLine("════════════════════════════════════════════════════════");
            logContent.AppendLine($"         CastleDB Prefab 同步日志");
            logContent.AppendLine($"         {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            logContent.AppendLine("════════════════════════════════════════════════════════");
            logContent.AppendLine();
            logContent.AppendLine($"模式: {(dryRun ? "Dry Run（预览）" : "实际同步")}");
            logContent.AppendLine($"严格模式: {strictMode}");
            logContent.AppendLine($"备份目录: {backupDir ?? "无（Dry Run）"}");
            logContent.AppendLine();
            logContent.AppendLine("────────────────────────────────────────────────────────");
            logContent.AppendLine("同步结果：");
            logContent.AppendLine();

            int successCount = 0;
            int failCount = 0;

            foreach (var result in syncResults)
            {
                if (result.success) successCount++; else failCount++;

                string icon = result.success ? "[成功]" : "[失败]";
                logContent.AppendLine($"{icon} NPC: {result.npcId}");
                logContent.AppendLine($"       Prefab: {result.prefabPath ?? "未找到"}");
                logContent.AppendLine($"       消息: {result.message}");

                if (result.changes.Count > 0)
                {
                    logContent.AppendLine("       变更:");
                    foreach (var change in result.changes)
                    {
                        logContent.AppendLine($"         • {change}");
                    }
                }
                logContent.AppendLine();
            }

            logContent.AppendLine("────────────────────────────────────────────────────────");
            logContent.AppendLine($"统计: 成功 {successCount}, 失败 {failCount}");
            logContent.AppendLine("════════════════════════════════════════════════════════");
            logContent.AppendLine();

            File.AppendAllText(absLogPath, logContent.ToString());
            Debug.Log($"[CastleDbPrefabSyncer] 同步日志已写入: {SYNC_LOG_FILE}");
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[CastleDbPrefabSyncer] 写入日志失败: {ex.Message}");
        }
    }

    // ===== 阶段4：Prefab 自动生成 =====

    /// <summary>
    /// 阶段4：为 NPC 创建新的 Prefab
    /// </summary>
    private string CreatePrefabForNpc(NpcEntry npc, SyncResult result)
    {
        try
        {
            // 使用 prefabName 或 displayName 作为 Prefab 文件名
            string prefabName = !string.IsNullOrEmpty(npc.prefabName) ? npc.prefabName : npc.displayName;
            if (string.IsNullOrEmpty(prefabName))
            {
                prefabName = npc.id; // fallback 到 id
            }

            // 清理文件名（移除非法字符）
            prefabName = System.Text.RegularExpressions.Regex.Replace(prefabName, @"[^a-zA-Z0-9_\u4e00-\u9fa5]", "");
            if (string.IsNullOrEmpty(prefabName))
            {
                result.success = false;
                result.message = "无法生成有效的 Prefab 文件名";
                Debug.LogError($"[CastleDbPrefabSyncer] NPC '{npc.id}' 的 prefabName/displayName 无效");
                return null;
            }

            string prefabPath = $"{PREFAB_SEARCH_PATH}/{prefabName}.prefab";

            // 检查文件是否已存在（避免覆盖）
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
            {
                result.success = false;
                result.message = $"Prefab 文件已存在但未被识别: {prefabPath}";
                Debug.LogWarning($"[CastleDbPrefabSyncer] Prefab 文件存在但查找失败，可能是命名规则问题");
                return null;
            }

            Debug.Log($"[CastleDbPrefabSyncer] 正在创建 Prefab: {prefabPath}");

            // 创建 GameObject
            GameObject enemyRoot = new GameObject(prefabName);

            // 添加基础组件
            var rb2d = enemyRoot.AddComponent<Rigidbody2D>();
            rb2d.bodyType = RigidbodyType2D.Dynamic;
            rb2d.gravityScale = 1f;
            rb2d.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb2d.constraints = RigidbodyConstraints2D.FreezeRotation;

            var mainCollider = enemyRoot.AddComponent<CapsuleCollider2D>();
            mainCollider.isTrigger = false;
            mainCollider.size = new Vector2(0.5f, 1f);

            enemyRoot.AddComponent<Animator>();
            enemyRoot.AddComponent<Damageable>();

            // 注意：EnemyAgentBase 是抽象类，不能直接添加。
            // 0.5：默认使用地面敌人控制器（NpcGroundController），新建 Prefab 后可按需替换为 NpcFlyController 等实现。
            enemyRoot.AddComponent<NpcGroundController>();

            // 保存为 Prefab
            PrefabUtility.SaveAsPrefabAsset(enemyRoot, prefabPath);

            // 清理临时 GameObject
            UnityEngine.Object.DestroyImmediate(enemyRoot);

            result.changes.Add($"[创建 Prefab] {prefabPath}");
            Debug.Log($"[CastleDbPrefabSyncer] Prefab 创建成功: {prefabPath}");

            return prefabPath;
        }
        catch (System.Exception ex)
        {
            result.success = false;
            result.message = $"创建 Prefab 失败: {ex.Message}";
            Debug.LogError($"[CastleDbPrefabSyncer] 创建 Prefab 失败: {ex.Message}\n{ex.StackTrace}");
            return null;
        }
    }
}
