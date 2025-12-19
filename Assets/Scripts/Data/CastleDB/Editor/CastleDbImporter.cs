using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    private const string PLAYER_CONFIG_PATH = "Assets/Resources/Config/PlayerConfig.asset";
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
        List<DetectionZoneEntry> detectionZones = null;
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
            detectionZones = service.GetAllDetectionZones();
            var players = service.GetAllPlayers();
            var playerAttackOverrides = service.GetAllPlayerAttackOverrides();

            // 阶段 3A: 提前查找 playerEntry，避免重复声明
            PlayerEntry playerEntry = players.Find(p => p.id == "player");

            if (npcs.Count == 0)
            {
                status = "NoData";
                message = "未找到任何NPC数据";
                Debug.LogWarning($"[CastleDbImporter] {message}");
                return;
            }

            // ===== Step 5.1: 全量业务校验（纯读，不写入）=====
            var validationErrors = new List<string>();
            foreach (var npc in npcs)
            {
                // 校验 maxHealth
                if (npc.maxHealth <= 0)
                {
                    validationErrors.Add($"NPC '{npc.id}' 的 maxHealth <= 0 ({npc.maxHealth})");
                }

                // 校验 moveSpeed
                if (npc.moveSpeed <= 0)
                {
                    validationErrors.Add($"NPC '{npc.id}' 的 moveSpeed <= 0 ({npc.moveSpeed})");
                }

                // 校验 animationTrigger
                if (string.IsNullOrWhiteSpace(npc.animationTrigger))
                {
                    validationErrors.Add($"NPC '{npc.id}' 的 animationTrigger 为空");
                }
                else if (!System.Text.RegularExpressions.Regex.IsMatch(npc.animationTrigger, @"^[a-zA-Z0-9_]+$"))
                {
                    validationErrors.Add($"NPC '{npc.id}' 的 animationTrigger '{npc.animationTrigger}' 包含非法字符");
                }

                // 校验 attackCooldown
                if (npc.attackCooldown < 0)
                {
                    validationErrors.Add($"NPC '{npc.id}' 的 attackCooldown < 0 ({npc.attackCooldown})");
                }

                // 校验 attackDamage
                if (npc.attackDamage < 0)
                {
                    validationErrors.Add($"NPC '{npc.id}' 的 attackDamage < 0 ({npc.attackDamage})");
                }
            }

            // ===== 阶段 3A: Player 数据校验 =====
            // 校验 Player Sheet：必须存在且 id=="player" 只有1行
            if (players.Count == 0)
            {
                validationErrors.Add("Player Sheet 不存在或为空（阶段 3A 必需）");
            }
            else
            {
                // 使用前面已声明的 playerEntry
                if (playerEntry == null)
                {
                    validationErrors.Add("Player Sheet 中未找到 id=='player' 的条目（阶段 3A 必需）");
                }
                else
                {
                    // 基础属性校验
                    if (playerEntry.maxHealth <= 0)
                        validationErrors.Add($"Player 'maxHealth' <= 0 ({playerEntry.maxHealth})");
                    if (playerEntry.walkSpeed <= 0)
                        validationErrors.Add($"Player 'walkSpeed' <= 0 ({playerEntry.walkSpeed})");
                    if (playerEntry.runSpeed <= 0)
                        validationErrors.Add($"Player 'runSpeed' <= 0 ({playerEntry.runSpeed})");
                    if (playerEntry.airWalkSpeed <= 0)
                        validationErrors.Add($"Player 'airWalkSpeed' <= 0 ({playerEntry.airWalkSpeed})");
                    if (playerEntry.jumpImpulse <= 0)
                        validationErrors.Add($"Player 'jumpImpulse' <= 0 ({playerEntry.jumpImpulse})");
                    if (playerEntry.climbSpeed <= 0)
                        validationErrors.Add($"Player 'climbSpeed' <= 0 ({playerEntry.climbSpeed})");
                    if (playerEntry.baseAttackDamage <= 0)
                        validationErrors.Add($"Player 'baseAttackDamage' <= 0 ({playerEntry.baseAttackDamage})");
                    if (playerEntry.invincibilityTime < 0)
                        validationErrors.Add($"Player 'invincibilityTime' < 0 ({playerEntry.invincibilityTime})");
                }
            }

            // ===== 阶段 3A: PlayerAttackOverride 数据校验 =====
            // PlayerAttackOverride Sheet 必须存在（可为空）
            if (playerAttackOverrides == null)
            {
                validationErrors.Add("PlayerAttackOverride Sheet 不存在（阶段 3A 必需，可为空列表）");
            }
            else
            {
                foreach (var pao in playerAttackOverrides)
                {
                    // damageMultiplier 必须 > 0
                    if (pao.damageMultiplier <= 0)
                    {
                        validationErrors.Add($"PlayerAttackOverride '{pao.id}' 的 damageMultiplier <= 0 ({pao.damageMultiplier})");
                    }

                    // damageOverride 若填写必须 > 0
                    if (pao.damageOverride < 0)
                    {
                        validationErrors.Add($"PlayerAttackOverride '{pao.id}' 的 damageOverride < 0 ({pao.damageOverride})");
                    }

                    // targetId 不能为空
                    if (string.IsNullOrWhiteSpace(pao.targetId))
                    {
                        validationErrors.Add($"PlayerAttackOverride '{pao.id}' 的 targetId 为空");
                    }
                }

                // 唯一性校验：(playerId, targetType, targetId) 必须唯一
                var uniqueCheck = new HashSet<string>();
                foreach (var pao in playerAttackOverrides)
                {
                    string key = $"{pao.playerId}|{pao.targetType}|{pao.targetId}";
                    if (uniqueCheck.Contains(key))
                    {
                        validationErrors.Add($"PlayerAttackOverride 存在重复的三元组: playerId='{pao.playerId}', targetType={pao.targetType}, targetId='{pao.targetId}'");
                    }
                    uniqueCheck.Add(key);
                }
            }

            // 如果有校验错误，拒绝导入（0写入）
            if (validationErrors.Count > 0)
            {
                status = "ValidationFailed";
                message = $"数据校验失败，共 {validationErrors.Count} 个错误：\n" + string.Join("\n", validationErrors);
                notes.AddRange(validationErrors.ConvertAll(e => $"❌ {e}"));
                Debug.LogError($"[CastleDbImporter] {message}");
                return;
            }

            // ===== Step 5.2: 批量写入（全部校验通过后才执行）=====
            var dirtyProfiles = new List<EnemyTuningProfile>();
            var dirtyPlayerConfig = new List<PlayerConfig>();  // 阶段 3A

            foreach (var npc in npcs)
            {
                var profile = CreateOrUpdateProfileInternal(npc, notes);
                if (profile != null)
                {
                    dirtyProfiles.Add(profile);
                    successCount++;
                }
                else
                {
                    failureCount++;
                }
            }

            // ===== 阶段 3A: PlayerConfig 批量写入 =====
            // 使用前面已声明的 playerEntry
            if (playerEntry != null)
            {
                var playerConfig = CreateOrUpdatePlayerConfigInternal(playerEntry, playerAttackOverrides, notes);
                if (playerConfig != null)
                {
                    dirtyPlayerConfig.Add(playerConfig);
                }
            }

            // ===== Step 5.3: 统一SetDirty + 一次性SaveAssets（原子写）=====
            if (dirtyProfiles.Count > 0)
            {
                foreach (var profile in dirtyProfiles)
                {
                    EditorUtility.SetDirty(profile);
                }
                Debug.Log($"[CastleDbImporter] 已标记 {dirtyProfiles.Count} 个 EnemyTuningProfile 为 Dirty");
            }

            // 阶段 3A: PlayerConfig SetDirty
            if (dirtyPlayerConfig.Count > 0)
            {
                foreach (var config in dirtyPlayerConfig)
                {
                    EditorUtility.SetDirty(config);
                }
                Debug.Log($"[CastleDbImporter] 已标记 {dirtyPlayerConfig.Count} 个 PlayerConfig 为 Dirty");
            }

            // 统一保存
            if (dirtyProfiles.Count > 0 || dirtyPlayerConfig.Count > 0)
            {
                AssetDatabase.SaveAssets();
                Debug.Log($"[CastleDbImporter] 已保存所有资产到磁盘");
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
            WriteImportLog(startedAt, finishedAt, status, message, sourceDescription, versionInfo, npcs, detectionZones, successCount, failureCount, notes);
        }
    }

    /// <summary>
    /// 创建或更新Profile（内部方法，不调用 SaveAssets）
    /// Step 5.2: 只做创建/更新逻辑，返回 Profile 对象供批量写入
    /// </summary>
    private static EnemyTuningProfile CreateOrUpdateProfileInternal(NpcEntry npc, List<string> notes)
    {
        try
        {
            // ===== 注意：业务校验已在 ImportAll 的 Step 5.1 完成，这里不再重复校验 =====

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

            // ===== Step 5.2: 不调用 SetDirty/SaveAssets，由调用方批量处理 =====
            return profile;
        }
        catch (System.Exception ex)
        {
            string errorMsg = $"创建/更新Profile失败 ({npc.id}): {ex.Message}";
            Debug.LogError($"[CastleDbImporter] {errorMsg}");
            notes.Add($"❌ {errorMsg}");
            return null;  // 返回 null 表示失败
        }
    }

    /// <summary>
    /// 创建或更新 PlayerConfig（阶段 3A）
    /// 内部方法，不调用 SaveAssets
    /// </summary>
    private static PlayerConfig CreateOrUpdatePlayerConfigInternal(
        PlayerEntry player,
        List<PlayerAttackOverrideEntry> overrides,
        List<string> notes)
    {
        try
        {
            // 确保输出目录存在
            string configDir = Path.GetDirectoryName(PLAYER_CONFIG_PATH);
            if (!string.IsNullOrEmpty(configDir) && !Directory.Exists(configDir))
            {
                Directory.CreateDirectory(configDir);
            }

            // 查找或创建 PlayerConfig
            PlayerConfig config = AssetDatabase.LoadAssetAtPath<PlayerConfig>(PLAYER_CONFIG_PATH);

            if (config == null)
            {
                // 创建新 PlayerConfig
                config = ScriptableObject.CreateInstance<PlayerConfig>();
                AssetDatabase.CreateAsset(config, PLAYER_CONFIG_PATH);
                Debug.Log($"[CastleDbImporter] 创建新 PlayerConfig: {PLAYER_CONFIG_PATH}");
            }
            else
            {
                Debug.Log($"[CastleDbImporter] 更新现有 PlayerConfig: {PLAYER_CONFIG_PATH}");
            }

            // 应用 CastleDB 数据
            config.ApplyFromCastleDb(player, overrides);

            return config;
        }
        catch (System.Exception ex)
        {
            string errorMsg = $"创建/更新 PlayerConfig 失败 ({player?.id ?? "null"}): {ex.Message}";
            Debug.LogError($"[CastleDbImporter] {errorMsg}");
            notes.Add($"❌ {errorMsg}");
            return null;
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
        List<DetectionZoneEntry> detectionZones,
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
                    logContent.AppendLine($"  invincibleDuration={npc.invincibleDuration}, knockbackMultiplier={npc.knockbackMultiplier}, knockbackToPlayer={npc.knockbackToPlayer}");
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
                GenerateFieldMappingTable(startedAt, npcs, detectionZones);
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
    /// 2B: 新增 DetectionZone 映射信息
    /// </summary>
    private static void GenerateFieldMappingTable(System.DateTime timestamp, List<NpcEntry> npcs, List<DetectionZoneEntry> detectionZones)
    {
        try
        {
            var mappingContent = new System.Text.StringBuilder();

            mappingContent.AppendLine("# CastleDB 字段映射表");
            mappingContent.AppendLine();
            mappingContent.AppendLine($"生成时间: {timestamp:yyyy-MM-dd HH:mm:ss}");
            mappingContent.AppendLine();
            mappingContent.AppendLine("## NPC 字段映射关系");
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
            mappingContent.AppendLine("| knockbackMultiplier | knockbackMultiplier | knockbackMultiplier | _knockbackMultiplier | 击退倍率（Player→Monster：怪物受击） |");
            mappingContent.AppendLine("| knockbackToPlayer | knockbackToPlayer | knockbackToPlayer | _knockbackToPlayer → Attack.knockback | 击退缩放（Monster→Player：玩家受击） |");
            mappingContent.AppendLine("| enableDeathAnimation | enableDeathAnimation | enableDeathAnimation | _enableDeathAnimation | 启用死亡动画 |");
            mappingContent.AppendLine("| useLegacyLogicFallback | useLegacyLogicFallback | useLegacyLogicFallback | _useLegacyLogicFallback | 使用旧逻辑回退 |");
            mappingContent.AppendLine();

            // 2B: DetectionZone 字段映射表
            mappingContent.AppendLine("## DetectionZone 字段映射关系 (2B)");
            mappingContent.AppendLine();
            mappingContent.AppendLine("| CastleDB 字段 | DetectionZoneEntry (DTO) | Prefab 层级 | EnemyAgentBase (Runtime) | 说明 |");
            mappingContent.AppendLine("|--------------|-------------------------|-------------|-------------------------|------|");
            mappingContent.AppendLine("| id | id | - | - | 检测区唯一标识符（可选） |");
            mappingContent.AppendLine("| npcId | npcId | Profile_[npcId].asset | - | 关联的 NPC ID |");
            mappingContent.AppendLine("| role | role (int→enum) | zoneBindings[].role | DetectionZoneBinding.Role | 检测区角色 |");
            mappingContent.AppendLine("| childId | childId | 子物体名称 | zoneBindings[].zone.gameObject.name | Prefab 子物体名 |");
            mappingContent.AppendLine();
            mappingContent.AppendLine("### Role 枚举映射");
            mappingContent.AppendLine();
            mappingContent.AppendLine("| CastleDB 值 | 枚举名称 | 说明 |");
            mappingContent.AppendLine("|-------------|---------|------|");
            mappingContent.AppendLine("| 0 | PrimaryAttack | 主攻击检测（GetDetectedTargets 默认来源） |");
            mappingContent.AppendLine("| 1 | SecondaryAttack | 副攻击检测 |");
            mappingContent.AppendLine("| 2 | Cliff | 崖边检测 |");
            mappingContent.AppendLine("| 3 | Alert | 警戒范围 |");
            mappingContent.AppendLine("| 4 | Lookout | 视野范围 |");
            mappingContent.AppendLine("| 5 | Custom | 自定义用途 |");
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
                mappingContent.AppendLine($"knockbackToPlayer: {npc.knockbackToPlayer}");
                mappingContent.AppendLine($"enableDeathAnimation: {npc.enableDeathAnimation}");
                mappingContent.AppendLine($"useLegacyLogicFallback: {npc.useLegacyLogicFallback}");
                mappingContent.AppendLine("```");
                mappingContent.AppendLine();
            }

            // 2B: 输出检测区数据（按 NPC 分组）
            if (detectionZones != null && detectionZones.Count > 0)
            {
                mappingContent.AppendLine("## 当前导入的 DetectionZone 数据 (2B)");
                mappingContent.AppendLine();

                // 按 npcId 分组
                var groupedZones = detectionZones
                    .Where(z => !string.IsNullOrEmpty(z.npcId))
                    .GroupBy(z => z.npcId)
                    .OrderBy(g => g.Key);

                foreach (var group in groupedZones)
                {
                    mappingContent.AppendLine($"### NPC: {group.Key}");
                    mappingContent.AppendLine();
                    mappingContent.AppendLine("| Role | childId | 说明 |");
                    mappingContent.AppendLine("|------|---------|------|");

                    foreach (var zone in group)
                    {
                        mappingContent.AppendLine($"| {zone.GetRoleName()} | {zone.childId} | {GetRoleDescription(zone.role)} |");
                    }
                    mappingContent.AppendLine();
                }
            }
            else
            {
                mappingContent.AppendLine("## 当前导入的 DetectionZone 数据 (2B)");
                mappingContent.AppendLine();
                mappingContent.AppendLine("*未找到检测区数据*");
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

    /// <summary>
    /// 2B: 获取检测区角色的描述文本
    /// 使用统一的 DetectionZoneRoleMapper 避免多处 switch 漂移（P1-3.4）
    /// </summary>
    private static string GetRoleDescription(int roleIndex)
    {
        return DetectionZoneRoleMapper.GetRoleDescription(roleIndex);
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

    // ===== 2B: DetectionZone 聚合与校验支持 =====

    /// <summary>
    /// 按 NPC ID 聚合检测区数据
    /// 返回一个字典：npcId → List<DetectionZoneEntry>
    /// 用于 ValidateEnemyPrefabsWindow 校验 Prefab 的 zoneBindings 与 CastleDB 定义是否一致
    /// </summary>
    /// <param name="service">已初始化的 CastleDbService</param>
    /// <returns>按 npcId 聚合的检测区字典</returns>
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

    /// <summary>
    /// 加载 CastleDB 并返回按 NPC ID 聚合的检测区数据（便捷方法）
    /// 用于 ValidateEnemyPrefabsWindow 等外部工具
    /// </summary>
    /// <returns>按 npcId 聚合的检测区字典，加载失败返回空字典</returns>
    public static Dictionary<string, List<DetectionZoneEntry>> LoadDetectionZonesGroupedByNpcId()
    {
        var result = new Dictionary<string, List<DetectionZoneEntry>>();

        try
        {
            var asset = Resources.Load<TextAsset>(CASTLEDB_RESOURCE_PATH);
            if (asset == null)
            {
                Debug.LogWarning($"[CastleDbImporter] LoadDetectionZonesGroupedByNpcId: 无法加载 CastleDB 资源");
                return result;
            }

            var source = new CastleDbJsonSource(asset);
            var service = new CastleDbService();
            service.Initialize(source);

            return GetDetectionZonesGroupedByNpcId(service);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[CastleDbImporter] LoadDetectionZonesGroupedByNpcId 失败: {ex.Message}");
            return result;
        }
    }

    /// <summary>
    /// 获取 NPC ID 与 Profile 文件名的映射
    /// Profile 文件命名规则：Profile_{npcId}.asset
    /// </summary>
    /// <returns>npcId → Profile 资源路径 的字典</returns>
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
            // Profile_M_Knight → M_Knight
            if (fileName.StartsWith("Profile_"))
            {
                string npcId = fileName.Substring("Profile_".Length);
                result[npcId] = file.Replace("\\", "/");
            }
        }

        return result;
    }
}
