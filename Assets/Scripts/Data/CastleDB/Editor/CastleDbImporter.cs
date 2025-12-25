using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CastleDB.Runtime;
using CastleDB.Editor;

/// <summary>
/// CastleDB 导入工具
/// 从CastleDB数据通过CastleDbService导入NPC数据到EnemyTuningProfile资源
///
/// 功能：
/// - 使用CastleDbService读取NPC数据
/// - 创建/更新EnemyTuningProfile ScriptableObject
/// - 支持 0.2 Legacy 模式和 0.4 Provider 模式
/// - 生成导入日志
///
/// 0.4 版本更新：
/// - 支持 Provider 流程（MonsterDataProvider）
/// - 优先尝试 0.4 数据源（Assets/Resources/Data/MonsterSystem.cdb）
/// - 回退到 0.2 Legacy 数据源（Assets/Resources/Data/CastleDbDemo/MonsterSystem.cdb）
///
/// 使用方式：
/// 1. 在Unity菜单中选择 Tools > CastleDB > Import All
/// 2. 工具会自动检测数据源版本并选择相应流程
/// 3. 为每个NPC创建或更新对应的Profile资源
/// 4. 生成导入日志到 Logs/CastleDbImport.log
/// </summary>
public class CastleDbImporter
{
    // 配置常量
    private const string CASTLEDB_RESOURCE_PATH = "Data/CastleDbDemo/MonsterSystem";  // 0.2 Legacy 路径
    private const string CASTLEDB_V03_RESOURCE_PATH = "Data/MonsterSystem";  // 0.4 新路径
    private const string PROFILE_OUTPUT_DIR = "Assets/Resources/Profiles";
    private const string PLAYER_CONFIG_PATH = "Assets/Resources/Config/PlayerConfig.asset";
    private const string ABILITY_CATALOG_PATH = "Assets/Resources/Config/AbilityCatalog.asset";  // 阶段 3B
    private const string IMPORT_LOG_DIR = "Logs";
    private const string IMPORT_LOG_FILE = "Logs/CastleDbImport.log";

    // 版本检查
    private const string EXPECTED_SCHEMA_VERSION_LEGACY = "0.2";
    // 注意：0.4+ 版本使用 CdbDataProviderRegistry.ExpectedSchemaVersion（单一真相源）

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

        // ===== 0.4 版本：多文件 Provider 流程 =====
        // 1. 确保 Provider 已注册
        CdbProviderBootstrap.EnsureRegistered();

        // 2. 使用 Coordinator 执行 Import All
        var coordinator = new CdbImportCoordinator(CdbDataProviderRegistry.Instance);
        var result = coordinator.ImportAll();

        // 3. 输出日志
        var logContent = result.GetFormattedLog();
        Debug.Log($"[CastleDbImporter] Import All {(result.IsSuccess ? "成功" : "失败")}");

        // 写入日志文件
        string absLogPath = GetAbsolutePath(IMPORT_LOG_FILE);
        SafeWriteAllText(absLogPath, logContent);

