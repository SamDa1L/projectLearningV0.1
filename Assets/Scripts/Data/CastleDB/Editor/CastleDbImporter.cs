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

    // ===== Step 0: 日志写入工具函数 =====

    /// <summary>
    /// 获取Unity项目根目录（绝对路径）
    /// </summary>
    private static string GetProjectRootPath()
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    }

    /// <summary>
    /// 将相对路径转换为绝对路径
    /// </summary>
    private static string GetAbsolutePath(string relativePath)
    {
        string projectRoot = GetProjectRootPath();
        return Path.Combine(projectRoot, relativePath);
    }

    /// <summary>
    /// 安全写入文件（原子写：临时文件 → 替换目标文件）
    /// </summary>
    private static void SafeWriteAllText(string path, string content)
    {
        try
        {
            // 确保目录存在
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // 写临时文件
            string tempPath = $"{path}.{System.Guid.NewGuid()}.tmp";
            File.WriteAllText(tempPath, content);

            // 替换目标文件
            File.Copy(tempPath, path, true);
            File.Delete(tempPath);

            Debug.Log($"[CastleDbImporter] 成功写入日志: {path}");
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[CastleDbImporter] 写入日志失败 (path={path}): {ex.Message}");

            // Fallback: 尝试写入后备日志
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
        Debug.Log("[CastleDbImporter] 开始导入CastleDB数据...");

        // ===== Step 1: 会话上下文初始化 =====
        var startedAt = System.DateTime.Now;
        string status = "Failed"; // 默认失败，成功时再改
        string message = null;
        string sourceDescription = null;
        string versionInfo = null;
        var npcs = new List<NpcEntry>();
        var notes = new List<string>();
        int successCount = 0;
        int failureCount = 0;

        try
        {
            // 1. 加载CastleDB资源
            var asset = Resources.Load<TextAsset>(CASTLEDB_RESOURCE_PATH);
            if (asset == null)
            {
                status = "AssetMissing";
                message = $"无法加载CastleDB资源: {CASTLEDB_RESOURCE_PATH}";
                Debug.LogError($"[CastleDbImporter] {message}");
                return;
            }
            sourceDescription = $"Resources/{CASTLEDB_RESOURCE_PATH}";

            // 2. 初始化CastleDbService
            var source = new CastleDbJsonSource(asset);
            var service = new CastleDbService();
            service.Initialize(source);

            // 3. 验证版本
            var versionInfoObj = service.GetVersionInfo();
            if (versionInfoObj == null || versionInfoObj.schemaVersion != EXPECTED_SCHEMA_VERSION)
            {
                status = "VersionMismatch";
                message = $"版本不匹配。期望: {EXPECTED_SCHEMA_VERSION}, 实际: {versionInfoObj?.schemaVersion ?? "null"}";
                versionInfo = $"{versionInfoObj?.schemaVersion ?? "null"}";
                Debug.LogError($"[CastleDbImporter] {message}");
                return;
            }
            versionInfo = versionInfoObj.schemaVersion;

            // 4. 生成备份
            BackupExistingProfiles();

            // 5. 获取所有NPC数据
            npcs = service.GetAllNpcs();
            if (npcs.Count == 0)
            {
                status = "NoData";
                message = "未找到任何NPC数据";
                Debug.LogWarning($"[CastleDbImporter] {message}");
                return;
            }

            // 6. 导入NPC数据（传入notes收集器）
            foreach (var npc in npcs)
            {
                if (CreateOrUpdateProfile(npc, notes))
                {
                    successCount++;
                }
                else
                {
                    failureCount++;
                }
            }

            // 7. 确定最终状态
            if (failureCount == 0)
            {
                status = "Success";
            }
            else
            {
                status = "CompletedWithFailures";
                message = $"部分NPC导入失败 (成功: {successCount}, 失败: {failureCount})";
            }

            // 8. 刷新资源
            AssetDatabase.Refresh();

            Debug.Log($"[CastleDbImporter] 导入完成！成功: {successCount}, 失败: {failureCount}");
        }
        catch (System.Exception ex)
        {
            status = "Exception";
            message = $"{ex.Message}\n{ex.StackTrace}";
            Debug.LogError($"[CastleDbImporter] 导入过程中发生异常: {ex.Message}\n{ex.StackTrace}");
        }
        finally
        {
            // ===== Step 1: finally 中统一写日志（Always Log）=====
            var finishedAt = System.DateTime.Now;
            WriteImportLog(startedAt, finishedAt, status, message, sourceDescription, versionInfo, npcs, successCount, failureCount, notes);
        }
    }

    /// <summary>
    /// 创建或更新Profile
    /// Step 2 改造：不再写文件，改为收集 notes
    /// </summary>
    private static bool CreateOrUpdateProfile(NpcEntry npc, List<string> notes)
    {
        try
        {
            // ===== 业务校验 =====
            // 校验 animationTrigger：必须非空且仅含字母/数字/下划线
            if (string.IsNullOrWhiteSpace(npc.animationTrigger))
            {
                string errorMsg = $"NPC '{npc.id}' 的 animationTrigger 为空，跳过导入。请到 CastleDB 修正。";
                Debug.LogError($"[CastleDbImporter] {errorMsg}");
                notes.Add($"❌ {errorMsg}");
                return false;
            }
            if (!System.Text.RegularExpressions.Regex.IsMatch(npc.animationTrigger, @"^[a-zA-Z0-9_]+$"))
            {
                string errorMsg = $"NPC '{npc.id}' 的 animationTrigger '{npc.animationTrigger}' 包含非法字符（仅允许字母/数字/下划线），跳过导入。";
                Debug.LogError($"[CastleDbImporter] {errorMsg}");
                notes.Add($"❌ {errorMsg}");
                return false;
            }

            // 确保输出目录存在
            if (!Directory.Exists(PROFILE_OUTPUT_DIR))
            {
                Directory.CreateDirectory(PROFILE_OUTPUT_DIR);
            }

            // 生成Profile文件路径（Step 5: 使用 npc.id 作为稳定主键，避免 displayName 改动导致引用断裂）
            string profilePath = $"{PROFILE_OUTPUT_DIR}/Profile_{npc.id}.asset";

            // 查找或创建Profile
            EnemyTuningProfile profile = AssetDatabase.LoadAssetAtPath<EnemyTuningProfile>(profilePath);

            // 记录旧的 animationTrigger（用于变更日志）
            string oldTrigger = profile != null ? profile.animationTrigger : null;

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

            // ===== Step 2: animationTrigger 变更记录到 notes（不再写文件）=====
            if (!string.IsNullOrEmpty(oldTrigger) && oldTrigger != npc.animationTrigger)
            {
                string changeLog = $"⚠️ AnimationTrigger 变更 - NPC '{npc.id}': 旧值='{oldTrigger}' → 新值='{npc.animationTrigger}' (提醒：请通知美术/动画同学同步 Animator Controller 的 Trigger 参数！)";
                Debug.LogWarning($"[CastleDbImporter] {changeLog}");
                notes.Add(changeLog);
            }

            // 保存资源
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            return true;
        }
        catch (System.Exception ex)
        {
            string errorMsg = $"创建/更新Profile失败 ({npc.id}): {ex.Message}";
            Debug.LogError($"[CastleDbImporter] {errorMsg}");
            notes.Add($"❌ {errorMsg}");
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
    /// Step 3: 会话级日志写入（升级版 LogImportResult）
    /// 无论成功、失败、异常都会产出完整的会话日志
    /// </summary>
    private static void WriteImportLog(
        System.DateTime startedAt,
        System.DateTime finishedAt,
        string status,
        string message,
        string sourceDescription,
        string versionInfo,
        List<NpcEntry> npcs,
        int successCount,
        int failureCount,
        List<string> notes)
    {
        try
        {
            var logContent = new System.Text.StringBuilder();

            // ===== Header: 会话信息 =====
            logContent.AppendLine("════════════════════════════════════════════════════════");
            logContent.AppendLine("         CastleDB Import Session Log");
            logContent.AppendLine("════════════════════════════════════════════════════════");
            logContent.AppendLine();
            logContent.AppendLine($"开始时间: {startedAt:yyyy-MM-dd HH:mm:ss}");
            logContent.AppendLine($"结束时间: {finishedAt:yyyy-MM-dd HH:mm:ss}");
            logContent.AppendLine($"耗时: {(finishedAt - startedAt).TotalSeconds:F2} 秒");
            logContent.AppendLine();

            // ===== Status =====
            logContent.AppendLine($"状态: {status}");
            logContent.AppendLine();

            // ===== Message（失败原因/异常） =====
            if (!string.IsNullOrEmpty(message))
            {
                logContent.AppendLine("════════════════════════════════════════════════════════");
                logContent.AppendLine("         失败原因/异常信息");
                logContent.AppendLine("════════════════════════════════════════════════════════");
                logContent.AppendLine(message);
                logContent.AppendLine();
            }

            // ===== Summary =====
            logContent.AppendLine("════════════════════════════════════════════════════════");
            logContent.AppendLine("         导入摘要");
            logContent.AppendLine("════════════════════════════════════════════════════════");
            logContent.AppendLine($"数据源: {sourceDescription ?? "N/A"}");
            logContent.AppendLine($"Schema版本: {versionInfo ?? "N/A"}");
            logContent.AppendLine($"NPC总数: {npcs.Count}");
            logContent.AppendLine($"成功数量: {successCount}");
            logContent.AppendLine($"失败数量: {failureCount}");
            logContent.AppendLine();

            // ===== Notes（Trigger变更、NPC失败原因等） =====
            if (notes.Count > 0)
            {
                logContent.AppendLine("════════════════════════════════════════════════════════");
                logContent.AppendLine("         过程提示/警告/错误");
                logContent.AppendLine("════════════════════════════════════════════════════════");
                foreach (var note in notes)
                {
                    logContent.AppendLine($"• {note}");
                }
                logContent.AppendLine();
            }

            // ===== NPC Summary（仅成功导入时输出详细信息） =====
            if (status == "Success" || status == "CompletedWithFailures")
            {
                logContent.AppendLine("════════════════════════════════════════════════════════");
                logContent.AppendLine("         NPC 数值字段摘要");
                logContent.AppendLine("════════════════════════════════════════════════════════");

                foreach (var npc in npcs)
                {
                    logContent.AppendLine($"- {npc.displayName} (id={npc.id})");
                    logContent.AppendLine($"  maxHealth={npc.maxHealth}, moveSpeed={npc.moveSpeed}, attackDamage={npc.attackDamage}");
                    logContent.AppendLine($"  attackRange={npc.attackRange}, attackCooldown={npc.attackCooldown}");
                    logContent.AppendLine($"  invincibleDuration={npc.invincibleDuration}, knockbackMultiplier={npc.knockbackMultiplier}");
                    logContent.AppendLine($"  enableDeathAnimation={npc.enableDeathAnimation}, useLegacyLogicFallback={npc.useLegacyLogicFallback}");
                    logContent.AppendLine($"  animationTrigger={npc.animationTrigger}");
                }
                logContent.AppendLine();
            }

            logContent.AppendLine("════════════════════════════════════════════════════════");
            logContent.AppendLine("         End of Log");
            logContent.AppendLine("════════════════════════════════════════════════════════");

            // ===== 写入文件（使用绝对路径 + 原子写）=====
            string absLogPath = GetAbsolutePath(IMPORT_LOG_FILE);
            SafeWriteAllText(absLogPath, logContent.ToString());

            // ===== Step 5: 生成字段映射表 =====
            if (status == "Success" || status == "CompletedWithFailures")
            {
                GenerateFieldMappingTable(startedAt, npcs);
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[CastleDbImporter] 写入会话日志时发生严重错误: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// Step 5: 生成字段映射表
    /// 记录 CastleDB → DTO → Profile → Runtime 的字段对应关系
    /// </summary>
    private static void GenerateFieldMappingTable(System.DateTime timestamp, List<NpcEntry> npcs)
    {
        try
        {
            var mappingContent = new System.Text.StringBuilder();

            mappingContent.AppendLine("# CastleDB 字段映射表");
            mappingContent.AppendLine();
            mappingContent.AppendLine($"生成时间: {timestamp:yyyy-MM-dd HH:mm:ss}");
            mappingContent.AppendLine();
            mappingContent.AppendLine("## 字段映射关系");
            mappingContent.AppendLine();
            mappingContent.AppendLine("| CastleDB 字段 | NpcEntry (DTO) | EnemyTuningProfile | EnemyAgentBase (Runtime) | 说明 |");
            mappingContent.AppendLine("|--------------|----------------|-------------------|-------------------------|------|");
            mappingContent.AppendLine("| id | id | - | - | NPC 唯一标识符 |");
            mappingContent.AppendLine("| displayName | displayName | profileName | - | 显示名称 |");
            mappingContent.AppendLine("| prefabName | prefabName | - | - | Prefab 名称（预留） |");
            mappingContent.AppendLine("| animationTrigger | animationTrigger | animationTrigger | _attackTriggerName | 攻击动画触发器 |");
            mappingContent.AppendLine("| maxHealth | maxHealth | maxHealth | - (via Damageable) | 最大生命值 |");
            mappingContent.AppendLine("| attackDamage | attackDamage | attackDamage | _attackDamage | 攻击伤害 |");
            mappingContent.AppendLine("| moveSpeed | moveSpeed | moveSpeed | _moveSpeed | 移动速度 |");
            mappingContent.AppendLine("| attackRange | attackRange | attackRange | _attackRange | 攻击范围 |");
            mappingContent.AppendLine("| attackCooldown | attackCooldown | attackCooldown | _attackCooldown | 攻击冷却 |");
            mappingContent.AppendLine("| invincibleDuration | invincibleDuration | invulnerableFrameDuration | - (via Damageable) | 无敌帧时长 |");
            mappingContent.AppendLine("| knockbackMultiplier | knockbackMultiplier | knockbackMultiplier | _knockbackMultiplier | 击退倍率 |");
            mappingContent.AppendLine("| enableDeathAnimation | enableDeathAnimation | enableDeathAnimation | _enableDeathAnimation | 启用死亡动画 |");
            mappingContent.AppendLine("| useLegacyLogicFallback | useLegacyLogicFallback | useLegacyLogicFallback | _useLegacyLogicFallback | 使用旧逻辑回退 |");
            mappingContent.AppendLine();
            mappingContent.AppendLine("## 当前导入的 NPC 数据");
            mappingContent.AppendLine();

            foreach (var npc in npcs)
            {
                mappingContent.AppendLine($"### {npc.displayName} (id={npc.id})");
                mappingContent.AppendLine();
                mappingContent.AppendLine("```");
                mappingContent.AppendLine($"animationTrigger: {npc.animationTrigger}");
                mappingContent.AppendLine($"maxHealth: {npc.maxHealth}");
                mappingContent.AppendLine($"attackDamage: {npc.attackDamage}");
                mappingContent.AppendLine($"moveSpeed: {npc.moveSpeed}");
                mappingContent.AppendLine($"attackRange: {npc.attackRange}");
                mappingContent.AppendLine($"attackCooldown: {npc.attackCooldown}");
                mappingContent.AppendLine($"invincibleDuration: {npc.invincibleDuration}");
                mappingContent.AppendLine($"knockbackMultiplier: {npc.knockbackMultiplier}");
                mappingContent.AppendLine($"enableDeathAnimation: {npc.enableDeathAnimation}");
                mappingContent.AppendLine($"useLegacyLogicFallback: {npc.useLegacyLogicFallback}");
                mappingContent.AppendLine("```");
                mappingContent.AppendLine();
            }

            // 写入映射表文件
            string mappingDir = "Logs/NotesLog/CodexProjectLogs";
            string mappingFileName = $"FieldMapping_{timestamp:yyyyMMdd_HHmmss}.md";
            string mappingPath = Path.Combine(mappingDir, mappingFileName);
            string absMappingPath = GetAbsolutePath(mappingPath);

            SafeWriteAllText(absMappingPath, mappingContent.ToString());

            Debug.Log($"[CastleDbImporter] 字段映射表已生成: {mappingPath}");
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[CastleDbImporter] 生成字段映射表失败: {ex.Message}");
        }
    }

    [MenuItem("Tools/CastleDB/Open Import Logs")]
    public static void OpenImportLogs()
    {
        // Step 4: 使用绝对路径解析
        string absDir = GetAbsolutePath(IMPORT_LOG_DIR);
        if (Directory.Exists(absDir))
        {
            EditorUtility.RevealInFinder(absDir);
        }
        else
        {
            Debug.LogWarning($"[CastleDbImporter] 日志目录不存在: {absDir}");
        }
    }

    /// <summary>
    /// Step 5: 一键回滚到上次导入前的状态
    /// 从最新的备份目录恢复 Profiles
    /// </summary>
    [MenuItem("Tools/CastleDB/Revert Last Import")]
    public static void RevertLastImport()
    {
        if (!EditorUtility.DisplayDialog(
            "确认回滚",
            "此操作将恢复到上次导入前的 Profile 状态。\n当前的 Profile 将被覆盖。\n\n是否继续？",
            "确认回滚",
            "取消"))
        {
            return;
        }

        try
        {
            string absBackupDir = GetAbsolutePath(BACKUP_DIR);
            if (!Directory.Exists(absBackupDir))
            {
                Debug.LogError($"[CastleDbImporter] 备份目录不存在: {absBackupDir}");
                EditorUtility.DisplayDialog("回滚失败", "备份目录不存在，无法回滚。", "确定");
                return;
            }

            // 查找最新的备份目录
            var backupDirs = Directory.GetDirectories(absBackupDir, "Backup_*");
            if (backupDirs.Length == 0)
            {
                Debug.LogError($"[CastleDbImporter] 未找到任何备份");
                EditorUtility.DisplayDialog("回滚失败", "未找到任何备份，无法回滚。", "确定");
                return;
            }

            // 按时间戳排序，获取最新的备份
            System.Array.Sort(backupDirs);
            string latestBackup = backupDirs[backupDirs.Length - 1];
            string backupName = Path.GetFileName(latestBackup);

            Debug.Log($"[CastleDbImporter] 开始回滚到备份: {backupName}");

            // 恢复备份文件到 Profile 目录
            var backupFiles = Directory.GetFiles(latestBackup, "Profile_*.asset");
            int restoredCount = 0;

            foreach (var backupFile in backupFiles)
            {
                string fileName = Path.GetFileName(backupFile);
                string destPath = Path.Combine(PROFILE_OUTPUT_DIR, fileName);

                File.Copy(backupFile, destPath, true);
                restoredCount++;
            }

            AssetDatabase.Refresh();

            Debug.Log($"[CastleDbImporter] 回滚完成！已恢复 {restoredCount} 个 Profile 文件");
            EditorUtility.DisplayDialog(
                "回滚成功",
                $"已从备份 {backupName} 恢复 {restoredCount} 个 Profile 文件。",
                "确定");
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
        }
        else
        {
            Debug.LogWarning($"[CastleDbImporter] Profile目录不存在: {PROFILE_OUTPUT_DIR}");
        }
    }
}
