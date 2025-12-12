using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using CastleDB.Runtime;

/// <summary>
/// CastleDB 导入工具
/// 从CastleDB数据通过CastleDbService导入NPC数据到EnemyTuningProfile资源
///
/// 功能：
/// - 使用CastleDbService读取NPC数据
/// - 创建/更新EnemyTuningProfile ScriptableObject
/// - 验证schemaVersion=0.2
/// - 生成导入日志
///
/// 使用方式：
/// 1. 在Unity菜单中选择 Tools > CastleDB > Import All
/// 2. 工具会自动通过CastleDbService读取数据
/// 3. 为每个NPC创建或更新对应的Profile资源
/// 4. 生成导入日志到 Logs/CastleDbImport.log
/// </summary>
public class CastleDbImporter
{
    // 配置常量
    private const string CASTLEDB_RESOURCE_PATH = "Data/CastleDbDemo/MonsterSystem";
    private const string PROFILE_OUTPUT_DIR = "Assets/Resources/Profiles";
    private const string IMPORT_LOG_DIR = "Logs";
    private const string IMPORT_LOG_FILE = "Logs/CastleDbImport.log";
    private const string BACKUP_DIR = "Logs/CastleDBImport/Backups";

    // 版本检查
    private const string EXPECTED_SCHEMA_VERSION = "0.2";

