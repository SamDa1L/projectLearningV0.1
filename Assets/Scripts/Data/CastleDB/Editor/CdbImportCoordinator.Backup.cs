using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using CastleDB.Runtime;

namespace CastleDB.Editor
{
    public partial class CdbImportCoordinator
    {
        #region 备份管理

        private const string BACKUP_DIR = "Logs/CastleDBImport/Backups";
        private const string PROFILE_OUTPUT_DIR = "Assets/Resources/Profiles";
        private const string PLAYER_CONFIG_PATH = "Assets/Resources/Config/PlayerConfig.asset";
        private const string ABILITY_CATALOG_PATH = "Assets/Resources/Config/AbilityCatalog.asset";
        private const string ENEMY_ABILITY_CATALOG_PATH = "Assets/Resources/Config/EnemyAbilityCatalog.asset";

        /// <summary>
        /// 备份现有资产（Profile/PlayerConfig/AbilityCatalog）
        /// </summary>
        /// <returns>备份时间戳（用于后续回滚），如果备份失败返回 null</returns>
        private string BackupExistingAssets()
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string backupPath = Path.Combine(BACKUP_DIR, $"Backup_{timestamp}");

            int backupCount = 0;

            // 1. 备份 EnemyTuningProfile 文件
            if (Directory.Exists(PROFILE_OUTPUT_DIR))
            {
                var profileFiles = Directory.GetFiles(PROFILE_OUTPUT_DIR, "Profile_*.asset");
                if (profileFiles.Length > 0)
                {
                    Directory.CreateDirectory(backupPath);
                    foreach (var file in profileFiles)
                    {
                        string fileName = Path.GetFileName(file);
                        string destPath = Path.Combine(backupPath, fileName);
                        File.Copy(file, destPath, true);
                        backupCount++;
    }
}
            }

            // 2. 备份 PlayerConfig 文件
            if (File.Exists(PLAYER_CONFIG_PATH))
            {
                if (!Directory.Exists(backupPath))
                {
                    Directory.CreateDirectory(backupPath);
                }

                string fileName = Path.GetFileName(PLAYER_CONFIG_PATH);
                string destPath = Path.Combine(backupPath, fileName);
                File.Copy(PLAYER_CONFIG_PATH, destPath, true);
                backupCount++;
            }

            // 3. 备份 AbilityCatalog 文件
            if (File.Exists(ABILITY_CATALOG_PATH))
            {
                if (!Directory.Exists(backupPath))
                {
                    Directory.CreateDirectory(backupPath);
                }

                string fileName = Path.GetFileName(ABILITY_CATALOG_PATH);
                string destPath = Path.Combine(backupPath, fileName);
                File.Copy(ABILITY_CATALOG_PATH, destPath, true);
                backupCount++;
            }

            if (File.Exists(ENEMY_ABILITY_CATALOG_PATH))
            {
                if (!Directory.Exists(backupPath))
                {
                    Directory.CreateDirectory(backupPath);
                }

                string fileName = Path.GetFileName(ENEMY_ABILITY_CATALOG_PATH);
                string destPath = Path.Combine(backupPath, fileName);
                File.Copy(ENEMY_ABILITY_CATALOG_PATH, destPath, true);
                backupCount++;
            }

            if (backupCount > 0)
            {
                Debug.Log($"[CdbImportCoordinator] 备份完成: {backupPath} ({backupCount} 个文件)");
                return timestamp;
            }

            return null;
        }

