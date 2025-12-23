using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using CastleDB.Runtime;

namespace CastleDB.Editor.Providers
{
    /// <summary>
    /// Monster 数据提供者（0.3 版本 Phase 2）
    /// 处理 MonsterSystem.cdb 中的 NPC 和 DetectionZone Sheet
    ///
    /// 职责：
    /// - 解析 NPC Sheet 数据
    /// - 解析 DetectionZone Sheet 数据
    /// - 校验 NPC 和 DetectionZone 数据完整性
    /// - 导入时生成 EnemyTuningProfile 资产
    ///
    /// 迁移来源：
    /// - NPC 解析：CastleDbService.cs:318-356
    /// - Zone 解析：CastleDbService.cs:445-464
    /// - 校验逻辑：CastleDbImporter.cs:168-204
    /// - Profile 生成：CastleDbImporter.cs:492-547
    /// </summary>
    public class MonsterDataProvider : CdbDataProviderBase
    {
        /// <summary>
        /// Provider ID，对应 Meta Sheet 中的 providerId
        /// </summary>
        public override string ExpectedProviderId => "Monster";

        // ===== 缓存数据 =====
        private List<NpcEntry> _npcs = new List<NpcEntry>();
        private List<DetectionZoneEntry> _detectionZones = new List<DetectionZoneEntry>();

        #region 初始化

        /// <summary>
        /// 解析 .cdb 数据并缓存
        /// 迁移自 CastleDbRepository.Load()
        /// </summary>
        protected override void OnInitialize(CastleDbRoot root, CdbModuleDescriptor descriptor)
        {
            _npcs.Clear();
            _detectionZones.Clear();

            foreach (var sheet in root.sheets)
            {
                switch (sheet.name)
                {
                    case "NPC":
                        _npcs = ConvertLinesToNpcEntries(sheet.lines);
                        Debug.Log($"[MonsterDataProvider] 解析 NPC Sheet：{_npcs.Count} 条");
                        break;

                    case "DetectionZone":
                        _detectionZones = ConvertLinesToDetectionZoneEntries(sheet.lines);
                        Debug.Log($"[MonsterDataProvider] 解析 DetectionZone Sheet：{_detectionZones.Count} 条");
                        break;

                    case "Meta":
                        // Meta Sheet 由 Registry 处理，此处跳过
                        break;

                    default:
                        // 0.3 版本 Monster.cdb 只处理 NPC/DetectionZone/Meta
                        // 其他 Sheet（Player/Ability 等）由对应 Provider 处理
                        Debug.LogWarning($"[MonsterDataProvider] 忽略未知 Sheet：{sheet.name}");
                        break;
                }
            }
        }

        #endregion

        #region NPC 解析（迁移自 CastleDbRepository）

        /// <summary>
        /// 将通用 lines 转换为 NpcEntry 列表
        /// 迁移自 CastleDbService.cs:318-331
        /// </summary>
        private List<NpcEntry> ConvertLinesToNpcEntries(List<object> lines)
        {
            var result = new List<NpcEntry>();
            if (lines == null) return result;

            foreach (var line in lines)
            {
                if (line is Dictionary<string, object> dict)
                {
                    result.Add(ConvertDictToNpcEntry(dict));
                }
            }
            return result;
        }

        /// <summary>
        /// 将字典转换为 NpcEntry
        /// 迁移自 CastleDbService.cs:336-357
        /// </summary>
        private NpcEntry ConvertDictToNpcEntry(Dictionary<string, object> dict)
        {
            return new NpcEntry
            {
                id = GetStringValue(dict, "id"),
                displayName = GetStringValue(dict, "displayName"),
                prefabName = GetStringValue(dict, "prefabName"),
                animationTrigger = GetStringValue(dict, "animationTrigger"),
                maxHealth = GetFloatValue(dict, "maxHealth"),
                attackDamage = GetFloatValue(dict, "attackDamage"),
                moveSpeed = GetFloatValue(dict, "moveSpeed"),
                attackRange = GetFloatValue(dict, "attackRange"),
                attackCooldown = GetFloatValue(dict, "attackCooldown"),
                invincibleDuration = GetFloatValue(dict, "invincibleDuration"),
                knockbackMultiplier = GetFloatValue(dict, "knockbackMultiplier"),
                enableDeathAnimation = GetBoolValue(dict, "enableDeathAnimation"),
                useLegacyLogicFallback = GetBoolValue(dict, "useLegacyLogicFallback"),
                perceptionRadius = GetFloatValue(dict, "perceptionRadius"),
                // 怪物命中玩家时的击退缩放系数，默认值为 1（保持 Prefab 原始击退）
                knockbackToPlayer = GetFloatValueWithDefault(dict, "knockbackToPlayer", 1f)
            };
        }

