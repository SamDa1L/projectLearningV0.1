using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using CastleDB.Runtime;

namespace CastleDB.Editor.Providers
{
    /// <summary>
    /// Player 数据提供者（0.3 版本 Phase 4）
    /// 处理 Player.cdb 中的 Player 和 PlayerAttackOverride Sheet
    ///
    /// 职责：
    /// - 解析 Player Sheet 数据
    /// - 解析 PlayerAttackOverride Sheet 数据
    /// - 校验 Player 和 Override 数据完整性
    /// - 导入时生成 PlayerConfig 资产
    /// - 导入时更新 Projectile prefab 伤害配置
    ///
    /// 迁移来源：
    /// - Player 解析：CastleDbService.cs:389-413
    /// - Override 解析：CastleDbService.cs:419-440
    /// - 校验逻辑：CastleDbImporter.cs:553-611
    /// - PlayerConfig 生成：CastleDbImporter.cs:888-952
    /// - Projectile 赋值：CastleDbImporter.cs:703-783
    /// </summary>
    public class PlayerDataProvider : CdbDataProviderBase
    {
        /// <summary>
        /// Provider ID，对应 Meta Sheet 中的 providerId
        /// </summary>
        public override string ExpectedProviderId => "Player";

        // ===== 缓存数据 =====
        private PlayerEntry _player;
        private List<PlayerAttackOverrideEntry> _overrides = new List<PlayerAttackOverrideEntry>();

        // ===== 常量 =====
        private const string PLAYER_CONFIG_PATH = "Assets/Resources/Config/PlayerConfig.asset";

        #region 初始化

        /// <summary>
        /// 解析 .cdb 数据并缓存
        /// </summary>
        protected override void OnInitialize(CastleDbRoot root, CdbModuleDescriptor descriptor)
        {
            _player = null;
            _overrides.Clear();

            foreach (var sheet in root.sheets)
            {
                switch (sheet.name)
                {
                    case "Player":
                        var players = ConvertLinesToPlayerEntries(sheet.lines);
                        if (players.Count > 0)
                        {
                            _player = players[0]; // Player Sheet 应该只有一行
                            Debug.Log($"[PlayerDataProvider] 解析 Player Sheet：id={_player.id}");
                        }
                        break;

                    case "PlayerAttackOverride":
                        _overrides = ConvertLinesToOverrideEntries(sheet.lines);
                        Debug.Log($"[PlayerDataProvider] 解析 PlayerAttackOverride Sheet：{_overrides.Count} 条");
                        break;

                    case "Meta":
                        // Meta Sheet 由 Registry 处理，此处跳过
                        break;

                    default:
                        Debug.LogWarning($"[PlayerDataProvider] 忽略未知 Sheet：{sheet.name}");
                        break;
                }
            }
        }

        #endregion

        #region Player 解析

