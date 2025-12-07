using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 检测区域助手工具
/// 提供编辑器菜单和辅助函数，便于检测区域的规范化和批量操作
///
/// 功能：
/// 1. 列出所有子物体DetectionZone - 快速查看一个Prefab包含哪些检测区
/// 2. 推荐迁移 - 扫描所有Prefab，指出哪些需要迁移（如根物体有DetectionZone）
/// 3. 验证命名规范 - 检查所有检测区是否遵循DZ_*命名规范
///
/// 设计说明：
/// - 这是第二阶段的工具类骨架，为未来的完整自动化预留扩展空间
/// - 目前主要提供查询和统计功能，不涉及破坏性操作
/// - 所有操作都是只读的，不会修改Prefab内容
/// </summary>
public class DetectionZoneHelper
{
    // ===== 菜单命令 =====

    /// <summary>
    /// 列出所选GameObject及其所有子物体的DetectionZone
    /// </summary>
    [MenuItem("Tools/Detection Zone/List All Zones In Selected")]
    public static void ListDetectionZonesInSelected()
    {
        GameObject selected = Selection.activeGameObject;
        if (selected == null)
        {
            EditorUtility.DisplayDialog("错误", "请先在Hierarchy中选择一个GameObject", "确定");
            return;
        }

        Debug.Log($"\n[DetectionZoneHelper] 扫描 '{selected.name}' 中的所有检测区\n");

        var allZones = selected.GetComponentsInChildren<DetectionZone>();

        if (allZones.Length == 0)
        {
            Debug.LogWarning($"[DetectionZoneHelper] '{selected.name}' 及其子物体中未找到任何DetectionZone");
            return;
        }

        Debug.Log($"[DetectionZoneHelper] 找到 {allZones.Length} 个DetectionZone:\n");

        for (int i = 0; i < allZones.Length; i++)
        {
            var zone = allZones[i];
            string indent = zone.transform.parent == selected.transform ? "  └─ " : "      └─ ";
            string path = GetRelativePath(selected.transform, zone.transform);
            Debug.Log($"{indent}{i + 1}. {path}");
        }

        Debug.Log("\n");
    }

    /// <summary>
    /// 扫描所有Prefab，找出需要迁移的（根物体有DetectionZone）
    /// </summary>
    [MenuItem("Tools/Detection Zone/Recommend Migration")]
    public static void RecommendMigration()
    {
        Debug.Log("\n[DetectionZoneHelper] 开始扫描所有敌人Prefab，查找需要迁移的检测区\n");

        var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Resources/Prefabs" });
        List<string> migrationCandidates = new List<string>();

        int checkedCount = 0;
        foreach (string guid in prefabGuids)
        {
            string prefabPath = AssetDatabase.GUIDToAssetPath(guid);

            if (!prefabPath.Contains("Enemy") && !prefabPath.Contains("enemy"))
                continue;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
                continue;

            checkedCount++;

            // 检查根物体是否有DetectionZone
            var rootZone = prefab.GetComponent<DetectionZone>();
            if (rootZone != null)
            {
                migrationCandidates.Add($"  - {prefabPath}");
                Debug.LogWarning(
                    $"[DetectionZoneHelper] {prefabPath}: 根物体包含DetectionZone，" +
                    $"建议迁至子物体（如DZ_Attack）",
                    prefab
                );
            }
        }

        Debug.Log($"\n[DetectionZoneHelper] 扫描完成: 检查了 {checkedCount} 个Prefab");

        if (migrationCandidates.Count > 0)
        {
            Debug.LogWarning($"发现 {migrationCandidates.Count} 个需要迁移的Prefab:\n" +
                string.Join("\n", migrationCandidates));
        }
        else
        {
            Debug.Log("✓ 所有Prefab都符合规范，无需迁移");
        }

        Debug.Log("\n");
    }

    /// <summary>
    /// 扫描所有Prefab，检查检测区命名规范
    /// </summary>
    [MenuItem("Tools/Detection Zone/Validate Naming Convention")]
    public static void ValidateNamingConvention()
    {
        Debug.Log("\n[DetectionZoneHelper] 开始检查所有检测区的命名规范\n");

        var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Resources/Prefabs" });
        List<string> nonStandardNames = new List<string>();

        int checkedCount = 0;
        int standardCount = 0;
        int nonStandardCount = 0;

        foreach (string guid in prefabGuids)
        {
            string prefabPath = AssetDatabase.GUIDToAssetPath(guid);

            if (!prefabPath.Contains("Enemy") && !prefabPath.Contains("enemy"))
                continue;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
                continue;

            var allZones = prefab.GetComponentsInChildren<DetectionZone>();
            checkedCount += allZones.Length;

            foreach (var zone in allZones)
            {
                // 检查是否以DZ_开头
                if (zone.gameObject.name.StartsWith("DZ_"))
                {
                    standardCount++;
                }
                else
                {
                    nonStandardCount++;
                    nonStandardNames.Add($"  - {prefabPath} > {zone.gameObject.name} (建议改为: DZ_{zone.gameObject.name})");
                }
            }
        }

        Debug.Log($"[DetectionZoneHelper] 扫描完成\n");
        Debug.Log($"✓ 符合规范 (DZ_*): {standardCount}");

        if (nonStandardCount > 0)
        {
            Debug.LogWarning($"⚠ 不符合规范: {nonStandardCount}\n");
            Debug.LogWarning("建议改名的检测区:\n" + string.Join("\n", nonStandardNames));
        }
        else
        {
            Debug.Log("✓ 所有检测区都符合命名规范");
        }

        Debug.Log("\n");
    }

    // ===== 辅助方法 =====

    /// <summary>
    /// 获取相对于根物体的相对路径
    /// 例如：根物体下DZ_Attack子物体会返回"DZ_Attack"
    /// </summary>
    private static string GetRelativePath(Transform root, Transform target)
    {
        if (target == root)
            return root.name;

        var path = new List<string> { target.name };
        Transform current = target.parent;

        while (current != null && current != root)
        {
            path.Insert(0, current.name);
            current = current.parent;
        }

        return string.Join("/", path);
    }
}