        #endregion

        #region DetectionZone 解析（迁移自 CastleDbRepository）

        /// <summary>
        /// 将通用 lines 转换为 DetectionZoneEntry 列表
        /// 迁移自 CastleDbService.cs:445-464
        /// </summary>
        private List<DetectionZoneEntry> ConvertLinesToDetectionZoneEntries(List<object> lines)
        {
            var result = new List<DetectionZoneEntry>();
            if (lines == null) return result;

            foreach (var line in lines)
            {
                if (line is Dictionary<string, object> dict)
                {
                    result.Add(new DetectionZoneEntry
                    {
                        id = GetStringValue(dict, "id"),
                        npcId = GetStringValue(dict, "npcId"),
                        role = GetIntValue(dict, "role"),
                        childId = GetStringValue(dict, "childId")
                    });
                }
            }
            return result;
        }

        #endregion

        #region 数据校验（迁移自 CastleDbImporter）

        /// <summary>
        /// 校验 NPC 和 DetectionZone 数据完整性
        /// 迁移自 CastleDbImporter.cs:168-204
        /// </summary>
        protected override List<string> OnValidate(CdbModuleDescriptor descriptor)
        {
            var errors = new List<string>();

            // ===== NPC 校验 =====
            foreach (var npc in _npcs)
            {
                // 校验 id
                if (string.IsNullOrWhiteSpace(npc.id))
                {
                    errors.Add($"NPC 的 id 为空");
                    continue;
                }

                // 校验 maxHealth
                if (npc.maxHealth <= 0)
                {
                    errors.Add($"NPC '{npc.id}' 的 maxHealth <= 0 ({npc.maxHealth})");
                }

                // 校验 moveSpeed
                if (npc.moveSpeed <= 0)
                {
                    errors.Add($"NPC '{npc.id}' 的 moveSpeed <= 0 ({npc.moveSpeed})");
                }

                // 校验 animationTrigger
                if (string.IsNullOrWhiteSpace(npc.animationTrigger))
                {
                    errors.Add($"NPC '{npc.id}' 的 animationTrigger 为空");
                }
                else if (!System.Text.RegularExpressions.Regex.IsMatch(npc.animationTrigger, @"^[a-zA-Z0-9_]+$"))
                {
                    errors.Add($"NPC '{npc.id}' 的 animationTrigger '{npc.animationTrigger}' 包含非法字符");
                }

                // 校验 attackCooldown
                if (npc.attackCooldown < 0)
                {
                    errors.Add($"NPC '{npc.id}' 的 attackCooldown < 0 ({npc.attackCooldown})");
                }

                // 校验 attackDamage
                if (npc.attackDamage < 0)
                {
                    errors.Add($"NPC '{npc.id}' 的 attackDamage < 0 ({npc.attackDamage})");
                }
            }

            // ===== NPC ID 唯一性校验 =====
            var npcIdSet = new HashSet<string>();
            foreach (var npc in _npcs)
            {
                if (!string.IsNullOrEmpty(npc.id))
                {
                    if (npcIdSet.Contains(npc.id))
                    {
                        errors.Add($"NPC id '{npc.id}' 重复");
                    }
                    npcIdSet.Add(npc.id);
                }
            }

            // ===== DetectionZone 校验 =====
            foreach (var zone in _detectionZones)
            {
                // 校验 npcId 是否存在
                if (string.IsNullOrWhiteSpace(zone.npcId))
                {
                    errors.Add($"DetectionZone '{zone.id}' 的 npcId 为空");
                }
                else if (!npcIdSet.Contains(zone.npcId))
                {
                    errors.Add($"DetectionZone '{zone.id}' 的 npcId '{zone.npcId}' 在 NPC Sheet 中不存在");
                }

                // 校验 childId
                if (string.IsNullOrWhiteSpace(zone.childId))
                {
                    errors.Add($"DetectionZone '{zone.id}' 的 childId 为空");
                }

                // 校验 role 范围
                if (zone.role < 0 || zone.role > 5)
                {
                    errors.Add($"DetectionZone '{zone.id}' 的 role {zone.role} 超出范围 (0-5)");
                }
            }

            return errors;
        }