        /// <summary>
        /// 将通用 lines 转换为 PlayerEntry 列表
        /// 迁移自 CastleDbService.cs:389-413
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
                        jumpImpulse = GetFloatValue(dict, "jumpImpulse"),
                        climbSpeed = GetFloatValue(dict, "climbSpeed"),
                        baseAttackDamage = GetFloatValue(dict, "baseAttackDamage")
                    });
                }
            }
            return result;
        }

        #endregion

        #region PlayerAttackOverride 解析

        /// <summary>
        /// 将通用 lines 转换为 PlayerAttackOverrideEntry 列表
        /// 迁移自 CastleDbService.cs:419-440
        /// </summary>
        private List<PlayerAttackOverrideEntry> ConvertLinesToOverrideEntries(List<object> lines)
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

        #endregion

        #region 数据校验

        /// <summary>
        /// 校验 Player 和 Override 数据完整性
        /// 迁移自 CastleDbImporter.cs:553-611
        /// </summary>
        protected override List<string> OnValidate(CdbModuleDescriptor descriptor)
        {
            var errors = new List<string>();

            // ===== Player 校验 =====
            if (_player == null)
            {
                errors.Add("Player Sheet 不存在或为空");
                return errors; // 无法继续校验
            }

            // 校验 id
            if (string.IsNullOrWhiteSpace(_player.id))
            {
                errors.Add("Player 的 id 为空");
            }

            // 校验数值字段
            if (_player.maxHealth <= 0)
                errors.Add($"Player 'maxHealth' <= 0 ({_player.maxHealth})");
            if (_player.walkSpeed <= 0)
                errors.Add($"Player 'walkSpeed' <= 0 ({_player.walkSpeed})");
            if (_player.runSpeed <= 0)
                errors.Add($"Player 'runSpeed' <= 0 ({_player.runSpeed})");
            if (_player.airWalkSpeed <= 0)
                errors.Add($"Player 'airWalkSpeed' <= 0 ({_player.airWalkSpeed})");
            if (_player.jumpImpulse <= 0)
                errors.Add($"Player 'jumpImpulse' <= 0 ({_player.jumpImpulse})");
            if (_player.climbSpeed <= 0)
                errors.Add($"Player 'climbSpeed' <= 0 ({_player.climbSpeed})");
            if (_player.baseAttackDamage <= 0)
                errors.Add($"Player 'baseAttackDamage' <= 0 ({_player.baseAttackDamage})");
            if (_player.invincibilityTime < 0)
                errors.Add($"Player 'invincibilityTime' < 0 ({_player.invincibilityTime})");

            // ===== PlayerAttackOverride 校验 =====
            foreach (var pao in _overrides)
            {
                // damageMultiplier 必须 > 0
                if (pao.damageMultiplier <= 0)
                {
                    errors.Add($"PlayerAttackOverride '{pao.id}' 的 damageMultiplier <= 0 ({pao.damageMultiplier})");
                }

                // damageOverride 若填写必须 >= 0
                if (pao.damageOverride < 0)
                {
                    errors.Add($"PlayerAttackOverride '{pao.id}' 的 damageOverride < 0 ({pao.damageOverride})");
                }

                // targetId 不能为空
                if (string.IsNullOrWhiteSpace(pao.targetId))
                {
                    errors.Add($"PlayerAttackOverride '{pao.id}' 的 targetId 为空");
                }

                // playerId 必须匹配
                if (!string.IsNullOrWhiteSpace(pao.playerId) && pao.playerId != _player.id)
                {
                    errors.Add($"PlayerAttackOverride '{pao.id}' 的 playerId '{pao.playerId}' 与 Player Sheet 的 id '{_player.id}' 不匹配");
                }
            }

            // 唯一性校验：(playerId, targetType, targetId) 必须唯一
            var uniqueCheck = new HashSet<string>();
            foreach (var pao in _overrides)
            {
                string key = $"{pao.playerId}|{pao.targetType}|{pao.targetId}";
                if (uniqueCheck.Contains(key))
                {
                    errors.Add($"PlayerAttackOverride 存在重复的三元组: playerId='{pao.playerId}', targetType={pao.targetType}, targetId='{pao.targetId}'");
                }
                uniqueCheck.Add(key);
            }

            return errors;
        }

        #endregion

        #region 导入（Editor Only）

        /// <summary>
        /// 导入 Player 数据生成 PlayerConfig 资产
        /// 迁移自 CastleDbImporter.cs:888-952
        /// </summary>
        protected override CdbImportResult OnImport(CdbModuleDescriptor descriptor)
        {
            var builder = new CdbImportResultBuilder(ExpectedProviderId);

            if (_player == null)
            {
                builder.AddError("Player 数据为空，无法导入");
                return builder.Build();
            }

            try
            {
                // ===== 1. 创建或更新 PlayerConfig =====
                CreateOrUpdatePlayerConfig(builder);

                // ===== 2. 更新 Projectile prefab 伤害配置 =====
                UpdateProjectileDamage(builder);
            }
            catch (Exception ex)
            {
                builder.AddError($"导入失败: {ex.Message}");
            }

            return builder.Build();
        }

        /// <summary>
        /// 创建或更新 PlayerConfig
        /// 迁移自 CastleDbImporter.cs:888-952
        /// </summary>
        private void CreateOrUpdatePlayerConfig(CdbImportResultBuilder builder)
        {
            // 确保输出目录存在
            string configDir = Path.GetDirectoryName(PLAYER_CONFIG_PATH);
            if (!string.IsNullOrEmpty(configDir) && !Directory.Exists(configDir))
            {
                Directory.CreateDirectory(configDir);
                builder.AddInfo($"创建目录：{configDir}");
            }

            // 查找或创建 PlayerConfig
            PlayerConfig config = AssetDatabase.LoadAssetAtPath<PlayerConfig>(PLAYER_CONFIG_PATH);

            bool isNew = config == null;
            if (isNew)
            {
                // 创建新 PlayerConfig
                config = ScriptableObject.CreateInstance<PlayerConfig>();
                AssetDatabase.CreateAsset(config, PLAYER_CONFIG_PATH);
                builder.Created(PLAYER_CONFIG_PATH);
            }
            else
            {
                builder.Updated(PLAYER_CONFIG_PATH);
            }

            // 应用 CastleDB 数据
            config.ApplyFromCastleDb(_player, _overrides);

            // 记录导入摘要
            builder.AddInfo($"PlayerConfig: id={_player.id}");
            builder.AddInfo($"  基础属性映射:");
            builder.AddInfo($"    • maxHealth: {_player.maxHealth}");
            builder.AddInfo($"    • invincibilityTime: {_player.invincibilityTime}");
            builder.AddInfo($"    • walkSpeed: {_player.walkSpeed}");
            builder.AddInfo($"    • runSpeed: {_player.runSpeed}");
            builder.AddInfo($"    • airWalkSpeed: {_player.airWalkSpeed}");
            builder.AddInfo($"    • jumpImpulse: {_player.jumpImpulse}");
            builder.AddInfo($"    • climbSpeed: {_player.climbSpeed}");
            builder.AddInfo($"    • baseAttackDamage: {_player.baseAttackDamage}");
            builder.AddInfo($"  攻击覆盖配置: {_overrides.Count} 条");

            // 列出所有攻击覆盖配置
            foreach (var ov in _overrides)
            {
                string targetTypeStr = ov.targetType == 0 ? "Hitbox" : "Projectile";
                string overrideInfo = ov.damageOverride > 0
                    ? $"override={ov.damageOverride}"
                    : $"multiplier={ov.damageMultiplier}";
                builder.AddInfo($"    • {ov.id}: {targetTypeStr} '{ov.targetId}' ({overrideInfo})");
            }

            // 标记为 dirty
            EditorUtility.SetDirty(config);
        }

        /// <summary>
        /// 更新 Projectile prefab 伤害配置
        /// 迁移自 CastleDbImporter.cs:703-783
        /// </summary>
        private void UpdateProjectileDamage(CdbImportResultBuilder builder)
        {
            // 筛选 targetType=1（Projectile）的覆盖配置
            var projectileOverrides = _overrides.Where(o => o.targetType == 1).ToList();

            if (projectileOverrides.Count == 0)
            {
                builder.AddInfo("无 Projectile 类型的攻击覆盖配置");
                return;
            }

            builder.AddInfo($"开始更新 Projectile prefab 伤害配置（{projectileOverrides.Count} 个）");

            foreach (var ov in projectileOverrides)
            {
                try
                {
                    // targetId 是 Resources 路径（例如 "Prefabs/Projectiles/Player/Arrow"）
                    GameObject prefab = Resources.Load<GameObject>(ov.targetId);

                    if (prefab == null)
                    {
                        builder.AddWarning($"Projectile prefab 未找到: {ov.targetId}");
                        continue;
                    }

                    // 获取 Projectile 组件
                    Projectile projectile = prefab.GetComponent<Projectile>();
                    if (projectile == null)
                    {
                        builder.AddWarning($"Projectile prefab 缺少 Projectile 组件: {ov.targetId}");
                        continue;
                    }

                    // 计算最终伤害（保持整型，与运行时链路一致）
                    int finalDamage;
                    if (ov.damageOverride > 0)
                    {
                        finalDamage = Mathf.RoundToInt(ov.damageOverride);
                    }
                    else
                    {
                        finalDamage = Mathf.Max(1, Mathf.RoundToInt(_player.baseAttackDamage * ov.damageMultiplier));
                    }

                    // 应用伤害（根据属性类型使用正确的赋值方式）
                    SerializedObject so = new SerializedObject(projectile);
                    SerializedProperty damageProp = so.FindProperty("damage");

                    if (damageProp != null)
                    {
                        // 检查属性类型并使用正确的赋值方法
                        if (damageProp.propertyType == SerializedPropertyType.Integer)
                        {
                            int oldDamage = damageProp.intValue;
                            damageProp.intValue = finalDamage;
                            so.ApplyModifiedProperties();

                            if (oldDamage != finalDamage)
                            {
                                builder.AddInfo($"  ✓ {ov.targetId}: damage {oldDamage} → {finalDamage}");
                            }
                        }
                        else if (damageProp.propertyType == SerializedPropertyType.Float)
                        {
                            float oldDamage = damageProp.floatValue;
                            damageProp.floatValue = finalDamage;
                            so.ApplyModifiedProperties();

                            if (Mathf.Abs(oldDamage - finalDamage) > 0.01f)
                            {
                                builder.AddInfo($"  ✓ {ov.targetId}: damage {oldDamage:F1} → {finalDamage:F1}");
                            }
                        }
                        else
                        {
                            builder.AddWarning($"Projectile '{ov.targetId}' 的 'damage' 字段类型不支持: {damageProp.propertyType}");
                        }

                        EditorUtility.SetDirty(projectile);
                        builder.Updated($"Resources/{ov.targetId}.prefab");
                    }
                    else
                    {
                        builder.AddWarning($"Projectile '{ov.targetId}' 没有 'damage' 字段");
                    }
                }
                catch (Exception ex)
                {
                    builder.AddError($"更新 Projectile '{ov.targetId}' 失败: {ex.Message}");
                }
            }
        }

        #endregion

        #region 数据查询

        /// <summary>
        /// 获取指定类型的所有条目
        /// </summary>
        public override List<T> GetAllEntries<T>()
        {
            if (typeof(T) == typeof(PlayerEntry))
            {
                return _player != null ? new List<T> { _player as T } : new List<T>();
            }

            if (typeof(T) == typeof(PlayerAttackOverrideEntry))
            {
                return _overrides.Cast<T>().ToList();
            }

            return new List<T>();
        }

        /// <summary>
        /// 按 ID 获取指定类型的条目
        /// </summary>
        public override T GetEntryById<T>(string id)
        {
            if (typeof(T) == typeof(PlayerEntry))
            {
                return _player?.id == id ? _player as T : null;
            }

            if (typeof(T) == typeof(PlayerAttackOverrideEntry))
            {
                return _overrides.FirstOrDefault(o => o.id == id) as T;
            }

            return null;
        }

        /// <summary>
        /// 获取玩家配置
        /// </summary>
        public PlayerEntry GetPlayer()
        {
            return _player;
        }

        /// <summary>
        /// 获取所有攻击覆盖配置
        /// </summary>
        public List<PlayerAttackOverrideEntry> GetAllOverrides()
        {
            return new List<PlayerAttackOverrideEntry>(_overrides);
        }

        #endregion

        #region 重置

        /// <summary>
        /// 清除缓存数据
        /// </summary>
        protected override void OnReset()
        {
            _player = null;
            _overrides.Clear();
        }

        #endregion

        #region 辅助方法

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

        #endregion
    }
}
