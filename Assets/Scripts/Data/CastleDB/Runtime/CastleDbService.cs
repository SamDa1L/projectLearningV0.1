using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CastleDB.Runtime
{
    /// <summary>
    /// CastleDB 服务实现
    /// 负责加载、缓存和管理 CastleDB 数据
    ///
    /// 设计说明：
    /// - 单例模式，通过 Initialize() 显式初始化
    /// - 支持版本检查，防止 schema 不匹配
    /// - 提供事件系统，通知数据变更
    /// - 所有操作都会写入日志
    /// </summary>
    public class CastleDbService : ICastleDbService
    {
        private static CastleDbService _instance;
        public static CastleDbService Instance => _instance;

        private ICastleDbRepository _repository;
        private CastleDbVersionInfo _versionInfo;
        private bool _initialized = false;

        public event Action OnDataChanged;
        public event Action<string> OnVersionMismatch;

        private const string LOG_FILE = "Logs/CastleDbLoad.log";
        private const string IMPORT_LOG_FILE = "Logs/CastleDbImport.log";

        /// <summary>
        /// 初始化 CastleDbService
        /// </summary>
        public void Initialize(ICastleDbSource source)
        {
            if (_initialized)
            {
                LogWarning("CastleDbService 已初始化，跳过重复初始化");
                return;
            }

            try
            {
                _repository = new CastleDbRepository();
                _repository.Load(source);

                _versionInfo = _repository.GetVersionInfo();

                // 检查版本
                if (!_repository.IsVersionMatched("0.2"))
                {
                    string errorMsg = $"Schema 版本不匹配！期望 0.2，实际 {_versionInfo.schemaVersion}";
                    LogError(errorMsg);
                    OnVersionMismatch?.Invoke(errorMsg);
                    return;
                }

                _initialized = true;
                LogInfo($"CastleDbService 初始化成功 - {source.GetSourceDescription()}");
                LogInfo($"版本信息：{_versionInfo}");
                LogInfo($"NPC 数量：{GetAllNpcs().Count}");
                LogInfo($"技能数量：{GetAllAbilities().Count}");
                LogInfo($"检测区数量：{GetAllDetectionZones().Count}");
            }
            catch (Exception ex)
            {
                LogError($"初始化失败：{ex.Message}\n{ex.StackTrace}");
                _initialized = false;
            }
        }

        /// <summary>
        /// 刷新数据
        /// </summary>
        public void Refresh()
        {
            if (!_initialized)
            {
                LogWarning("CastleDbService 未初始化，无法刷新");
                return;
            }

            try
            {
                LogInfo("开始刷新 CastleDB 数据...");
                OnDataChanged?.Invoke();
                LogInfo("数据刷新完成");
            }
            catch (Exception ex)
            {
                LogError($"刷新失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 获取所有 NPC
        /// </summary>
        public List<NpcEntry> GetAllNpcs()
        {
            if (!_initialized)
            {
                LogWarning("CastleDbService 未初始化");
                return new List<NpcEntry>();
            }

            return _repository.GetAllNpcs();
        }

        /// <summary>
        /// 按 ID 获取 NPC
        /// </summary>
        public NpcEntry GetNpcById(string id)
        {
            if (!_initialized)
            {
                LogWarning("CastleDbService 未初始化");
                return null;
            }

            var npc = _repository.GetNpcById(id);
            if (npc == null)
            {
                LogWarning($"未找到 NPC：{id}");
            }

            return npc;
        }

        /// <summary>
        /// 获取所有技能
        /// </summary>
        public List<AbilityEntry> GetAllAbilities()
        {
            if (!_initialized)
            {
                LogWarning("CastleDbService 未初始化");
                return new List<AbilityEntry>();
            }

            return _repository.GetAllAbilities();
        }

        /// <summary>
        /// 按 ID 获取技能
        /// </summary>
        public AbilityEntry GetAbilityById(string id)
        {
            if (!_initialized)
            {
                LogWarning("CastleDbService 未初始化");
                return null;
            }

            var ability = _repository.GetAbilityById(id);
            if (ability == null)
            {
                LogWarning($"未找到技能：{id}");
            }

            return ability;
        }

        /// <summary>
        /// 获取所有玩家
        /// </summary>
        public List<PlayerEntry> GetAllPlayers()
        {
            if (!_initialized)
            {
                LogWarning("CastleDbService 未初始化");
                return new List<PlayerEntry>();
            }

            return _repository.GetAllPlayers();
        }

        /// <summary>
        /// 获取所有玩家攻击覆盖
        /// 阶段 3A: 攻击伤害链路
        /// </summary>
        public List<PlayerAttackOverrideEntry> GetAllPlayerAttackOverrides()
        {
            if (!_initialized)
            {
                LogWarning("CastleDbService 未初始化");
                return new List<PlayerAttackOverrideEntry>();
            }

            return _repository.GetAllPlayerAttackOverrides();
        }

        /// <summary>
        /// 获取所有检测区
        /// </summary>
        public List<DetectionZoneEntry> GetAllDetectionZones()
        {
            if (!_initialized)
            {
                LogWarning("CastleDbService 未初始化");
                return new List<DetectionZoneEntry>();
            }

            return _repository.GetAllDetectionZones();
        }

        /// <summary>
        /// 按 NPC ID 获取检测区
        /// </summary>
        public List<DetectionZoneEntry> GetDetectionZonesByNpcId(string npcId)
        {
            if (!_initialized)
            {
                LogWarning("CastleDbService 未初始化");
                return new List<DetectionZoneEntry>();
            }

            return _repository.GetDetectionZonesByNpcId(npcId);
        }

        /// <summary>
        /// 获取版本信息
        /// </summary>
        public CastleDbVersionInfo GetVersionInfo()
        {
            return _versionInfo;
        }

        // ===== 日志方法 =====

        private void LogInfo(string message)
        {
            Debug.Log($"[CastleDbService] {message}");
            AppendToLogFile(LOG_FILE, $"[INFO] {DateTime.Now:HH:mm:ss} - {message}");
        }

        private void LogWarning(string message)
        {
            Debug.LogWarning($"[CastleDbService] {message}");
            AppendToLogFile(LOG_FILE, $"[WARN] {DateTime.Now:HH:mm:ss} - {message}");
        }

        private void LogError(string message)
        {
            Debug.LogError($"[CastleDbService] {message}");
            AppendToLogFile(LOG_FILE, $"[ERROR] {DateTime.Now:HH:mm:ss} - {message}");
        }

        private void AppendToLogFile(string filePath, string message)
        {
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(filePath));
                System.IO.File.AppendAllText(filePath, message + "\n");
            }
            catch (Exception ex)
            {
                Debug.LogError($"写入日志文件失败：{ex.Message}");
            }
        }
    }

    /// <summary>
    /// CastleDB 仓储实现
    /// 负责缓存和查询数据
    /// </summary>
    public class CastleDbRepository : ICastleDbRepository
    {
        private List<NpcEntry> _npcs = new List<NpcEntry>();
        private List<AbilityEntry> _abilities = new List<AbilityEntry>();
        private List<PlayerEntry> _players = new List<PlayerEntry>();
        private List<PlayerAttackOverrideEntry> _playerAttackOverrides = new List<PlayerAttackOverrideEntry>();
        private List<DetectionZoneEntry> _detectionZones = new List<DetectionZoneEntry>();
        private CastleDbVersionInfo _versionInfo;

        /// <summary>
        /// 加载数据
        /// </summary>
        public void Load(ICastleDbSource source)
        {
            var root = source.ReadCastleDbJson();
            if (root == null)
            {
                throw new Exception("无法读取 CastleDB 数据");
            }

            // 解析各个 Sheet
            foreach (var sheet in root.sheets)
            {
                switch (sheet.name)
                {
                    case "NPC":
                        _npcs = ConvertLinesToNpcEntries(sheet.lines);
                        break;
                    case "Ability":
                        _abilities = ConvertLinesToAbilityEntries(sheet.lines);
                        break;
                    case "Player":
                        _players = ConvertLinesToPlayerEntries(sheet.lines);
                        break;
                    case "PlayerAttackOverride":
                        _playerAttackOverrides = ConvertLinesToPlayerAttackOverrideEntries(sheet.lines);
                        break;
                    case "DetectionZone":
                        _detectionZones = ConvertLinesToDetectionZoneEntries(sheet.lines);
                        break;
                    case "Meta":
                        ParseMetaSheet(ConvertLinesToMetaEntries(sheet.lines));
                        break;
                }
            }
        }

        /// <summary>
        /// 将通用 lines 转换为 NpcEntry 列表
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

        /// <summary>
        /// 将通用 lines 转换为 AbilityEntry 列表
        /// </summary>
        private List<AbilityEntry> ConvertLinesToAbilityEntries(List<object> lines)
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
                        attackType = GetIntValue(dict, "attackType")
                    });
                }
            }
            return result;
        }

        /// <summary>
        /// 将通用 lines 转换为 PlayerEntry 列表
        /// 阶段 3A: 支持完整的玩家属性字段
        /// </summary>
        private List<PlayerEntry> ConvertLinesToPlayerEntries(List<object> lines)
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
                        jumpImpulse = GetFloatValue(dict, "jumpImpulse"),  // CastleDB 字段名
                        climbSpeed = GetFloatValue(dict, "climbSpeed"),
                        baseAttackDamage = GetFloatValue(dict, "baseAttackDamage")
                    });
                }
            }
            return result;
        }

        /// <summary>
        /// 将通用 lines 转换为 PlayerAttackOverrideEntry 列表
        /// 阶段 3A: 攻击伤害覆盖配置
        /// </summary>
        private List<PlayerAttackOverrideEntry> ConvertLinesToPlayerAttackOverrideEntries(List<object> lines)
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
        /// 将通用 lines 转换为 DetectionZoneEntry 列表
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

        /// <summary>
        /// 将通用 lines 转换为 MetaEntry 列表
        /// </summary>
        private List<MetaEntry> ConvertLinesToMetaEntries(List<object> lines)
        {
            var result = new List<MetaEntry>();
            if (lines == null) return result;

            foreach (var line in lines)
            {
                if (line is Dictionary<string, object> dict)
                {
                    result.Add(new MetaEntry
                    {
                        key = GetStringValue(dict, "key"),
                        value = GetStringValue(dict, "value")
                    });
                }
            }
            return result;
        }

        /// <summary>
        /// 从字典中获取字符串值
        /// </summary>
        private string GetStringValue(Dictionary<string, object> dict, string key)
        {
            if (dict.TryGetValue(key, out var value))
            {
                return value?.ToString() ?? "";
            }
            return "";
        }

        /// <summary>
        /// 从字典中获取浮点数值
        /// </summary>
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

        /// <summary>
        /// 从字典中获取浮点数值（带默认值）
        /// 用于新字段的向后兼容：如果 CastleDB 中没有该字段，返回指定的默认值
        /// </summary>
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

        /// <summary>
        /// 从字典中获取整数值
        /// </summary>
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

        /// <summary>
        /// 从字典中获取布尔值
        /// </summary>
        private bool GetBoolValue(Dictionary<string, object> dict, string key)
        {
            if (dict.TryGetValue(key, out var value))
            {
                if (value is bool b) return b;
                if (bool.TryParse(value?.ToString(), out var result)) return result;
            }
            return false;
        }

        /// <summary>
        /// 解析 Meta Sheet 获取版本信息
        /// </summary>
        private void ParseMetaSheet(List<MetaEntry> metaLines)
        {
            string schemaVersion = "unknown";
            string featureFlags = "";

            if (metaLines != null)
            {
                foreach (var meta in metaLines)
                {
                    if (meta.key == "schemaVersion")
                        schemaVersion = meta.value;
                    else if (meta.key == "featureFlags")
                        featureFlags = meta.value;
                }
            }

            _versionInfo = new CastleDbVersionInfo(schemaVersion, featureFlags);
        }

        public List<NpcEntry> GetAllNpcs() => new List<NpcEntry>(_npcs);

        public NpcEntry GetNpcById(string id) => _npcs.FirstOrDefault(n => n.id == id);

        public List<AbilityEntry> GetAllAbilities() => new List<AbilityEntry>(_abilities);

        public AbilityEntry GetAbilityById(string id) => _abilities.FirstOrDefault(a => a.id == id);

        public List<PlayerEntry> GetAllPlayers() => new List<PlayerEntry>(_players);

        public List<PlayerAttackOverrideEntry> GetAllPlayerAttackOverrides() => new List<PlayerAttackOverrideEntry>(_playerAttackOverrides);

        public List<DetectionZoneEntry> GetAllDetectionZones() => new List<DetectionZoneEntry>(_detectionZones);

        public List<DetectionZoneEntry> GetDetectionZonesByNpcId(string npcId)
        {
            return _detectionZones.Where(dz => dz.npcId == npcId).ToList();
        }

        public CastleDbVersionInfo GetVersionInfo() => _versionInfo;

        public bool IsVersionMatched(string expectedVersion)
        {
            return _versionInfo != null && _versionInfo.schemaVersion == expectedVersion;
        }
    }
}