        // 如果失败，显示错误对话框
        if (!result.IsSuccess)
        {
            EditorUtility.DisplayDialog(
                "Import All 失败",
                "导入过程中发生错误，请查看 Console 和日志文件了解详情。\n\n" +
                $"日志位置：{IMPORT_LOG_FILE}",
                "确定"
            );
        }
        else
        {
            Debug.Log($"[CastleDbImporter] 导入完成，日志已保存至：{IMPORT_LOG_FILE}");
        }
    }

    /// <summary>
    /// 0.4 版本：尝试使用 Provider 流程导入
    /// 返回 true 表示成功处理（无论导入成功还是失败），false 表示需要回退到 Legacy
    /// </summary>
    private static bool TryImportWithProvider()
    {
        // 1. 尝试加载 0.4 版本数据源
        var asset = Resources.Load<TextAsset>(CASTLEDB_V03_RESOURCE_PATH);
        if (asset == null)
        {
            Debug.Log($"[CastleDbImporter] 未找到 0.4 数据源：{CASTLEDB_V03_RESOURCE_PATH}，尝试 Legacy 路径");
            return false;
        }

        // 2. 解析并检查是否有 providerId（判断是否为 0.4 格式）
        var source = new CastleDbJsonSource(asset);
        var root = source.ReadCastleDbJson();
        if (root == null)
        {
            Debug.LogWarning("[CastleDbImporter] 无法解析 0.4 数据源");
            return false;
        }

        // 查找 Meta Sheet 中的 providerId
        var metaSheet = root.sheets.Find(s => s.name == "Meta");
        if (metaSheet == null || metaSheet.lines == null)
        {
            Debug.Log("[CastleDbImporter] 数据源无 Meta Sheet，回退到 Legacy");
            return false;
        }

        // 解析 Meta 条目
        var metaEntries = new List<MetaEntry>();
        foreach (var line in metaSheet.lines)
        {
            if (line is Dictionary<string, object> dict)
            {
                metaEntries.Add(new MetaEntry
                {
                    key = dict.TryGetValue("key", out var k) ? k?.ToString() ?? "" : "",
                    value = dict.TryGetValue("value", out var v) ? v?.ToString() ?? "" : ""
                });
            }
        }

        var descriptor = CdbModuleDescriptor.FromMetaEntries(metaEntries, $"Assets/Resources/{CASTLEDB_V03_RESOURCE_PATH}.cdb");

        // 检查是否有 providerId（0.4 格式标志）
        if (descriptor.IsLegacy || string.IsNullOrEmpty(descriptor.ProviderId))
        {
            Debug.Log("[CastleDbImporter] 数据源无 providerId，回退到 Legacy");
            return false;
        }

        // 3. 使用 Provider 流程导入
        Debug.Log($"[CastleDbImporter] 检测到 0.4 格式数据源：providerId={descriptor.ProviderId}, schemaVersion={descriptor.SchemaVersion}");
        return ImportWithProvider(source, descriptor);
    }

    /// <summary>
    /// 使用 Provider 流程执行导入
    /// </summary>
    private static bool ImportWithProvider(CastleDbJsonSource source, CdbModuleDescriptor descriptor)
    {
        var startedAt = System.DateTime.Now;
        var notes = new List<string>();
        string status = "Failed";
        string message = null;

        try
        {
            // 1. 确保 Provider 已注册
            CdbProviderBootstrap.EnsureRegistered();

            var registry = CdbDataProviderRegistry.Instance;

            // 2. 校验 schemaVersion
            if (descriptor.SchemaVersion != CdbDataProviderRegistry.ExpectedSchemaVersion)
            {
                status = "VersionMismatch";
                message = $"Schema 版本不匹配。期望: {CdbDataProviderRegistry.ExpectedSchemaVersion}, 实际: {descriptor.SchemaVersion}";
                Debug.LogError($"[CastleDbImporter] {message}");
                WriteProviderImportLog(startedAt, status, message, descriptor, notes);
                return true;  // 已处理，不回退
            }

            // 3. 注册描述符
            registry.RegisterDescriptor(descriptor);

            // 4. 获取 Provider
            var provider = registry.GetProvider(descriptor.ProviderId);
            if (provider == null)
            {
                status = "ProviderNotFound";
                message = $"未找到 Provider：{descriptor.ProviderId}";
                Debug.LogError($"[CastleDbImporter] {message}");
                WriteProviderImportLog(startedAt, status, message, descriptor, notes);
                return true;
            }

            // 5. 注意：备份已由 CdbImportCoordinator 统一管理（Phase 5）
            notes.Add("ℹ️ 备份由 Coordinator 管理");

            // 6. 初始化 Provider
            Debug.Log($"[CastleDbImporter] 初始化 Provider：{descriptor.ProviderId}");
            provider.Initialize(source, descriptor);
            notes.Add($"✅ Provider 初始化完成");

            // 7. 校验
            Debug.Log($"[CastleDbImporter] 校验数据：{descriptor.ProviderId}");
            var validationErrors = provider.Validate(descriptor);
            if (validationErrors.Count > 0)
            {
                status = "ValidationFailed";
                message = $"数据校验失败，共 {validationErrors.Count} 个错误";
                foreach (var error in validationErrors)
                {
                    notes.Add($"❌ {error}");
                    Debug.LogError($"[CastleDbImporter] 校验错误：{error}");
                }
                WriteProviderImportLog(startedAt, status, message, descriptor, notes);
                return true;
            }
            notes.Add($"✅ 数据校验通过");

            // 8. 导入
            Debug.Log($"[CastleDbImporter] 导入数据：{descriptor.ProviderId}");
            var importResult = provider.Import(descriptor);
            importResult.LogToConsole();

            // 9. 收集导入结果
            foreach (var info in importResult.InfoLogs)
            {
                notes.Add($"ℹ️ {info}");
            }
            foreach (var warning in importResult.Warnings)
            {
                notes.Add($"⚠️ {warning}");
            }

            if (!importResult.Success)
            {
                status = "ImportFailed";
                message = $"导入失败：{importResult.Errors.Count} 个错误";
                foreach (var error in importResult.Errors)
                {
                    notes.Add($"❌ {error}");
                }
                WriteProviderImportLog(startedAt, status, message, descriptor, notes);
                return true;
            }

            // 10. 统一保存资产
            if (importResult.DirtyAssets.Count > 0)
            {
                AssetDatabase.SaveAssets();
                Debug.Log($"[CastleDbImporter] 已保存 {importResult.DirtyAssets.Count} 个资产");
            }

            // 11. 处理其他模块（Player/Ability 仍使用旧逻辑，因为对应 Provider 尚未实现）
            ImportPlayerAndAbilityLegacy(source, notes);

            // 12. 刷新资源
            AssetDatabase.Refresh();

            status = "Success";
            message = $"Provider 导入成功：创建 {importResult.CreatedCount}，更新 {importResult.UpdatedCount}";
            Debug.Log($"[CastleDbImporter] {message}");

            WriteProviderImportLog(startedAt, status, message, descriptor, notes);

            // 提示用户同步 Prefab
            PromptUserToSyncPrefabs(importResult.CreatedCount + importResult.UpdatedCount, 0);

            return true;
        }
        catch (System.Exception ex)
        {
            status = "Exception";
            message = $"{ex.Message}\n{ex.StackTrace}";
            Debug.LogError($"[CastleDbImporter] Provider 导入异常：{ex.Message}");
            WriteProviderImportLog(startedAt, status, message, descriptor, notes);
            return true;  // 已处理，不回退
        }
    }

    /// <summary>
    /// 0.4 过渡期：使用旧逻辑导入 Player 和 Ability（待 Phase 4 实现对应 Provider 后移除）
    /// </summary>
    private static void ImportPlayerAndAbilityLegacy(CastleDbJsonSource source, List<string> notes)
    {
        try
        {
            var service = new CastleDbService();
            // 不校验版本，直接读取数据
            var root = source.ReadCastleDbJson();
            if (root == null) return;

            // 手动解析 Player/Ability 数据（跳过版本检查）
            var players = new List<PlayerEntry>();
            var playerAttackOverrides = new List<PlayerAttackOverrideEntry>();
            var abilities = new List<AbilityEntry>();

            foreach (var sheet in root.sheets)
            {
                switch (sheet.name)
                {
                    case "Player":
                        players = ParsePlayerEntries(sheet.lines);
                        break;
                    case "PlayerAttackOverride":
                        playerAttackOverrides = ParsePlayerAttackOverrideEntries(sheet.lines);
                        break;
                    case "Ability":
                        abilities = ParseAbilityEntries(sheet.lines);
                        break;
                }
            }

            // 处理 PlayerConfig
            var playerEntry = players.Find(p => p.id == "player");
            if (playerEntry != null)
            {
                var playerConfig = CreateOrUpdatePlayerConfigInternal(playerEntry, playerAttackOverrides, notes);
                if (playerConfig != null)
                {
                    EditorUtility.SetDirty(playerConfig);
                    ApplyProjectileDamageToPrefabs(playerConfig, playerAttackOverrides, notes);
                }
            }

            // 处理 AbilityCatalog
            if (abilities.Count > 0)
            {
                var abilityCatalog = CreateOrUpdateAbilityCatalogInternal(abilities, notes);
                if (abilityCatalog != null)
                {
                    EditorUtility.SetDirty(abilityCatalog);
                }
            }

            notes.Add("✅ Player/Ability 数据已处理（Legacy 逻辑）");
        }
        catch (System.Exception ex)
        {
            notes.Add($"⚠️ Player/Ability 处理失败：{ex.Message}");
            Debug.LogWarning($"[CastleDbImporter] Player/Ability 处理失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 写入 Provider 导入日志
    /// </summary>
    private static void WriteProviderImportLog(
        System.DateTime startedAt,
        string status,
        string message,
        CdbModuleDescriptor descriptor,
        List<string> notes)
    {
        var finishedAt = System.DateTime.Now;
        var logContent = new System.Text.StringBuilder();

        logContent.AppendLine("════════════════════════════════════════════════════════");
        logContent.AppendLine("         CastleDB Import Session Log (Provider Mode)");
        logContent.AppendLine("════════════════════════════════════════════════════════");
        logContent.AppendLine();
        logContent.AppendLine($"开始时间: {startedAt:yyyy-MM-dd HH:mm:ss}");
        logContent.AppendLine($"结束时间: {finishedAt:yyyy-MM-dd HH:mm:ss}");
        logContent.AppendLine($"耗时: {(finishedAt - startedAt).TotalSeconds:F2} 秒");
        logContent.AppendLine();
        logContent.AppendLine($"状态: {status}");
        logContent.AppendLine($"Provider: {descriptor?.ProviderId ?? "N/A"}");
        logContent.AppendLine($"Schema 版本: {descriptor?.SchemaVersion ?? "N/A"}");
        logContent.AppendLine($"资源路径: {descriptor?.ResourcePath ?? "N/A"}");
        logContent.AppendLine();

        if (!string.IsNullOrEmpty(message))
        {
            logContent.AppendLine("════════════════════════════════════════════════════════");
            logContent.AppendLine("         消息");
            logContent.AppendLine("════════════════════════════════════════════════════════");
            logContent.AppendLine(message);
            logContent.AppendLine();
        }

        if (notes.Count > 0)
        {
            logContent.AppendLine("════════════════════════════════════════════════════════");
            logContent.AppendLine("         详细日志");
            logContent.AppendLine("════════════════════════════════════════════════════════");
            foreach (var note in notes)
            {
                logContent.AppendLine($"• {note}");
            }
            logContent.AppendLine();
        }

        logContent.AppendLine("════════════════════════════════════════════════════════");
        logContent.AppendLine("         End of Log");
        logContent.AppendLine("════════════════════════════════════════════════════════");

        string absLogPath = GetAbsolutePath(IMPORT_LOG_FILE);
        SafeWriteAllText(absLogPath, logContent.ToString());
    }

    /// <summary>
    /// Legacy (0.2) 导入流程
    /// </summary>
    private static void ImportAllLegacy()
    {
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
            if (versionInfoObj == null || versionInfoObj.schemaVersion != EXPECTED_SCHEMA_VERSION_LEGACY)
            {
                status = "VersionMismatch";
                message = $"版本不匹配。期望: {EXPECTED_SCHEMA_VERSION_LEGACY}, 实际: {versionInfoObj?.schemaVersion ?? "null"}";
                versionInfo = $"{versionInfoObj?.schemaVersion ?? "null"}";
                Debug.LogError($"[CastleDbImporter] {message}");
                return;
            }
            versionInfo = versionInfoObj.schemaVersion;

            // 4. 注意：备份已由 CdbImportCoordinator 统一管理（Phase 5）
            // Legacy 流程不再单独备份

            // 5. 获取所有NPC数据
            npcs = service.GetAllNpcs();
            detectionZones = service.GetAllDetectionZones();
            var players = service.GetAllPlayers();
            var playerAttackOverrides = service.GetAllPlayerAttackOverrides();
            var abilities = service.GetAllAbilities();  // 阶段 3B

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

            // ===== 阶段 3B: Ability Sheet 数据校验 =====
            // Ability Sheet 必须存在（可为空）
            if (abilities == null)
            {
                validationErrors.Add("Ability Sheet 不存在（阶段 3B 必需，可为空列表）");
            }
            else
            {
                // 1. Registry 校验：所有 ability.id 必须在 AbilityRegistry 中注册
                foreach (var ability in abilities)
                {
                    if (!AbilityRegistry.IsRegistered(ability.id))
                    {
                        validationErrors.Add($"Ability '{ability.id}' 未在 AbilityRegistry 中注册（必须先在 AbilityRegistry.registeredIds 中添加）");
                    }
                }

                // 2. paramsJson 校验：若非空，必须是合法 JSON 对象（不允许 primitives/arrays）
                foreach (var ability in abilities)
                {
                    if (!string.IsNullOrWhiteSpace(ability.paramsJson))
                    {
                        // 使用公开的 CastleDbJsonUtil 校验 JSON 对象
                        var parsed = CastleDB.Runtime.CastleDbJsonUtil.TryParseJsonObject(ability.paramsJson);

                        if (parsed == null)
                        {
                            // 解析失败或不是对象（可能是 primitive/array）
                            validationErrors.Add($"Ability '{ability.id}' 的 paramsJson 必须是 JSON 对象（{{...}}），当前值: {ability.paramsJson}");
                        }
                    }
                }

                // 3. 最小覆盖率校验：至少要有 5 个条目（对应 5 个基础能力）
                if (abilities.Count < 5)
                {
                    validationErrors.Add($"Ability Sheet 至少需要 5 个条目（基础能力覆盖），当前只有 {abilities.Count} 个");
                }

                // 4. hookType 覆盖率校验：每个 hookType 至少要有 1 个启用的能力
                var hookTypeCoverage = new Dictionary<int, int>();
                for (int i = 0; i <= 4; i++) // 0=Move, 1=Run, 2=Jump, 3=Attack, 4=RangedAttack
                {
                    hookTypeCoverage[i] = 0;
                }

                foreach (var ability in abilities)
                {
                    if (ability.enabled && hookTypeCoverage.ContainsKey(ability.hookType))
                    {
                        hookTypeCoverage[ability.hookType]++;
                    }
                }

                foreach (var kvp in hookTypeCoverage)
                {
                    if (kvp.Value == 0)
                    {
                        string hookTypeName = ((AbilityHookType)kvp.Key).ToString();
                        validationErrors.Add($"Ability Sheet 的 hookType {hookTypeName} 没有任何启用的能力（至少需要 1 个）");
                    }
                }

                // 5. 唯一性校验：同一个 hookType 下，priority 必须唯一（用于排序和调试）
                var priorityCheck = new Dictionary<int, HashSet<int>>();
                for (int i = 0; i <= 4; i++)
                {
                    priorityCheck[i] = new HashSet<int>();
                }

                foreach (var ability in abilities)
                {
                    if (priorityCheck.ContainsKey(ability.hookType))
                    {
                        if (priorityCheck[ability.hookType].Contains(ability.priority))
                        {
                            string hookTypeName = ((AbilityHookType)ability.hookType).ToString();
                            validationErrors.Add($"Ability Sheet 的 hookType {hookTypeName} 中存在重复的 priority {ability.priority}（必须唯一以保证执行顺序确定）");
                        }
                        priorityCheck[ability.hookType].Add(ability.priority);
                    }
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
            var dirtyAbilityCatalog = new List<AbilityCatalog>();  // 阶段 3B

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

                    // ===== 阶段 3A: Projectile prefab 级一次性赋值（按文档要求）=====
                    ApplyProjectileDamageToPrefabs(playerConfig, playerAttackOverrides, notes);
                }
            }

            // ===== 阶段 3B: AbilityCatalog 批量写入 =====
            if (abilities != null && abilities.Count > 0)
            {
                var abilityCatalog = CreateOrUpdateAbilityCatalogInternal(abilities, notes);
                if (abilityCatalog != null)
                {
                    dirtyAbilityCatalog.Add(abilityCatalog);
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

            // 阶段 3B: AbilityCatalog SetDirty
            if (dirtyAbilityCatalog.Count > 0)
            {
                foreach (var catalog in dirtyAbilityCatalog)
                {
                    EditorUtility.SetDirty(catalog);
                }
                Debug.Log($"[CastleDbImporter] 已标记 {dirtyAbilityCatalog.Count} 个 AbilityCatalog 为 Dirty");
            }

            // 统一保存
            if (dirtyProfiles.Count > 0 || dirtyPlayerConfig.Count > 0 || dirtyAbilityCatalog.Count > 0)
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

            // 阶段4：Import All 与 Sync 联动提示
            if (status == "Success" || status == "CompletedWithFailures")
            {
                PromptUserToSyncPrefabs(successCount, failureCount);
            }
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

            // 阶段 3A: 记录玩家字段映射导入摘要
            notes.Add($"✅ PlayerConfig: id={player.id}");
            notes.Add($"   │ 基础属性映射:");
            notes.Add($"   │  • maxHealth: {player.maxHealth}");
            notes.Add($"   │  • invincibilityTime: {player.invincibilityTime}");
            notes.Add($"   │  • walkSpeed: {player.walkSpeed}");
            notes.Add($"   │  • runSpeed: {player.runSpeed}");
            notes.Add($"   │  • airWalkSpeed: {player.airWalkSpeed}");
            notes.Add($"   │  • jumpImpulse: {player.jumpImpulse}");
            notes.Add($"   │  • climbSpeed: {player.climbSpeed}");
            notes.Add($"   │  • baseAttackDamage: {player.baseAttackDamage}");
            notes.Add($"   │ 攻击覆盖配置: {overrides.Count} 条");

            // 列出所有攻击覆盖配置
            foreach (var ov in overrides)
            {
                string targetTypeStr = ov.targetType == 0 ? "Hitbox" : "Projectile";
                string overrideInfo = ov.damageOverride > 0
                    ? $"override={ov.damageOverride}"
                    : $"multiplier={ov.damageMultiplier}";
                notes.Add($"   │  • {ov.id}: {targetTypeStr} '{ov.targetId}' ({overrideInfo})");
            }

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
    /// 创建或更新 AbilityCatalog（阶段 3B）
    /// 内部方法，不调用 SaveAssets
    /// </summary>
    private static AbilityCatalog CreateOrUpdateAbilityCatalogInternal(
        List<AbilityEntry> abilities,
        List<string> notes)
    {
        try
        {
            // 确保输出目录存在
            string catalogDir = Path.GetDirectoryName(ABILITY_CATALOG_PATH);
            if (!string.IsNullOrEmpty(catalogDir) && !Directory.Exists(catalogDir))
            {
                Directory.CreateDirectory(catalogDir);
            }

            // 查找或创建 AbilityCatalog
            AbilityCatalog catalog = AssetDatabase.LoadAssetAtPath<AbilityCatalog>(ABILITY_CATALOG_PATH);

            if (catalog == null)
            {
                // 创建新 AbilityCatalog
                catalog = ScriptableObject.CreateInstance<AbilityCatalog>();
                AssetDatabase.CreateAsset(catalog, ABILITY_CATALOG_PATH);
                Debug.Log($"[CastleDbImporter] 创建新 AbilityCatalog: {ABILITY_CATALOG_PATH}");
            }
            else
            {
                Debug.Log($"[CastleDbImporter] 更新现有 AbilityCatalog: {ABILITY_CATALOG_PATH}");
            }

            // 应用 CastleDB 数据
            catalog.ApplyFromCastleDb(abilities);

            // 阶段 3B: 记录能力导入摘要
            notes.Add($"✅ AbilityCatalog: {abilities.Count} 个能力配置");

            // 统计每个 hookType 的能力数量
            var hookTypeStats = new Dictionary<AbilityHookType, int>();
            foreach (AbilityHookType hookType in System.Enum.GetValues(typeof(AbilityHookType)))
            {
                hookTypeStats[hookType] = 0;
            }

            foreach (var ability in abilities)
            {
                if (ability.enabled)
                {
                    hookTypeStats[(AbilityHookType)ability.hookType]++;
                }
            }

            notes.Add($"   │ 能力分布:");
            foreach (var kvp in hookTypeStats)
            {
                notes.Add($"   │  • {kvp.Key}: {kvp.Value} 个启用");
            }

            // 列出所有能力配置
            notes.Add($"   │ 能力详情:");
            foreach (var ability in abilities)
            {
                string enabledStr = ability.enabled ? "✓" : "✗";
                string hookTypeName = ((AbilityHookType)ability.hookType).ToString();
                notes.Add($"   │  • [{enabledStr}] {ability.id}: {hookTypeName}, priority={ability.priority}");
            }

            return catalog;
        }
        catch (System.Exception ex)
        {
            string errorMsg = $"创建/更新 AbilityCatalog 失败: {ex.Message}";
            Debug.LogError($"[CastleDbImporter] {errorMsg}");
            notes.Add($"❌ {errorMsg}");
            return null;
        }
    }

    /// <summary>
    /// 阶段 3A: 应用 Projectile 伤害到 prefab（prefab 级一次性赋值）
    /// 遍历 PlayerAttackOverride 中 targetType=1 的条目，根据 Resources 路径加载 prefab，
    /// 直接修改 prefab 的 Projectile.damage 字段
    /// </summary>
    private static void ApplyProjectileDamageToPrefabs(
        PlayerConfig playerConfig,
        List<PlayerAttackOverrideEntry> overrides,
        List<string> notes)
    {
        if (playerConfig == null || overrides == null)
            return;

        // 筛选出 Projectile 类型的覆盖配置
        var projectileOverrides = overrides.FindAll(o => o.targetType == 1);

        if (projectileOverrides.Count == 0)
        {
            notes.Add("   │ Projectile prefab 处理: 无 Projectile 覆盖配置");
            return;
        }

        notes.Add($"   │ Projectile prefab 级赋值: {projectileOverrides.Count} 个");

        int successCount = 0;
        int failureCount = 0;

        foreach (var ov in projectileOverrides)
        {
            try
            {
                // 从 Resources 路径加载 prefab
                GameObject prefab = Resources.Load<GameObject>(ov.targetId);
                if (prefab == null)
                {
                    string errorMsg = $"Projectile prefab 未找到: '{ov.targetId}'";
                    Debug.LogWarning($"[CastleDbImporter] {errorMsg}");
                    notes.Add($"   │  ⚠️ {ov.id}: {errorMsg}");
                    failureCount++;
                    continue;
                }

                // 获取 Projectile 组件
                Projectile projectile = prefab.GetComponent<Projectile>();
                if (projectile == null)
                {
                    string errorMsg = $"Projectile 组件未找到: '{ov.targetId}'";
                    Debug.LogWarning($"[CastleDbImporter] {errorMsg}");
                    notes.Add($"   │  ⚠️ {ov.id}: {errorMsg}");
                    failureCount++;
                    continue;
                }

                // 计算最终伤害
                int finalDamage = playerConfig.CalculateFinalDamage(
                    PlayerAttackOverride.TargetType.Projectile,
                    ov.targetId);

                // 记录原始值
                int originalDamage = projectile.damage;

                // 修改 prefab 的 damage 值
                projectile.damage = finalDamage;

                // 标记 prefab 为 dirty
                EditorUtility.SetDirty(prefab);

                successCount++;
                notes.Add($"   │  ✅ {ov.id}: '{ov.targetId}' damage {originalDamage} → {finalDamage}");

                Debug.Log($"[CastleDbImporter] Projectile prefab 已更新: {ov.targetId} damage={finalDamage}");
            }
            catch (System.Exception ex)
            {
                string errorMsg = $"处理 Projectile prefab 失败: {ov.id} - {ex.Message}";
                Debug.LogError($"[CastleDbImporter] {errorMsg}");
                notes.Add($"   │  ❌ {ov.id}: {errorMsg}");
                failureCount++;
            }
        }

        if (successCount > 0 || failureCount > 0)
        {
            notes.Add($"   │ Projectile prefab 处理完成: 成功 {successCount}, 失败 {failureCount}");
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
    /// 从最新的备份目录恢复 Profiles、PlayerConfig 和 AbilityCatalog（阶段 3A、3B）
    /// </summary>
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
            // 使用 CdbImportCoordinator 的回滚逻辑
            string absBackupDir = GetAbsolutePath("Logs/CastleDBImport/Backups");
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

            Debug.Log($"[CastleDbImporter] 调用 CdbImportCoordinator 回滚到备份: {backupName}");

            // 使用 Coordinator 的 RestoreFromBackup 通过反射调用（因为它是 private）
            var coordinatorType = typeof(CdbImportCoordinator);
            var restoreMethod = coordinatorType.GetMethod("RestoreFromBackup",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (restoreMethod == null)
            {
                Debug.LogError("[CastleDbImporter] 无法找到 CdbImportCoordinator.RestoreFromBackup 方法");
                EditorUtility.DisplayDialog("回滚失败", "内部错误：无法找到回滚方法", "确定");
                return;
            }

            var coordinator = new CdbImportCoordinator(CdbDataProviderRegistry.Instance);

            // 提取时间戳（backupName 格式为 "Backup_yyyyMMdd_HHmmss"）
            string timestamp = backupName.Replace("Backup_", "");
            restoreMethod.Invoke(coordinator, new object[] { timestamp });

            AssetDatabase.Refresh();

            Debug.Log($"[CastleDbImporter] 回滚完成！备份：{backupName}");
            EditorUtility.DisplayDialog(
                "回滚成功",
                $"已从备份 {backupName} 恢复资产。",
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

    /// <summary>
    /// 阶段4：Import All 完成后提示用户运行 Sync NPC Prefabs
    /// </summary>
    private static void PromptUserToSyncPrefabs(int successCount, int failureCount)
    {
        // 使用 EditorApplication.delayCall 延迟弹窗，避免阻塞 Import 流程
        EditorApplication.delayCall += () =>
        {
            string title = "导入完成";
            string message;

            if (failureCount == 0)
            {
                message = $"成功导入 {successCount} 个 NPC 配置！\n\n" +
                         "建议立即运行 'Sync NPC Prefabs' 来更新 Prefab 资产。\n\n" +
                         "是否现在同步？";
            }
            else
            {
                message = $"导入完成（成功: {successCount}, 失败: {failureCount}）\n\n" +
                         "建议运行 'Sync NPC Prefabs' 来同步成功的 NPC。\n\n" +
                         "是否现在同步？";
            }

            bool shouldSync = EditorUtility.DisplayDialog(
                title,
                message,
                "立即同步",
                "稍后手动同步"
            );

            if (shouldSync)
            {
                Debug.Log("[CastleDbImporter] 用户选择立即同步 Prefabs，正在打开同步工具...");
                // 打开 Sync NPC Prefabs 窗口
                EditorApplication.ExecuteMenuItem("Tools/CastleDB/Sync NPC Prefabs");
            }
            else
            {
                Debug.Log("[CastleDbImporter] 用户选择稍后手动同步。提示：请记得运行 'Tools → CastleDB → Sync NPC Prefabs'");
            }
        };
    }

    // ===== 0.4 版本：辅助解析方法 =====

    /// <summary>
    /// 解析 Player Sheet 数据
    /// </summary>
    private static List<PlayerEntry> ParsePlayerEntries(List<object> lines)
    {
        var result = new List<PlayerEntry>();
        if (lines == null) return result;

        foreach (var line in lines)
        {
            if (line is Dictionary<string, object> dict)
            {
                result.Add(new PlayerEntry
                {
                    id = GetStringValue(dict, "id"),
                    maxHealth = GetFloatValue(dict, "maxHealth"),
                    invincibilityTime = GetFloatValue(dict, "invincibilityTime"),
                    walkSpeed = GetFloatValue(dict, "walkSpeed"),
                    runSpeed = GetFloatValue(dict, "runSpeed"),
                    airWalkSpeed = GetFloatValue(dict, "airWalkSpeed"),
                    jumpImpulse = GetFloatValue(dict, "jumpImpulse"),
                    climbSpeed = GetFloatValue(dict, "climbSpeed"),
                    baseAttackDamage = GetFloatValue(dict, "baseAttackDamage")
                });
            }
        }
        return result;
    }

    /// <summary>
    /// 解析 PlayerAttackOverride Sheet 数据
    /// </summary>
    private static List<PlayerAttackOverrideEntry> ParsePlayerAttackOverrideEntries(List<object> lines)
    {
        var result = new List<PlayerAttackOverrideEntry>();
        if (lines == null) return result;

        foreach (var line in lines)
        {
            if (line is Dictionary<string, object> dict)
            {
                result.Add(new PlayerAttackOverrideEntry
                {
                    id = GetStringValue(dict, "id"),
                    playerId = GetStringValue(dict, "playerId"),
                    targetType = GetIntValue(dict, "targetType"),
                    targetId = GetStringValue(dict, "targetId"),
                    damageMultiplier = GetFloatValue(dict, "damageMultiplier"),
                    damageOverride = GetIntValue(dict, "damageOverride")
                });
            }
        }
        return result;
    }

    /// <summary>
    /// 解析 Ability Sheet 数据
    /// </summary>
    private static List<AbilityEntry> ParseAbilityEntries(List<object> lines)
    {
        var result = new List<AbilityEntry>();
        if (lines == null) return result;

        foreach (var line in lines)
        {
            if (line is Dictionary<string, object> dict)
            {
                result.Add(new AbilityEntry
                {
                    id = GetStringValue(dict, "id"),
                    hookType = GetIntValue(dict, "hookType"),
                    priority = GetIntValue(dict, "priority"),
                    enabled = GetBoolValue(dict, "enabled"),
                    paramsJson = GetStringValue(dict, "paramsJson")
                });
            }
        }
        return result;
    }

    // ===== 字典辅助方法 =====

    private static string GetStringValue(Dictionary<string, object> dict, string key)
    {
        if (dict.TryGetValue(key, out var value))
        {
            return value?.ToString() ?? "";
        }
        return "";
    }

    private static float GetFloatValue(Dictionary<string, object> dict, string key)
    {
        if (dict.TryGetValue(key, out var value))
        {
            if (value is float f) return f;
            if (value is double d) return (float)d;
            if (value is int i) return i;
            if (float.TryParse(value?.ToString(), out var result)) return result;
        }
        return 0f;
    }

    private static int GetIntValue(Dictionary<string, object> dict, string key)
    {
        if (dict.TryGetValue(key, out var value))
        {
            if (value is int i) return i;
            if (value is long l) return (int)l;
            if (int.TryParse(value?.ToString(), out var result)) return result;
        }
        return 0;
    }

    private static bool GetBoolValue(Dictionary<string, object> dict, string key)
    {
        if (dict.TryGetValue(key, out var value))
        {
            if (value is bool b) return b;
            if (bool.TryParse(value?.ToString(), out var result)) return result;
        }
        return false;
    }
}
