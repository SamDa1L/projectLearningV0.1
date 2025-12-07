using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 检测区域批量迁移工具
/// 自动将现有不规范的Prefab迁移到v0.2标准
///
/// 功能：
/// 1. 扫描所有敌人Prefab
/// 2. 检查是否符合新规范：
///    - 检测区子物体命名是否为DZ_*格式
///    - 根物体是否有DetectionZone（应该迁至子物体）
///    - zoneBindings是否已配置
/// 3. 执行迁移操作：
///    - 创建备份Prefab（原名+_backup）
///    - 迁移不规范的检测区
///    - 自动填充zoneBindings
/// 4. 生成详细的迁移报告
///
/// 使用方式：
/// Tools → Detection Zone → Batch Migrate All Prefabs
/// </summary>
public class BatchMigrateDetectionZones
{
    // ===== 配置常量 =====

    private const string PREFAB_SEARCH_PATH = "Assets/Resources/Prefabs";
    private const string BACKUP_SUFFIX = "_backup";

    // ===== 统计数据 =====

    private class MigrationStats
    {
        public int totalPrefabs = 0;
        public int migratedPrefabs = 0;
        public int skippedPrefabs = 0;
        public int failedPrefabs = 0;
        public List<string> migrationLog = new List<string>();
        public List<string> warningLog = new List<string>();
        public List<string> errorLog = new List<string>();
    }

    // ===== 菜单项 =====

    [MenuItem("Tools/Detection Zone/Batch Migrate All Prefabs")]
    public static void MigrateAllPrefabs()
    {
        if (!EditorUtility.DisplayDialog(
            "确认迁移",
            "此操作将迁移所有敌人Prefab到v0.2标准。\n\n" +
            "• 将创建备份Prefab（名称+_backup）\n" +
            "• 不规范的检测区将被重命名为DZ_*格式\n" +
            "• 根物体的DetectionZone将迁移到子物体\n\n" +
            "建议在提交代码前执行此操作。确定继续？",
            "继续", "取消"))
        {
            return;
        }

        Debug.Log("\n========== 开始检测区域批量迁移 ==========\n");

        var stats = new MigrationStats();

        // 扫描所有敌人Prefab
        var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { PREFAB_SEARCH_PATH });

        foreach (string guid in prefabGuids)
        {
            string prefabPath = AssetDatabase.GUIDToAssetPath(guid);

            // 过滤敌人Prefab
            if (!prefabPath.Contains("Enemy") && !prefabPath.Contains("enemy"))
                continue;

            // 跳过已有_backup的Prefab
            if (prefabPath.Contains(BACKUP_SUFFIX))
                continue;

            stats.totalPrefabs++;

            // 加载Prefab
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                stats.failedPrefabs++;
                stats.errorLog.Add($"✗ 无法加载Prefab：{prefabPath}");
                continue;
            }

            // 检查是否需要迁移
            if (!NeedsMigration(prefab))
            {
                stats.skippedPrefabs++;
                stats.migrationLog.Add($"✓ 已符合规范，跳过：{prefabPath}");
                continue;
            }

