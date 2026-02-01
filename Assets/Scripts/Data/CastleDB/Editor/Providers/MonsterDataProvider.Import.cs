using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using CastleDB.Runtime;

namespace CastleDB.Editor.Providers
{
    public partial class MonsterDataProvider : CdbDataProviderBase
    {
        #region 导入（Editor Only）

        /// <summary>
        /// 导入 NPC 数据生成 EnemyTuningProfile 资产
        /// 迁移自 CastleDbImporter.cs:492-547
        /// </summary>
        protected override CdbImportResult OnImport(CdbModuleDescriptor descriptor)
        {
            var builder = new CdbImportResultBuilder(ExpectedProviderId);

            const string PROFILE_OUTPUT_DIR = "Assets/Resources/Profiles";

            // 确保输出目录存在
            if (!System.IO.Directory.Exists(PROFILE_OUTPUT_DIR))
            {
                System.IO.Directory.CreateDirectory(PROFILE_OUTPUT_DIR);
                builder.AddInfo($"创建目录：{PROFILE_OUTPUT_DIR}");
            }

            foreach (var npc in _npcs)
            {
                try
                {
                    // 生成 Profile 文件路径（使用 npc.id 作为稳定主键）
                    string profilePath = $"{PROFILE_OUTPUT_DIR}/Profile_{npc.id}.asset";

                    // 查找或创建 Profile
                    EnemyTuningProfile profile = AssetDatabase.LoadAssetAtPath<EnemyTuningProfile>(profilePath);

                    // 记录旧的 animationTrigger（用于变更日志）
                    string oldTrigger = profile != null ? profile.animationTrigger : null;
                    string oldCastTrigger = profile != null ? profile.castTrigger : null;

                    bool isNew = profile == null;
                    if (isNew)
                    {
                        // 创建新 Profile
                        profile = ScriptableObject.CreateInstance<EnemyTuningProfile>();
                        profile.profileName = npc.displayName;
                        AssetDatabase.CreateAsset(profile, profilePath);
                        builder.Created(profilePath);
                    }
                    else
                    {
                        builder.Updated(profilePath);
                    }

                    // 应用 CastleDB 数据
                    var npcAbilities = _npcAbilities
                        .Where(binding => binding != null && binding.npcId == npc.id)
                        .OrderByDescending(binding => binding.priority)
                        .ThenBy(binding => binding.id)
                        .ToList();

                    var npcAbilityIdSet = new HashSet<string>(npcAbilities.Where(b => b != null).Select(b => b.id));

                    var passiveBindings = _npcPassiveAbilityBindings
                        .Where(b => b != null && npcAbilityIdSet.Contains(b.bindingId))
                        .OrderBy(b => b.bindingId)
                        .ToList();

                    var passiveConditions = _npcPassiveAbilityConditions
                        .Where(c => c != null && npcAbilityIdSet.Contains(c.bindingId))
                        .OrderBy(c => c.bindingId)
                        .ThenBy(c => c.order)
                        .ToList();

                    profile.ApplyFromCastleDb(npc, npcAbilities, passiveBindings, passiveConditions);

                    // animationTrigger 变更记录
                    if (!string.IsNullOrEmpty(oldTrigger) && oldTrigger != npc.animationTrigger)
                    {
                        builder.AddWarning($"AnimationTrigger 变更 - NPC '{npc.id}': '{oldTrigger}' → '{npc.animationTrigger}'");
                    }

                    // castTrigger 变更记录
                    if (!string.IsNullOrEmpty(oldCastTrigger) && oldCastTrigger != npc.castTrigger)
                    {
                        builder.AddWarning($"CastTrigger 变更 - NPC '{npc.id}': '{oldCastTrigger}' → '{npc.castTrigger}'");
                    }

                    // 标记为 dirty（由 ImportCoordinator 统一保存）
                    EditorUtility.SetDirty(profile);
                }
                catch (Exception ex)
                {
                    builder.AddError($"创建/更新 Profile 失败 ({npc.id}): {ex.Message}");
    }
}

            // 记录 DetectionZone 信息（不生成资产，仅记录日志）
            if (_detectionZones.Count > 0)
            {
                builder.AddInfo($"DetectionZone 数据：{_detectionZones.Count} 条（将在 Sync NPC Prefabs 时应用）");
            }

            try
            {
                string catalogDir = Path.GetDirectoryName(ENEMY_ABILITY_CATALOG_PATH);
                if (!string.IsNullOrEmpty(catalogDir) && !Directory.Exists(catalogDir))
                {
                    Directory.CreateDirectory(catalogDir);
                    builder.AddInfo($"Created dir: {catalogDir}");
                }

                AbilityCatalog catalog = AssetDatabase.LoadAssetAtPath<AbilityCatalog>(ENEMY_ABILITY_CATALOG_PATH);
                bool isNew = catalog == null;
                if (isNew)
                {
                    catalog = ScriptableObject.CreateInstance<AbilityCatalog>();
                    AssetDatabase.CreateAsset(catalog, ENEMY_ABILITY_CATALOG_PATH);
                    builder.Created(ENEMY_ABILITY_CATALOG_PATH);
                }
                else
                {
                    builder.Updated(ENEMY_ABILITY_CATALOG_PATH);
                }

                catalog.ApplyFromCastleDb(_enemyAbilities, _enemyProjectiles, null, _enemyOnHitSequences, _npcPassiveAbilities);
                builder.AddInfo($"EnemyAbilityCatalog: {_enemyAbilities.Count} abilities");
                builder.AddInfo($"EnemyAbilityProjectiles: {_enemyProjectiles.Count} entries");
                builder.AddInfo($"EnemyAbilityOnHitSequences: {_enemyOnHitSequences.Count} sequences");
                builder.AddInfo($"NpcPassiveAbilities: {_npcPassiveAbilities.Count} entries");

                EditorUtility.SetDirty(catalog);
            }
            catch (Exception ex)
            {
                builder.AddError($"Create/Update EnemyAbilityCatalog failed: {ex.Message}");
            }

            return builder.Build();
        }

        #endregion
}
}