        /// <summary>
        /// 从最新备份恢复资产（导入失败时调用）
        /// </summary>
        /// <param name="backupTimestamp">备份时间戳（可选，不提供则使用最新备份）</param>
        private void RestoreFromBackup(string backupTimestamp = null)
        {
            string backupPath;

            if (!string.IsNullOrEmpty(backupTimestamp))
            {
                backupPath = Path.Combine(BACKUP_DIR, $"Backup_{backupTimestamp}");
            }
            else
            {
                // 查找最新备份
                if (!Directory.Exists(BACKUP_DIR))
                {
                    Debug.LogWarning("[CdbImportCoordinator] 备份目录不存在，无法回滚");
                    return;
                }

                var backupDirs = Directory.GetDirectories(BACKUP_DIR, "Backup_*")
                    .OrderByDescending(d => d)
                    .ToList();

                if (backupDirs.Count == 0)
                {
                    Debug.LogWarning("[CdbImportCoordinator] 未找到备份，无法回滚");
                    return;
                }

                backupPath = backupDirs[0];
            }

            if (!Directory.Exists(backupPath))
            {
                Debug.LogWarning($"[CdbImportCoordinator] 备份路径不存在：{backupPath}");
                return;
            }

            Debug.Log($"[CdbImportCoordinator] 开始从备份恢复：{backupPath}");

            int restoredCount = 0;

            // 1. 恢复 EnemyTuningProfile 文件
            if (Directory.Exists(PROFILE_OUTPUT_DIR))
            {
                var currentProfileFiles = Directory.GetFiles(PROFILE_OUTPUT_DIR, "Profile_*.asset");
                foreach (var file in currentProfileFiles)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[CdbImportCoordinator] 删除当前文件失败：{file} - {ex.Message}");
                    }
                }
            }

            var backupProfileFiles = Directory.GetFiles(backupPath, "Profile_*.asset");
            foreach (var backupFile in backupProfileFiles)
            {
                try
                {
                    string fileName = Path.GetFileName(backupFile);
                    string destPath = Path.Combine(PROFILE_OUTPUT_DIR, fileName);
                    File.Copy(backupFile, destPath, true);
                    restoredCount++;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[CdbImportCoordinator] 恢复文件失败：{backupFile} - {ex.Message}");
                }
            }

            // 2. 恢复 PlayerConfig 文件
            string backupPlayerConfig = Path.Combine(backupPath, "PlayerConfig.asset");
            if (File.Exists(backupPlayerConfig))
            {
                try
                {
                    if (File.Exists(PLAYER_CONFIG_PATH))
                    {
                        File.Delete(PLAYER_CONFIG_PATH);
                    }
                    File.Copy(backupPlayerConfig, PLAYER_CONFIG_PATH, true);
                    restoredCount++;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[CdbImportCoordinator] 恢复 PlayerConfig 失败：{ex.Message}");
                }
            }

            // 3. 恢复 AbilityCatalog 文件
            string backupAbilityCatalog = Path.Combine(backupPath, "AbilityCatalog.asset");
            if (File.Exists(backupAbilityCatalog))
            {
                try
                {
                    if (File.Exists(ABILITY_CATALOG_PATH))
                    {
                        File.Delete(ABILITY_CATALOG_PATH);
                    }
                    File.Copy(backupAbilityCatalog, ABILITY_CATALOG_PATH, true);
                    restoredCount++;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[CdbImportCoordinator] 恢复 AbilityCatalog 失败：{ex.Message}");
                }
            }

            // 4. 刷新 AssetDatabase
            string backupEnemyAbilityCatalog = Path.Combine(backupPath, "EnemyAbilityCatalog.asset");
            if (File.Exists(backupEnemyAbilityCatalog))
            {
                try
                {
                    if (File.Exists(ENEMY_ABILITY_CATALOG_PATH))
                    {
                        File.Delete(ENEMY_ABILITY_CATALOG_PATH);
                    }
                    File.Copy(backupEnemyAbilityCatalog, ENEMY_ABILITY_CATALOG_PATH, true);
                    restoredCount++;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[CdbImportCoordinator] Restore EnemyAbilityCatalog failed: {ex.Message}");
                }
            }

            AssetDatabase.Refresh();

            Debug.Log($"[CdbImportCoordinator] 回滚完成：恢复了 {restoredCount} 个文件");
        }

        #endregion
}
}