            // 执行迁移
            if (MigratePrefab(prefab, prefabPath, stats))
            {
                stats.migratedPrefabs++;
            }
            else
            {
                stats.failedPrefabs++;
            }
        }

        // 打印迁移报告
        PrintMigrationReport(stats);

        Debug.Log("\n========== 迁移完成 ==========\n");
    }

    // ===== 核心迁移逻辑 =====

    /// <summary>
    /// 检查Prefab是否需要迁移
    /// </summary>
    private static bool NeedsMigration(GameObject prefab)
    {
        // 检查1：根物体是否有DetectionZone
        if (prefab.GetComponent<DetectionZone>() != null)
            return true;

        // 检查2：子物体命名是否符合DZ_*规范
        var allZones = prefab.GetComponentsInChildren<DetectionZone>();
        foreach (var zone in allZones)
        {
            if (!zone.gameObject.name.StartsWith("DZ_"))
                return true;
        }

        // 检查3：是否有EnemyAgentBase且zoneBindings为空
        var enemyAgent = prefab.GetComponent<EnemyAgentBase>();
        if (enemyAgent != null)
        {
            // 通过SerializedObject检查zoneBindings是否配置
            var serializedObject = new SerializedObject(enemyAgent);
            var zoneBindingsProp = serializedObject.FindProperty("zoneBindings");
            if (zoneBindingsProp != null && zoneBindingsProp.arraySize == 0)
                return true;
        }

        return false;
    }

    /// <summary>
    /// 执行单个Prefab的迁移
    /// </summary>
    private static bool MigratePrefab(GameObject prefab, string prefabPath, MigrationStats stats)
    {
        try
        {
            Debug.Log($"[迁移] {prefabPath}");

            // 1. 创建备份
            string backupPath = prefabPath.Replace(".prefab", $"{BACKUP_SUFFIX}.prefab");
            if (!AssetDatabase.CopyAsset(prefabPath, backupPath))
            {
                stats.errorLog.Add($"✗ 无法创建备份：{backupPath}");
                return false;
            }
            Debug.Log($"  ✓ 创建备份：{backupPath}");

            // 2. 迁移根物体的DetectionZone
            MigrateRootDetectionZone(prefab);

            // 3. 重命名不规范的检测区
            RenameDetectionZonesToStandard(prefab);

            // 4. 配置zoneBindings（如果有EnemyAgentBase）
            ConfigureZoneBindings(prefab);

            // 5. 保存Prefab
            PrefabUtility.SavePrefabAsset(prefab);
            AssetDatabase.Refresh();

            stats.migrationLog.Add($"✓ 迁移成功：{prefabPath}");
            return true;
        }
        catch (System.Exception ex)
        {
            stats.errorLog.Add($"✗ 迁移失败：{prefabPath}\n  错误：{ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 迁移根物体上的DetectionZone到子物体
    /// </summary>
    private static void MigrateRootDetectionZone(GameObject prefab)
    {
        var rootZone = prefab.GetComponent<DetectionZone>();
        if (rootZone == null)
            return;

        Debug.Log($"  • 迁移根物体DetectionZone");

        // 获取根物体的Collider2D
        var rootCollider = prefab.GetComponent<Collider2D>();

        // 创建DZ_Attack子物体
        GameObject dzAttackGO = new GameObject("DZ_Attack");
        dzAttackGO.transform.SetParent(prefab.transform);
        dzAttackGO.transform.localPosition = Vector3.zero;

        // 复制Collider2D到子物体
        if (rootCollider != null)
        {
            var newCollider = dzAttackGO.AddComponent<BoxCollider2D>();
            newCollider.size = new Vector2(1f, 1f);
            newCollider.isTrigger = true;
        }

        // 移动DetectionZone脚本到子物体
        var newZone = dzAttackGO.AddComponent<DetectionZone>();
        newZone.detectedColliders = rootZone.detectedColliders;

        // 删除根物体的DetectionZone和Collider
        Object.DestroyImmediate(rootZone);
        if (rootCollider != null && !(rootCollider is CircleCollider2D))
        {
            Object.DestroyImmediate(rootCollider);
        }

        Debug.Log($"    → 根物体DetectionZone已迁移到 DZ_Attack");
    }

    /// <summary>
    /// 重命名不符合DZ_*规范的检测区
    /// </summary>
    private static void RenameDetectionZonesToStandard(GameObject prefab)
    {
        var allZones = prefab.GetComponentsInChildren<DetectionZone>();

        int renamedCount = 0;
        foreach (var zone in allZones)
        {
            if (zone.gameObject.name.StartsWith("DZ_"))
                continue;

            // 根据检测区的用途推测应该的名称
            string newName = InferZoneName(zone.gameObject.name);
            Debug.Log($"  • 重命名: '{zone.gameObject.name}' → '{newName}'");
            zone.gameObject.name = newName;
            renamedCount++;
        }

        if (renamedCount > 0)
        {
            Debug.Log($"    → 重命名了 {renamedCount} 个检测区");
        }
    }

    /// <summary>
    /// 根据原始名称推测新的DZ_*命名
    /// </summary>
    private static string InferZoneName(string originalName)
    {
        string lower = originalName.ToLower();

        if (lower.Contains("attack") || lower.Contains("sword") || lower.Contains("bite"))
            return "DZ_Attack";
        if (lower.Contains("cliff") || lower.Contains("edge") || lower.Contains("ground"))
            return "DZ_Cliff";
        if (lower.Contains("alert") || lower.Contains("aware"))
            return "DZ_Alert";
        if (lower.Contains("lookout") || lower.Contains("view") || lower.Contains("sight"))
            return "DZ_Lookout";

        // 默认设为Attack
        return "DZ_Attack";
    }

    /// <summary>
    /// 配置EnemyAgentBase的zoneBindings列表
    /// </summary>
    private static void ConfigureZoneBindings(GameObject prefab)
    {
        var enemyAgent = prefab.GetComponent<EnemyAgentBase>();
        if (enemyAgent == null)
            return;

        var serializedObject = new SerializedObject(enemyAgent);
        var zoneBindingsProp = serializedObject.FindProperty("zoneBindings");

        if (zoneBindingsProp == null || zoneBindingsProp.arraySize > 0)
            return; // 已配置或无法访问

        Debug.Log($"  • 配置zoneBindings列表");

        // 获取所有子物体的DetectionZone
        var allZones = prefab.GetComponentsInChildren<DetectionZone>();
        if (allZones.Length == 0)
            return;

        // 为每个检测区添加binding
        foreach (var zone in allZones)
        {
            zoneBindingsProp.arraySize++;
            var bindingElement = zoneBindingsProp.GetArrayElementAtIndex(zoneBindingsProp.arraySize - 1);

            // 设置zone字段
            var zoneField = bindingElement.FindPropertyRelative("zone");
            zoneField.objectReferenceValue = zone;

            // 根据名称推测role
            var roleField = bindingElement.FindPropertyRelative("role");
            string zoneName = zone.gameObject.name;
            if (zoneName == "DZ_Attack")
                roleField.enumValueIndex = (int)DetectionZoneBinding.Role.PrimaryAttack;
            else if (zoneName == "DZ_Cliff")
                roleField.enumValueIndex = (int)DetectionZoneBinding.Role.Cliff;
            else if (zoneName == "DZ_Alert")
                roleField.enumValueIndex = (int)DetectionZoneBinding.Role.Alert;
            else if (zoneName == "DZ_Lookout")
                roleField.enumValueIndex = (int)DetectionZoneBinding.Role.Lookout;
            else
                roleField.enumValueIndex = (int)DetectionZoneBinding.Role.Custom;

            // 设置note字段
            var noteField = bindingElement.FindPropertyRelative("note");
            noteField.stringValue = $"Auto-migrated from {zoneName}";

            Debug.Log($"    → 添加binding: {zoneName} (role={roleField.enumNames[roleField.enumValueIndex]})");
        }

        serializedObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(enemyAgent);
    }

    // ===== 报告生成 =====

    /// <summary>
    /// 打印迁移报告
    /// </summary>
    private static void PrintMigrationReport(MigrationStats stats)
    {
        Debug.Log("\n");
        Debug.Log("========== 迁移报告 ==========");
        Debug.Log($"总计Prefab数: {stats.totalPrefabs}");
        Debug.Log($"✓ 成功迁移: {stats.migratedPrefabs}");
        Debug.Log($"⊘ 跳过（已符合规范）: {stats.skippedPrefabs}");
        Debug.Log($"✗ 失败: {stats.failedPrefabs}");
        Debug.Log("");

        // 迁移日志
        if (stats.migrationLog.Count > 0)
        {
            Debug.Log("[迁移日志]");
            foreach (var log in stats.migrationLog)
            {
                Debug.Log(log);
            }
            Debug.Log("");
        }

        // 警告日志
        if (stats.warningLog.Count > 0)
        {
            Debug.LogWarning("[警告]");
            foreach (var log in stats.warningLog)
            {
                Debug.LogWarning(log);
            }
            Debug.Log("");
        }

        // 错误日志
        if (stats.errorLog.Count > 0)
        {
            Debug.LogError("[错误]");
            foreach (var log in stats.errorLog)
            {
                Debug.LogError(log);
            }
            Debug.Log("");
        }

        // 显示完成对话框
        string message = $"迁移完成！\n\n" +
            $"成功迁移: {stats.migratedPrefabs}\n" +
            $"跳过: {stats.skippedPrefabs}\n" +
            $"失败: {stats.failedPrefabs}\n\n" +
            $"详见Console输出";

        EditorUtility.DisplayDialog("迁移报告", message, "确定");
    }
}
