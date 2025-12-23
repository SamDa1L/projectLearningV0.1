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
    /// PlayerAbility 数据提供者（0.3 版本 Phase 4）
    /// 处理 PlayerAbility.cdb 中的 Ability Sheet
    ///
    /// 职责：
    /// - 解析 Ability Sheet 数据
    /// - 校验 Ability 数据完整性（含 paramsJson 格式、priority、Registry 注册）
    /// - 导入时生成 AbilityCatalog 资产
    ///
    /// 依赖：
    /// - Player Provider（dependencies="Player"，但当前实现不需要运行时查询）
    ///
    /// 迁移来源：
    /// - Ability 解析：CastleDbService.cs:363-383
    /// - 校验逻辑：CastleDbImporter.cs:613-696
    /// - AbilityCatalog 生成：CastleDbImporter.cs:954-1031
    /// </summary>
    public class AbilityDataProvider : CdbDataProviderBase
    {
        /// <summary>
        /// Provider ID，对应 Meta Sheet 中的 providerId
        /// </summary>
        public override string ExpectedProviderId => "PlayerAbility";

        // ===== 缓存数据 =====
        private List<AbilityEntry> _abilities = new List<AbilityEntry>();

        // ===== 常量 =====
        private const string ABILITY_CATALOG_PATH = "Assets/Resources/Config/AbilityCatalog.asset";

        #region 初始化

        /// <summary>
        /// 解析 .cdb 数据并缓存
        /// </summary>
        protected override void OnInitialize(CastleDbRoot root, CdbModuleDescriptor descriptor)
        {
            _abilities.Clear();

            foreach (var sheet in root.sheets)
            {
                switch (sheet.name)
                {
                    case "Ability":
                        _abilities = ConvertLinesToAbilityEntries(sheet.lines);
                        Debug.Log($"[AbilityDataProvider] 解析 Ability Sheet：{_abilities.Count} 条");
                        break;

                    case "Meta":
                        // Meta Sheet 由 Registry 处理，此处跳过
                        break;

                    default:
                        Debug.LogWarning($"[AbilityDataProvider] 忽略未知 Sheet：{sheet.name}");
                        break;
                }
            }
        }

        #endregion

        #region Ability 解析

        /// <summary>
        /// 将通用 lines 转换为 AbilityEntry 列表
        /// 迁移自 CastleDbService.cs:363-383
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
                        hookType = GetIntValue(dict, "hookType"),
                        priority = GetIntValue(dict, "priority"),
                        enabled = GetBoolValue(dict, "enabled"),
                        paramsJson = GetStringValue(dict, "paramsJson")
                    });
                }
            }
            return result;
        }

        #endregion

        #region 数据校验

        /// <summary>
        /// 校验 Ability 数据完整性
        /// 迁移自 CastleDbImporter.cs:613-696
        /// </summary>
        protected override List<string> OnValidate(CdbModuleDescriptor descriptor)
        {
            var errors = new List<string>();

            // ===== 1. Registry 校验：所有 ability.id 必须在 AbilityRegistry 中注册 =====
            foreach (var ability in _abilities)
            {
                if (!AbilityRegistry.IsRegistered(ability.id))
                {
                    errors.Add($"Ability '{ability.id}' 未在 AbilityRegistry 中注册");
                }
            }

            // ===== 2. paramsJson 格式校验（如果非空，必须是 JSON 对象） =====
            foreach (var ability in _abilities)
            {
                if (!string.IsNullOrWhiteSpace(ability.paramsJson))
                {
                    // 使用 CastleDbJsonUtil 公开 API 校验格式
                    var parsed = CastleDbJsonUtil.TryParseJsonObject(ability.paramsJson);
                    if (parsed == null)
                    {
                        errors.Add($"Ability '{ability.id}' 的 paramsJson 必须是 JSON 对象 ({{...}})，不能是基本类型或数组");
                    }
                }
            }

            // ===== 3. hookType 范围校验 =====
            foreach (var ability in _abilities)
            {
                if (ability.hookType < 0 || ability.hookType > 4)
                {
                    errors.Add($"Ability '{ability.id}' 的 hookType {ability.hookType} 超出范围 (0-4)");
                }
            }

            // ===== 4. priority 唯一性校验（同一 hookType 内 priority 必须唯一） =====
            var hookTypeGroups = _abilities.GroupBy(a => a.hookType);
            foreach (var group in hookTypeGroups)
            {
                var priorityCheck = new HashSet<int>();
                foreach (var ability in group)
                {
                    if (priorityCheck.Contains(ability.priority))
                    {
                        errors.Add($"hookType {group.Key} 内存在重复的 priority: {ability.priority}");
                    }
                    priorityCheck.Add(ability.priority);
                }
            }

            // ===== 5. 启用能力数量检查（每个 hookType 至少有一个启用的能力，否则 warning） =====
            // 注意：这是 warning 而不是 error，但在 OnValidate 中只能返回 errors
            // 我们通过 Info 日志输出 warning，不阻断导入
            var enabledByHookType = _abilities
                .Where(a => a.enabled)
                .GroupBy(a => a.hookType)
                .ToDictionary(g => g.Key, g => g.Count());

            for (int hookType = 0; hookType <= 4; hookType++)
            {
                if (!enabledByHookType.ContainsKey(hookType) || enabledByHookType[hookType] == 0)
                {
                    Debug.LogWarning($"[AbilityDataProvider] hookType {hookType} 没有任何启用的能力");
                }
            }

            return errors;
        }

        #endregion

        #region 导入（Editor Only）

        /// <summary>
        /// 导入 Ability 数据生成 AbilityCatalog 资产
        /// 迁移自 CastleDbImporter.cs:954-1031
        /// </summary>
        protected override CdbImportResult OnImport(CdbModuleDescriptor descriptor)
        {
            var builder = new CdbImportResultBuilder(ExpectedProviderId);

            try
            {
                // 确保输出目录存在
                string catalogDir = Path.GetDirectoryName(ABILITY_CATALOG_PATH);
                if (!string.IsNullOrEmpty(catalogDir) && !Directory.Exists(catalogDir))
                {
                    Directory.CreateDirectory(catalogDir);
                    builder.AddInfo($"创建目录：{catalogDir}");
                }

                // 查找或创建 AbilityCatalog
                AbilityCatalog catalog = AssetDatabase.LoadAssetAtPath<AbilityCatalog>(ABILITY_CATALOG_PATH);

                bool isNew = catalog == null;
                if (isNew)
                {
                    // 创建新 AbilityCatalog
                    catalog = ScriptableObject.CreateInstance<AbilityCatalog>();
                    AssetDatabase.CreateAsset(catalog, ABILITY_CATALOG_PATH);
                    builder.Created(ABILITY_CATALOG_PATH);
                }
                else
                {
                    builder.Updated(ABILITY_CATALOG_PATH);
                }

                // 应用 CastleDB 数据
                catalog.ApplyFromCastleDb(_abilities);

                // 记录导入摘要
                builder.AddInfo($"AbilityCatalog: {_abilities.Count} 个能力配置");

                // 统计每个 hookType 的能力数量
                var hookTypeStats = new Dictionary<AbilityHookType, int>();
                foreach (AbilityHookType hookType in System.Enum.GetValues(typeof(AbilityHookType)))
                {
                    hookTypeStats[hookType] = 0;
                }

                foreach (var ability in _abilities)
                {
                    if (ability.enabled)
                    {
                        hookTypeStats[(AbilityHookType)ability.hookType]++;
                    }
                }

                builder.AddInfo($"  能力分布:");
                foreach (var kvp in hookTypeStats)
                {
                    builder.AddInfo($"    • {kvp.Key}: {kvp.Value} 个启用");
                }

                // 列出所有能力配置
                builder.AddInfo($"  能力详情:");
                foreach (var ability in _abilities)
                {
                    string enabledStr = ability.enabled ? "✓" : "✗";
                    string hookTypeName = ((AbilityHookType)ability.hookType).ToString();
                    builder.AddInfo($"    • [{enabledStr}] {ability.id}: {hookTypeName}, priority={ability.priority}");
                }

                // 标记为 dirty
                EditorUtility.SetDirty(catalog);
            }
            catch (Exception ex)
            {
                builder.AddError($"创建/更新 AbilityCatalog 失败: {ex.Message}");
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
            if (typeof(T) == typeof(AbilityEntry))
            {
                return _abilities.Cast<T>().ToList();
            }

            return new List<T>();
        }

        /// <summary>
        /// 按 ID 获取指定类型的条目
        /// </summary>
        public override T GetEntryById<T>(string id)
        {
            if (typeof(T) == typeof(AbilityEntry))
            {
                return _abilities.FirstOrDefault(a => a.id == id) as T;
            }

            return null;
        }

        /// <summary>
        /// 获取所有能力配置
        /// </summary>
        public List<AbilityEntry> GetAllAbilities()
        {
            return new List<AbilityEntry>(_abilities);
        }

        /// <summary>
        /// 按 hookType 获取能力列表
        /// </summary>
        public List<AbilityEntry> GetAbilitiesByHookType(int hookType)
        {
            return _abilities.Where(a => a.hookType == hookType).ToList();
        }

        #endregion

        #region 重置

        /// <summary>
        /// 清除缓存数据
        /// </summary>
        protected override void OnReset()
        {
            _abilities.Clear();
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
