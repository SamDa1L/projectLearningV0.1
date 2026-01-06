using System.Collections.Generic;
using System.IO;
using CastleDB.Editor;
using CastleDB.Runtime;
using UnityEditor;
using UnityEngine;

/// <summary>
/// CastleDB 导入工具（0.4 Provider 流程）
/// - Import All：交由 CdbImportCoordinator 扫描/校验/导入
/// - Revert Last Import：回滚到最近一次导入前的备份
/// - Prefab/Scene 工具：通过 providerId 动态定位模块资源
/// </summary>
public class CastleDbImporter
{
    private const string MONSTER_PROVIDER_ID = "Monster";

    private const string PROFILE_OUTPUT_DIR = "Assets/Resources/Profiles";
    private const string IMPORT_LOG_DIR = "Logs";
    private const string IMPORT_LOG_FILE = "Logs/CastleDbImport.log";

    private const string SETTINGS_PATH = "Assets/Settings/CdbImportSettings.asset";
    private const string SETTINGS_DIR = "Assets/Settings";

    private static string GetProjectRootPath()
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    }

    private static string GetAbsolutePath(string relativePath)
    {
        string projectRoot = GetProjectRootPath();
        return Path.Combine(projectRoot, relativePath);
    }

    private static void SafeWriteAllText(string path, string content)
    {
        try
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string tempPath = $"{path}.{System.Guid.NewGuid()}.tmp";
            File.WriteAllText(tempPath, content);

            File.Copy(tempPath, path, true);
            File.Delete(tempPath);

            Debug.Log($"[CastleDbImporter] 成功写入日志: {path}");
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[CastleDbImporter] 写入日志失败 (path={path}): {ex.Message}");

            try
            {
                string fallbackPath = $"{path}_fallback_{System.DateTime.Now:yyyyMMdd_HHmmss}.log";
                File.WriteAllText(fallbackPath, content);
                Debug.LogWarning($"[CastleDbImporter] 已写入后备日志: {fallbackPath}");
            }
            catch (System.Exception fallbackEx)
            {
                Debug.LogError($"[CastleDbImporter] 后备日志写入也失败: {fallbackEx.Message}");
            }
        }
    }

    [MenuItem("Tools/CastleDB/Import All")]
    public static void ImportAll()
    {
        Debug.Log("[CastleDbImporter] 开始导入 CastleDB 数据...");

        CdbProviderBootstrap.EnsureRegistered();

        var coordinator = new CdbImportCoordinator(CdbDataProviderRegistry.Instance);
        var result = coordinator.ImportAll();

        var logContent = result.GetFormattedLog();
        Debug.Log($"[CastleDbImporter] Import All {(result.IsSuccess ? "成功" : "失败")}");

        string absLogPath = GetAbsolutePath(IMPORT_LOG_FILE);
        SafeWriteAllText(absLogPath, logContent);

        if (!result.IsSuccess)
        {
            EditorUtility.DisplayDialog(
                "Import All 失败",
                "导入过程中发生错误，请查看 Console 和日志文件了解详情。\n\n" +
                $"日志位置：{IMPORT_LOG_FILE}",
                "确定"
            );
            return;
        }

        Debug.Log($"[CastleDbImporter] 导入完成，日志已保存至：{IMPORT_LOG_FILE}");
    }

    [MenuItem("Tools/CastleDB/Open Import Logs")]
    public static void OpenImportLogs()
    {
        string absDir = GetAbsolutePath(IMPORT_LOG_DIR);
        if (Directory.Exists(absDir))
        {
            EditorUtility.RevealInFinder(absDir);
            return;
        }

        Debug.LogWarning($"[CastleDbImporter] 日志目录不存在: {absDir}");
    }

    [MenuItem("Tools/CastleDB/Revert Last Import")]
    public static void RevertLastImport()
    {
        if (!EditorUtility.DisplayDialog(
            "确认回滚",
            "此操作将恢复到上次导入前的 Profile、PlayerConfig 和 AbilityCatalog 状态。\n当前的文件将被覆盖。\n\n是否继续？",
            "确认回滚",
            "取消"))
        {
            return;
        }

        try
        {
            string absBackupDir = GetAbsolutePath("Logs/CastleDBImport/Backups");
            if (!Directory.Exists(absBackupDir))
            {
                Debug.LogError($"[CastleDbImporter] 备份目录不存在: {absBackupDir}");
                EditorUtility.DisplayDialog("回滚失败", "备份目录不存在，无法回滚。", "确定");
                return;
            }

            var backupDirs = Directory.GetDirectories(absBackupDir, "Backup_*");
            if (backupDirs.Length == 0)
            {
                Debug.LogError("[CastleDbImporter] 未找到任何备份");
                EditorUtility.DisplayDialog("回滚失败", "未找到任何备份，无法回滚。", "确定");
                return;
            }

            System.Array.Sort(backupDirs);
            string latestBackup = backupDirs[backupDirs.Length - 1];
            string backupName = Path.GetFileName(latestBackup);

            Debug.Log($"[CastleDbImporter] 调用 CdbImportCoordinator 回滚到备份: {backupName}");

            var coordinatorType = typeof(CdbImportCoordinator);
            var restoreMethod = coordinatorType.GetMethod(
                "RestoreFromBackup",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (restoreMethod == null)
            {
                Debug.LogError("[CastleDbImporter] 无法找到 CdbImportCoordinator.RestoreFromBackup 方法");
                EditorUtility.DisplayDialog("回滚失败", "内部错误：无法找到回滚方法", "确定");
                return;
            }

            var coordinator = new CdbImportCoordinator(CdbDataProviderRegistry.Instance);

            string timestamp = backupName.Replace("Backup_", "");
            restoreMethod.Invoke(coordinator, new object[] { timestamp });

            AssetDatabase.Refresh();

            Debug.Log($"[CastleDbImporter] 回滚完成！备份：{backupName}");
            EditorUtility.DisplayDialog("回滚成功", $"已从备份 {backupName} 恢复资产。", "确定");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[CastleDbImporter] 回滚失败: {ex.Message}\n{ex.StackTrace}");
            EditorUtility.DisplayDialog("回滚失败", $"回滚过程中发生错误：{ex.Message}", "确定");
        }
    }

    [MenuItem("Tools/CastleDB/Open Profile Directory")]
    public static void OpenProfileDirectory()
    {
        if (Directory.Exists(PROFILE_OUTPUT_DIR))
        {
            EditorUtility.RevealInFinder(PROFILE_OUTPUT_DIR);
            return;
        }

        Debug.LogWarning($"[CastleDbImporter] Profile目录不存在: {PROFILE_OUTPUT_DIR}");
    }

    [MenuItem("Tools/CastleDB/Settings")]
    public static void OpenSettings()
    {
        var settings = AssetDatabase.LoadAssetAtPath<CdbImportSettings>(SETTINGS_PATH);

        if (settings == null)
        {
            if (!Directory.Exists(SETTINGS_DIR))
            {
                Directory.CreateDirectory(SETTINGS_DIR);
                AssetDatabase.Refresh();
            }

            settings = ScriptableObject.CreateInstance<CdbImportSettings>();
            settings.cdbImportRoot = "Assets/Resources";

            AssetDatabase.CreateAsset(settings, SETTINGS_PATH);
            AssetDatabase.SaveAssets();

            Debug.Log($"[CastleDbImporter] 已创建 CdbImportSettings: {SETTINGS_PATH}");
        }

        Selection.activeObject = settings;
        EditorGUIUtility.PingObject(settings);
    }

    // ===== 2B: DetectionZone 聚合与校验支持 =====

    public static Dictionary<string, List<DetectionZoneEntry>> GetDetectionZonesGroupedByNpcId(CastleDbService service)
    {
        var result = new Dictionary<string, List<DetectionZoneEntry>>();

        if (service == null)
        {
            Debug.LogWarning("[CastleDbImporter] GetDetectionZonesGroupedByNpcId: service 为 null");
            return result;
        }

        var allZones = service.GetAllDetectionZones();
        if (allZones == null || allZones.Count == 0)
        {
            Debug.Log("[CastleDbImporter] GetDetectionZonesGroupedByNpcId: 未找到任何检测区数据");
            return result;
        }

        foreach (var zone in allZones)
        {
            if (string.IsNullOrEmpty(zone.npcId))
            {
                Debug.LogWarning($"[CastleDbImporter] 检测区 '{zone.id}' 的 npcId 为空，已跳过");
                continue;
            }

            if (!result.ContainsKey(zone.npcId))
            {
                result[zone.npcId] = new List<DetectionZoneEntry>();
            }
            result[zone.npcId].Add(zone);
        }

        Debug.Log($"[CastleDbImporter] 检测区聚合完成：{result.Count} 个 NPC，共 {allZones.Count} 个检测区");
        return result;
    }

    public static Dictionary<string, List<DetectionZoneEntry>> LoadDetectionZonesGroupedByNpcId()
    {
        var result = new Dictionary<string, List<DetectionZoneEntry>>();

        try
        {
            if (!CdbEditorModuleLoader.TryCreateServiceByProviderId(MONSTER_PROVIDER_ID, out var service, out var error))
            {
                Debug.LogWarning($"[CastleDbImporter] LoadDetectionZonesGroupedByNpcId: {error}");
                return result;
            }

            return GetDetectionZonesGroupedByNpcId(service);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[CastleDbImporter] LoadDetectionZonesGroupedByNpcId 失败: {ex.Message}");
            return result;
        }
    }

    public static Dictionary<string, string> GetNpcIdToProfilePathMapping()
    {
        var result = new Dictionary<string, string>();

        if (!Directory.Exists(PROFILE_OUTPUT_DIR))
        {
            return result;
        }

        var profileFiles = Directory.GetFiles(PROFILE_OUTPUT_DIR, "Profile_*.asset");
        foreach (var file in profileFiles)
        {
            string fileName = Path.GetFileNameWithoutExtension(file);
            if (fileName.StartsWith("Profile_"))
            {
                string npcId = fileName.Substring("Profile_".Length);
                result[npcId] = file.Replace("\\", "/");
            }
        }

        return result;
    }
}