        #endregion

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
                    profile.ApplyFromCastleDb(npc);

                    // animationTrigger 变更记录
                    if (!string.IsNullOrEmpty(oldTrigger) && oldTrigger != npc.animationTrigger)
                    {
                        builder.AddWarning($"AnimationTrigger 变更 - NPC '{npc.id}': '{oldTrigger}' → '{npc.animationTrigger}'");
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

            return builder.Build();
        }

        #endregion

        #region 数据查询

        /// <summary>
        /// 获取指定类型的所有条目
        /// </summary>
        public override List<T> GetAllEntries<T>()
        {
            if (typeof(T) == typeof(NpcEntry))
            {
                return _npcs.Cast<T>().ToList();
            }

            if (typeof(T) == typeof(DetectionZoneEntry))
            {
                return _detectionZones.Cast<T>().ToList();
            }

            return new List<T>();
        }

        /// <summary>
        /// 按 ID 获取指定类型的条目
        /// </summary>
        public override T GetEntryById<T>(string id)
        {
            if (typeof(T) == typeof(NpcEntry))
            {
                return _npcs.FirstOrDefault(n => n.id == id) as T;
            }

            if (typeof(T) == typeof(DetectionZoneEntry))
            {
                return _detectionZones.FirstOrDefault(z => z.id == id) as T;
            }

            return null;
        }

        /// <summary>
        /// 按 NPC ID 获取检测区列表
        /// </summary>
        public List<DetectionZoneEntry> GetDetectionZonesByNpcId(string npcId)
        {
            return _detectionZones.Where(z => z.npcId == npcId).ToList();
        }

        /// <summary>
        /// 获取所有 NPC
        /// </summary>
        public List<NpcEntry> GetAllNpcs()
        {
            return new List<NpcEntry>(_npcs);
        }

        /// <summary>
        /// 获取所有检测区
        /// </summary>
        public List<DetectionZoneEntry> GetAllDetectionZones()
        {
            return new List<DetectionZoneEntry>(_detectionZones);
        }

        #endregion

        #region 重置

        /// <summary>
        /// 清除缓存数据
        /// </summary>
        protected override void OnReset()
        {
            _npcs.Clear();
            _detectionZones.Clear();
        }

        #endregion

        #region 辅助方法（迁移自 CastleDbRepository）

        private string GetStringValue(Dictionary<string, object> dict, string key)
        {
            if (dict.TryGetValue(key, out var value))
            {
                return value?.ToString() ?? "";
            }
            return "";
        }

        private float GetFloatValue(Dictionary<string, object> dict, string key)
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

        private float GetFloatValueWithDefault(Dictionary<string, object> dict, string key, float defaultValue)
        {
            if (dict.TryGetValue(key, out var value))
            {
                if (value is float f) return f;
                if (value is double d) return (float)d;
                if (value is int i) return i;
                if (float.TryParse(value?.ToString(), out var result)) return result;
            }
            return defaultValue;
        }

        private int GetIntValue(Dictionary<string, object> dict, string key)
        {
            if (dict.TryGetValue(key, out var value))
            {
                if (value is int i) return i;
                if (value is long l) return (int)l;
                if (int.TryParse(value?.ToString(), out var result)) return result;
            }
            return 0;
        }

        private bool GetBoolValue(Dictionary<string, object> dict, string key)
        {
            if (dict.TryGetValue(key, out var value))
            {
                if (value is bool b) return b;
                if (bool.TryParse(value?.ToString(), out var result)) return result;
            }
            return false;
        }

        #endregion
    }
}
