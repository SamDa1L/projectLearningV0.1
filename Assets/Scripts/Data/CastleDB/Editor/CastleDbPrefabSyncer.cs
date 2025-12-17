using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CastleDB.Runtime;

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
                // 调用 ValidateEnemyPrefabsWindow 的校验逻辑
                EditorApplication.ExecuteMenuItem("Tools/Stage1/Validate Enemy Prefabs");
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
            if (string.IsNullOrEmpty(prefabPath))
            {
                result.success = false;
                result.message = "找不到对应的 Prefab";
                Debug.LogWarning($"[CastleDbPrefabSyncer] NPC '{npcId}' 找不到 Prefab");
                return result;
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

                // 查找子物体
                var childTransform = prefabContents.transform.Find(expectedChildId);
                if (childTransform == null)
                {
                    // 尝试递归查找
                    childTransform = FindChildRecursive(prefabContents.transform, expectedChildId);
                }

                if (childTransform == null)
                {
                    result.changes.Add($"[跳过] role={expectedRole}: 找不到子物体 '{expectedChildId}'");
                    Debug.LogWarning($"[CastleDbPrefabSyncer] {prefabPath}: 找不到子物体 '{expectedChildId}'");
                    continue;
                }

                // 获取 DetectionZone 组件
                var detectionZone = childTransform.GetComponent<DetectionZone>();
                if (detectionZone == null)
                {
                    result.changes.Add($"[跳过] role={expectedRole}: 子物体 '{expectedChildId}' 缺少 DetectionZone 组件");
                    Debug.LogWarning($"[CastleDbPrefabSyncer] {prefabPath}: 子物体 '{expectedChildId}' 缺少 DetectionZone 组件");
                    continue;
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
            var asset = Resources.Load<TextAsset>("Data/CastleDbDemo/MonsterSystem");
            if (asset == null)
            {
                return result;
            }

            var source = new CastleDbJsonSource(asset);
            var service = new CastleDbService();
            service.Initialize(source);

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
}