    [MenuItem("Tools/CastleDB/Import All")]
    public static void ImportAll()
    {
        Debug.Log("[CastleDbImporter] 开始导入CastleDB数据...");

        try
        {
            // 1. 加载CastleDB资源
            var asset = Resources.Load<TextAsset>(CASTLEDB_RESOURCE_PATH);
            if (asset == null)
            {
                Debug.LogError($"[CastleDbImporter] 无法加载CastleDB资源: {CASTLEDB_RESOURCE_PATH}");
                return;
            }

            // 2. 初始化CastleDbService
            var source = new CastleDbJsonSource(asset);
            var service = new CastleDbService();
            service.Initialize(source);

            // 3. 验证版本
            var versionInfo = service.GetVersionInfo();
            if (versionInfo == null || versionInfo.schemaVersion != EXPECTED_SCHEMA_VERSION)
            {
                Debug.LogError($"[CastleDbImporter] 版本不匹配。期望: {EXPECTED_SCHEMA_VERSION}, 实际: {versionInfo?.schemaVersion ?? "null"}");
                return;
            }

            // 4. 生成备份
            BackupExistingProfiles();

            // 5. 获取所有NPC数据
            var npcs = service.GetAllNpcs();
            if (npcs.Count == 0)
            {
                Debug.LogWarning("[CastleDbImporter] 未找到任何NPC数据");
                return;
            }

            // 6. 导入NPC数据
            int successCount = 0;
            int failureCount = 0;

            foreach (var npc in npcs)
            {
                if (CreateOrUpdateProfile(npc))
                {
                    successCount++;
                }
                else
                {
                    failureCount++;
                }
            }

            // 7. 生成日志
            LogImportResult(npcs, successCount, failureCount, versionInfo);

            // 8. 刷新资源
            AssetDatabase.Refresh();

            Debug.Log($"[CastleDbImporter] 导入完成！成功: {successCount}, 失败: {failureCount}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[CastleDbImporter] 导入过程中发生错误: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// 创建或更新Profile
    /// </summary>
    private static bool CreateOrUpdateProfile(NpcEntry npc)
    {
        try
        {
            // 确保输出目录存在
            if (!Directory.Exists(PROFILE_OUTPUT_DIR))
            {
                Directory.CreateDirectory(PROFILE_OUTPUT_DIR);
            }

            // 生成Profile文件路径（使用displayName作为文件名）
            string profilePath = $"{PROFILE_OUTPUT_DIR}/Profile_{npc.displayName}.asset";

            // 查找或创建Profile
            EnemyTuningProfile profile = AssetDatabase.LoadAssetAtPath<EnemyTuningProfile>(profilePath);

            if (profile == null)
            {
                // 创建新Profile
                profile = ScriptableObject.CreateInstance<EnemyTuningProfile>();
                profile.profileName = npc.displayName;
                AssetDatabase.CreateAsset(profile, profilePath);
                Debug.Log($"[CastleDbImporter] 创建新Profile: {profilePath}");
            }
            else
            {
                Debug.Log($"[CastleDbImporter] 更新现有Profile: {profilePath}");
            }

            // 应用CastleDB数据
            profile.ApplyFromCastleDb(npc);

            // 保存资源
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            return true;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[CastleDbImporter] 创建/更新Profile失败 ({npc.id}): {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 生成现有Profile的备份
    /// </summary>
    private static void BackupExistingProfiles()
    {
        try
        {
            if (!Directory.Exists(PROFILE_OUTPUT_DIR))
                return;

            // 创建备份目录
            if (!Directory.Exists(BACKUP_DIR))
            {
                Directory.CreateDirectory(BACKUP_DIR);
            }

            // 生成备份时间戳
            string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string backupPath = Path.Combine(BACKUP_DIR, $"Backup_{timestamp}");

            // 复制所有Profile到备份目录
            var profileFiles = Directory.GetFiles(PROFILE_OUTPUT_DIR, "Profile_*.asset");
            if (profileFiles.Length > 0)
            {
                Directory.CreateDirectory(backupPath);
                foreach (var file in profileFiles)
                {
                    string fileName = Path.GetFileName(file);
                    string destPath = Path.Combine(backupPath, fileName);
                    File.Copy(file, destPath, true);
                }

                Debug.Log($"[CastleDbImporter] 备份完成: {backupPath} ({profileFiles.Length} 个文件)");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[CastleDbImporter] 备份失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 生成导入日志
    /// </summary>
    private static void LogImportResult(List<NpcEntry> npcs, int successCount, int failureCount, CastleDbVersionInfo versionInfo)
    {
        try
        {
            // 创建日志目录
            var logDir = Path.GetDirectoryName(IMPORT_LOG_FILE);
            if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir);
            }

            // 生成日志文件
            string logPath = IMPORT_LOG_FILE;

            var logContent = new System.Text.StringBuilder();
            logContent.AppendLine("=== CastleDB 导入日志 ===");
            logContent.AppendLine($"导入时间: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            logContent.AppendLine($"Schema版本: {versionInfo?.schemaVersion ?? "unknown"}");
            logContent.AppendLine($"总计: {npcs.Count} 个NPC");
            logContent.AppendLine($"成功: {successCount}");
            logContent.AppendLine($"失败: {failureCount}");
            logContent.AppendLine();
            logContent.AppendLine("=== NPC数值字段摘要 ===");

            foreach (var npc in npcs)
            {
                logContent.AppendLine($"- {npc.displayName} (id={npc.id})");
                logContent.AppendLine($"  maxHealth={npc.maxHealth}, moveSpeed={npc.moveSpeed}, attackDamage={npc.attackDamage}");
                logContent.AppendLine($"  attackRange={npc.attackRange}, attackCooldown={npc.attackCooldown}");
                logContent.AppendLine($"  invincibleDuration={npc.invincibleDuration}, knockbackMultiplier={npc.knockbackMultiplier}");
                logContent.AppendLine($"  enableDeathAnimation={npc.enableDeathAnimation}, useLegacyLogicFallback={npc.useLegacyLogicFallback}");
            }

            File.WriteAllText(logPath, logContent.ToString());
            Debug.Log($"[CastleDbImporter] 日志已保存: {logPath}");
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[CastleDbImporter] 生成日志失败: {ex.Message}");
        }
    }

    [MenuItem("Tools/CastleDB/Open Import Logs")]
    public static void OpenImportLogs()
    {
        if (Directory.Exists(IMPORT_LOG_DIR))
        {
            EditorUtility.RevealInFinder(IMPORT_LOG_DIR);
        }
        else
        {
            Debug.LogWarning($"[CastleDbImporter] 日志目录不存在: {IMPORT_LOG_DIR}");
        }
    }

    [MenuItem("Tools/CastleDB/Open Profile Directory")]
    public static void OpenProfileDirectory()
    {
        if (Directory.Exists(PROFILE_OUTPUT_DIR))
        {
            EditorUtility.RevealInFinder(PROFILE_OUTPUT_DIR);
        }
        else
        {
            Debug.LogWarning($"[CastleDbImporter] Profile目录不存在: {PROFILE_OUTPUT_DIR}");
        }
    }
}
