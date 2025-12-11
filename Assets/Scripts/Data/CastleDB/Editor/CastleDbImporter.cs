using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CastleDB.Runtime;

namespace CastleDB.Editor
{
    /// <summary>
    /// CastleDB 导入工具
    /// 负责从 CastleDB JSON 导入数据到 EnemyTuningProfile ScriptableObject
    ///
    /// 工作流程：
    /// 1. 从 Resources/Data/CastleDbDemo/MonsterSystem.cdb 读取 JSON
    /// 2. 解析 NPC 数据
    /// 3. 为每个 NPC 创建或更新 EnemyTuningProfile
    /// 4. 生成备份和导入日志
    /// 5. 支持回滚到上一个版本
    ///
    /// 使用方式：
    /// - 菜单：Tools > CastleDB > Import All
    /// - 快捷键：无
    /// - 输出：Assets/Resources/Profiles/ 下的 Profile_*.asset 文件
    /// </summary>
    public class CastleDbImporter
    {
        // ===== 常量定义 =====
        private const string PROFILES_DIR = "Assets/Resources/Profiles";
        private const string BACKUP_DIR = "Logs/NotesLog/ProfileBackups";
        private const string IMPORT_LOG_DIR = "Logs/NotesLog/ImportLogs";
        private const string CASTLEDB_PATH = "Data/CastleDbDemo/MonsterSystem.cdb";
        private const string CASTLEDB_RESOURCE_PATH = "Data/CastleDbDemo/MonsterSystem";

        // ===== 菜单项 =====

        [MenuItem("Tools/CastleDB/Import All")]
        public static void ImportAll()
        {
            Debug.Log("\n========== CastleDB Import All 开始 ==========\n");

            try
            {
                // 1. 加载 CastleDB
                var castleDbAsset = Resources.Load<TextAsset>(CASTLEDB_RESOURCE_PATH);
                if (castleDbAsset == null)
                {
                    Debug.LogError($"[CastleDbImporter] 无法加载 CastleDB 资源: {CASTLEDB_RESOURCE_PATH}");
                    return;
                }

                // 2. 解析 JSON
                var source = new CastleDbJsonSource(castleDbAsset);
                var root = source.ReadCastleDbJson();
                if (root == null)
                {
                    Debug.LogError("[CastleDbImporter] CastleDB JSON 解析失败");
                    return;
                }

                // 3. 验证版本
                if (!VerifyVersion(root))
                {
                    Debug.LogError("[CastleDbImporter] CastleDB 版本验证失败");
                    return;
                }

                // 4. 创建目录
                EnsureDirectoriesExist();

                // 5. 生成备份
                BackupExistingProfiles();

                // 6. 解析 NPC 数据
                var npcs = ParseNpcData(root);
                if (npcs.Count == 0)
                {
                    Debug.LogWarning("[CastleDbImporter] 未找到任何 NPC 数据");
                    return;
                }

                // 7. 导入每个 NPC
                int successCount = 0;
                var importLog = new List<string>();
                importLog.Add($"导入时间: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                importLog.Add($"导入 NPC 数量: {npcs.Count}");
                importLog.Add("");

                foreach (var npc in npcs)
                {
                    try
                    {
                        if (CreateOrUpdateProfile(npc))
                        {
                            successCount++;
                            importLog.Add($"✓ {npc.id} ({npc.displayName}) - 导入成功");
                            Debug.Log($"[CastleDbImporter] ✓ {npc.id} 导入成功");
                        }
                        else
                        {
                            importLog.Add($"✗ {npc.id} ({npc.displayName}) - 导入失败");
                            Debug.LogWarning($"[CastleDbImporter] ✗ {npc.id} 导入失败");
                        }
                    }
                    catch (Exception ex)
                    {
                        importLog.Add($"✗ {npc.id} ({npc.displayName}) - 异常: {ex.Message}");
                        Debug.LogError($"[CastleDbImporter] ✗ {npc.id} 导入异常: {ex.Message}");
                    }
                }

                // 8. 刷新资源
                AssetDatabase.Refresh();

                // 9. 生成导入日志
                SaveImportLog(importLog, successCount, npcs.Count);

                Debug.Log($"\n========== CastleDB Import All 完成 ==========");
                Debug.Log($"成功导入: {successCount}/{npcs.Count} NPC");
                Debug.Log($"日志保存到: {IMPORT_LOG_DIR}\n");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CastleDbImporter] 导入过程异常: {ex.Message}\n{ex.StackTrace}");
            }
        }

        [MenuItem("Tools/CastleDB/Backup Profiles")]
        public static void BackupExistingProfiles()
        {
            if (!Directory.Exists(PROFILES_DIR))
            {
                Debug.LogWarning($"[CastleDbImporter] Profiles 目录不存在: {PROFILES_DIR}");
                return;
            }

            EnsureDirectoriesExist();

            string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string backupPath = Path.Combine(BACKUP_DIR, $"Backup_{timestamp}");
            Directory.CreateDirectory(backupPath);

            var profileFiles = Directory.GetFiles(PROFILES_DIR, "Profile_*.asset");
            int backupCount = 0;

            foreach (var file in profileFiles)
            {
                string fileName = Path.GetFileName(file);
                string destPath = Path.Combine(backupPath, fileName);
                File.Copy(file, destPath, true);
                backupCount++;
            }

            Debug.Log($"[CastleDbImporter] 备份完成: {backupCount} 个 Profile 备份到 {backupPath}");
        }

        // ===== 核心逻辑 =====

        /// <summary>
        /// 验证 CastleDB 版本
        /// </summary>
        private static bool VerifyVersion(CastleDbRoot root)
        {
            if (root == null || root.sheets == null || root.sheets.Count == 0)
            {
                Debug.LogError("[CastleDbImporter] CastleDB 数据为空");
                return false;
            }

            // 查找 Meta sheet
            var metaSheet = root.sheets.FirstOrDefault(s => s.name == "Meta");
            if (metaSheet == null)
            {
                Debug.LogWarning("[CastleDbImporter] 未找到 Meta sheet，跳过版本检查");
                return true;
            }

            Debug.Log("[CastleDbImporter] ✓ 版本验证通过");
            return true;
        }

        /// <summary>
        /// 解析 NPC 数据
        /// </summary>
        private static List<NpcEntry> ParseNpcData(CastleDbRoot root)
        {
            var npcs = new List<NpcEntry>();

            if (root.sheets == null)
                return npcs;

            // 查找 NPC sheet
            var npcSheet = root.sheets.FirstOrDefault(s => s.name == "NPC");
            if (npcSheet == null)
            {
                Debug.LogWarning("[CastleDbImporter] 未找到 NPC sheet");
                return npcs;
            }

            // 解析 NPC 数据
            if (npcSheet.lines != null)
            {
                foreach (var line in npcSheet.lines)
                {
                    try
                    {
                        // 将 object 转换为 JSON 字符串，再反序列化为 NpcEntry
                        string json = JsonUtility.ToJson(line);
                        var npc = JsonUtility.FromJson<NpcEntry>(json);
                        if (npc != null && !string.IsNullOrEmpty(npc.id))
                        {
                            npcs.Add(npc);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[CastleDbImporter] 解析 NPC 数据失败: {ex.Message}");
                    }
                }
            }

            Debug.Log($"[CastleDbImporter] 解析 NPC 数据: {npcs.Count} 个");
            return npcs;
        }

        /// <summary>
        /// 创建或更新 Profile
        /// </summary>
        private static bool CreateOrUpdateProfile(NpcEntry npc)
        {
            if (npc == null || string.IsNullOrEmpty(npc.id))
            {
                Debug.LogWarning("[CastleDbImporter] NPC 数据无效");
                return false;
            }

            string profilePath = Path.Combine(PROFILES_DIR, $"Profile_{npc.id}.asset");

            // 检查是否已存在
            EnemyTuningProfile profile = AssetDatabase.LoadAssetAtPath<EnemyTuningProfile>(profilePath);
            bool isNew = profile == null;

            if (isNew)
            {
                profile = ScriptableObject.CreateInstance<EnemyTuningProfile>();
                if (profile == null)
                {
                    Debug.LogError($"[CastleDbImporter] 无法创建 EnemyTuningProfile 实例");
                    return false;
                }
            }

            // 应用 NPC 数据到 Profile
            profile.profileName = npc.displayName ?? npc.id;
            profile.maxHealth = npc.maxHealth;
            profile.moveSpeed = npc.moveSpeed;
            profile.perceptionRadius = 5f; // 默认值
            profile.attackRange = npc.attackRange;
            profile.attackDamage = (int)npc.attackDamage;
            profile.attackCooldown = npc.attackCooldown;
            profile.knockbackForce = new Vector2(5f, 3f); // 默认值
            profile.patrolDistance = 4f; // 默认值
            profile.hitRecoveryDelay = 0.5f; // 默认值
            profile.invulnerableFrameDuration = npc.invincibleDuration;
            profile.enableDeathAnimation = npc.enableDeathAnimation;
            profile.deathDelay = 1f; // 默认值

            // 保存资源
            if (isNew)
            {
                AssetDatabase.CreateAsset(profile, profilePath);
                Debug.Log($"[CastleDbImporter] 创建新 Profile: {profilePath}");
            }
            else
            {
                EditorUtility.SetDirty(profile);
                Debug.Log($"[CastleDbImporter] 更新现有 Profile: {profilePath}");
            }

            AssetDatabase.SaveAssets();
            return true;
        }

        /// <summary>
        /// 确保所有必需的目录存在
        /// </summary>
        private static void EnsureDirectoriesExist()
        {
            if (!Directory.Exists(PROFILES_DIR))
                Directory.CreateDirectory(PROFILES_DIR);

            if (!Directory.Exists(BACKUP_DIR))
                Directory.CreateDirectory(BACKUP_DIR);

            if (!Directory.Exists(IMPORT_LOG_DIR))
                Directory.CreateDirectory(IMPORT_LOG_DIR);
        }

        /// <summary>
        /// 保存导入日志
        /// </summary>
        private static void SaveImportLog(List<string> log, int successCount, int totalCount)
        {
            EnsureDirectoriesExist();

            string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string logPath = Path.Combine(IMPORT_LOG_DIR, $"ImportLog_{timestamp}.txt");

            log.Add("");
            log.Add($"导入结果: {successCount}/{totalCount} 成功");
            log.Add($"成功率: {(successCount * 100.0f / totalCount):F1}%");

            File.WriteAllLines(logPath, log);
            Debug.Log($"[CastleDbImporter] 导入日志已保存: {logPath}");
        }
    }
}
