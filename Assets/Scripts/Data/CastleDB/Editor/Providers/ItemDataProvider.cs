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
    /// Item 数据提供者（0.4 版本 Phase 1）
    /// 处理 Item.cdb 中的 Item Sheet
    ///
    /// 职责：
    /// - 解析 Item Sheet 数据
    /// - 校验 Item 数据完整性（含 itemType/abilityId/consumeEffectJson 格式）
    /// - 导入时生成 ItemCatalog 资产
    ///
    /// 依赖：
    /// - 无（dependencies=""）
    ///
    /// 规范（0.4 契约）：
    /// - [C-Data-2] Item.cdb schema 校验
    /// - [C-Data-3] consumeEffectJson 必须为 JSON 对象，支持 heal:number
    /// - [C-Data-4] 生成 ItemCatalog.asset（路径：Assets/Resources/Config/ItemCatalog.asset）
    /// </summary>
    public class ItemDataProvider : CdbDataProviderBase
    {
        /// <summary>
        /// Provider ID，对应 Meta Sheet 中的 providerId
        /// </summary>
        public override string ExpectedProviderId => "Item";

        // ===== 缓存数据 =====
        private List<ItemEntry> _items = new List<ItemEntry>();

        // ===== 常量 =====
        private const string ITEM_CATALOG_PATH = "Assets/Resources/Config/ItemCatalog.asset";

        #region 初始化

        /// <summary>
        /// 解析 .cdb 数据并缓存
        /// </summary>
        protected override void OnInitialize(CastleDbRoot root, CdbModuleDescriptor descriptor)
        {
            _items.Clear();

            foreach (var sheet in root.sheets)
            {
                switch (sheet.name)
                {
                    case "Item":
                        _items = ConvertLinesToItemEntries(sheet.lines);
                        Debug.Log($"[ItemDataProvider] 解析 Item Sheet：{_items.Count} 条");
                        break;

                    case "Meta":
                        // Meta Sheet 由 Registry 处理，此处跳过
                        break;

                    default:
                        Debug.LogWarning($"[ItemDataProvider] 忽略未知 Sheet：{sheet.name}");
                        break;
                }
            }
        }

        #endregion

        #region Item 解析

        /// <summary>
        /// 将通用 lines 转换为 ItemEntry 列表
        /// </summary>
        private List<ItemEntry> ConvertLinesToItemEntries(List<object> lines)
        {
            var result = new List<ItemEntry>();
            if (lines == null) return result;

            foreach (var line in lines)
            {
                if (line is Dictionary<string, object> dict)
                {
                    result.Add(new ItemEntry
                    {
                        id = GetStringValue(dict, "id"),
                        displayName = GetStringValue(dict, "displayName"),
                        itemType = GetStringValue(dict, "itemType"), // CastleDB 中存储为字符串
                        icon = GetStringValue(dict, "icon"),
                        maxStack = GetIntValue(dict, "maxStack"),
                        abilityId = GetStringValue(dict, "abilityId"),
                        relicId = GetStringValue(dict, "relicId"),
                        consumeEffectJson = GetStringValue(dict, "consumeEffectJson"),
                        uiTag = GetStringValue(dict, "uiTag")
                    });
                }
            }
            return result;
        }

        #endregion

        #region 数据校验

        /// <summary>
        /// 校验 Item 数据完整性
        /// 规范（[C-Data-2] + [C-Data-3]）：
        /// - schemaVersion 必须为 0.4
        /// - itemType 必须是 "Ability"|"Consumable"|"Material"（大小写敏感）
        /// - itemType=Ability 时 abilityId 必填
        /// - consumeEffectJson 必须为 JSON 对象（如非空）
        /// - consumeEffectJson 支持键 heal:number，多余键 Warning
        /// </summary>
        protected override List<string> OnValidate(CdbModuleDescriptor descriptor)
        {
            var errors = new List<string>();

            // ===== 1. id 非空唯一性校验 =====
            var idSet = new HashSet<string>();
            foreach (var item in _items)
            {
                if (string.IsNullOrWhiteSpace(item.id))
                {
                    errors.Add($"Item id 不能为空");
                    continue;
                }

                if (idSet.Contains(item.id))
                {
                    errors.Add($"Item id 重复：'{item.id}'");
                }
                idSet.Add(item.id);
            }

            // ===== 2. itemType 枚举校验（大小写敏感） =====
            var validTypes = new HashSet<string> { "Ability", "Consumable", "Material", "Relic" };
            foreach (var item in _items)
            {
                if (string.IsNullOrWhiteSpace(item.itemType))
                {
                    errors.Add($"Item '{item.id}' 的 itemType 不能为空");
                    continue;
                }

                if (!validTypes.Contains(item.itemType))
                {
                    errors.Add($"Item '{item.id}' 的 itemType '{item.itemType}' 非法，必须是 Ability|Consumable|Material（大小写敏感）");
                }
            }

            // ===== 3. abilityId 必填性与跨文件引用校验（itemType=Ability 时） =====
            // 获取 PlayerAbility Provider 进行跨文件引用校验
            var abilityProvider = CdbDataProviderRegistry.Instance.GetProvider("PlayerAbility") as AbilityDataProvider;

            foreach (var item in _items)
            {
                if (item.itemType == "Ability")
                {
                    // 3.1 必填性校验
                    if (string.IsNullOrWhiteSpace(item.abilityId))
                    {
                        errors.Add($"Item '{item.id}' 的 itemType=Ability，但 abilityId 为空");
                        continue;
                    }

                    // 3.2 跨文件引用校验（需要 PlayerAbility Provider 已初始化）
                    if (abilityProvider != null && abilityProvider.IsInitialized)
                    {
                        var ability = abilityProvider.GetEntryById<AbilityEntry>(item.abilityId);
                        if (ability == null)
                        {
                            errors.Add($"Item '{item.id}' 的 abilityId '{item.abilityId}' 在 PlayerAbility 中不存在");
                        }
                    }
                    else
                    {
                        // PlayerAbility Provider 未初始化，记录 Warning（不阻断导入）
                        Debug.LogWarning($"[ItemDataProvider] PlayerAbility Provider 未初始化，跳过 Item '{item.id}' 的 abilityId 引用校验");
                    }
                }
            }

            // ===== 3.5 relicId 必填性与跨文件引用校验（itemType=Relic 时） =====
            var relicProvider = CdbDataProviderRegistry.Instance.GetProvider("Relic") as RelicDataProvider;

            foreach (var item in _items)
            {
                if (item.itemType != "Relic")
                {
                    // 允许旧数据/非遗物物品不填写 relicId（如填写则给 Warning）
                    if (!string.IsNullOrWhiteSpace(item.relicId))
                    {
                        Debug.LogWarning($"[ItemDataProvider] Item '{item.id}' 的 relicId 将被忽略（itemType={item.itemType}）");
                    }
                    continue;
                }

                if (string.IsNullOrWhiteSpace(item.relicId))
                {
                    errors.Add($"Item '{item.id}' 的 itemType=Relic，但 relicId 为空");
                    continue;
                }

                if (relicProvider != null && relicProvider.IsInitialized)
                {
                    var relic = relicProvider.GetEntryById<RelicDefinition>(item.relicId);
                    if (relic == null)
                    {
                        errors.Add($"Item '{item.id}' 的 relicId '{item.relicId}' 在 Relic 中不存在");
                    }
                }
                else
                {
                    Debug.LogWarning($"[ItemDataProvider] Relic Provider 未初始化，跳过 Item '{item.id}' 的 relicId 引用校验");
                }

                // 0.5 最小约束：遗物不堆叠
                if (item.maxStack != 1)
                {
                    errors.Add($"Item '{item.id}' 的 itemType=Relic 时 maxStack 必须为 1（当前={item.maxStack}）");
                }

                if (!string.IsNullOrWhiteSpace(item.abilityId))
                {
                    errors.Add($"Item '{item.id}' 的 itemType=Relic 时 abilityId 必须为空（当前='{item.abilityId}'）");
                }

                if (!string.IsNullOrWhiteSpace(item.consumeEffectJson))
                {
                    errors.Add($"Item '{item.id}' 的 itemType=Relic 时 consumeEffectJson 必须为空");
                }
            }

            // ===== 4. consumeEffectJson 格式校验（非空时必须是 JSON 对象） =====
            foreach (var item in _items)
            {
                if (!string.IsNullOrWhiteSpace(item.consumeEffectJson))
                {
                    var parsed = CastleDbJsonUtil.TryParseJsonObject(item.consumeEffectJson);
                    if (parsed == null)
                    {
                        errors.Add($"Item '{item.id}' 的 consumeEffectJson 必须是 JSON 对象 ({{...}})，不能是基本类型或数组");
                        continue;
                    }

                    // 校验 heal 字段类型（如果存在）
                    if (parsed.ContainsKey("heal"))
                    {
                        var healValue = parsed["heal"];
                        if (!(healValue is int || healValue is long || healValue is float || healValue is double))
                        {
                            errors.Add($"Item '{item.id}' 的 consumeEffectJson.heal 必须是 number 类型");
                        }
                    }

                    // 多余键 Warning（不阻断导入）
                    var knownKeys = new HashSet<string> { "heal" };
                    foreach (var key in parsed.Keys)
                    {
                        if (!knownKeys.Contains(key))
                        {
                            Debug.LogWarning($"[ItemDataProvider] Item '{item.id}' 的 consumeEffectJson 包含未知键：'{key}'（将被忽略）");
                        }
                    }
                }
            }

            // ===== 5. icon 路径非空校验（可选：Resources 路径存在性检查） =====
            foreach (var item in _items)
            {
                if (string.IsNullOrWhiteSpace(item.icon))
                {
                    Debug.LogWarning($"[ItemDataProvider] Item '{item.id}' 的 icon 为空");
                }
            }

            // ===== 6. maxStack 范围校验 =====
            foreach (var item in _items)
            {
                if (item.maxStack <= 0)
                {
                    errors.Add($"Item '{item.id}' 的 maxStack 必须 > 0，当前值：{item.maxStack}");
                }
            }

            return errors;
        }

        #endregion

        #region 导入（Editor Only）

        /// <summary>
        /// 导入 Item 数据生成 ItemCatalog 资产
        /// 规范（[C-Data-4]）：
        /// - 路径：Assets/Resources/Config/ItemCatalog.asset
        /// - 结构：ItemDefinition[] items
        /// - 转换：ItemEntry → ItemDefinition（解析 consumeEffectJson）
        /// </summary>
        protected override CdbImportResult OnImport(CdbModuleDescriptor descriptor)
        {
            var builder = new CdbImportResultBuilder(ExpectedProviderId);

            try
            {
                // 确保输出目录存在
                string catalogDir = Path.GetDirectoryName(ITEM_CATALOG_PATH);
                if (!string.IsNullOrEmpty(catalogDir) && !Directory.Exists(catalogDir))
                {
                    Directory.CreateDirectory(catalogDir);
                    builder.AddInfo($"创建目录：{catalogDir}");
                }

                // 查找或创建 ItemCatalog
                ItemCatalog catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(ITEM_CATALOG_PATH);

                bool isNew = catalog == null;
                if (isNew)
                {
                    // 创建新 ItemCatalog
                    catalog = ScriptableObject.CreateInstance<ItemCatalog>();
                    AssetDatabase.CreateAsset(catalog, ITEM_CATALOG_PATH);
                    builder.Created(ITEM_CATALOG_PATH);
                }
                else
                {
                    builder.Updated(ITEM_CATALOG_PATH);
                }

                // 转换 ItemEntry → ItemDefinition
                var itemDefinitions = new List<ItemDefinition>();
                foreach (var entry in _items)
                {
                    itemDefinitions.Add(ConvertToItemDefinition(entry, builder));
                }

                // 应用到 ItemCatalog
                catalog.ApplyFromCastleDb(itemDefinitions);

                // 记录导入摘要
                builder.AddInfo($"ItemCatalog: {itemDefinitions.Count} 个 Item 配置");

                // 统计类型分布
                var typeStats = new Dictionary<string, int>();
                foreach (var item in _items)
                {
                    if (!typeStats.ContainsKey(item.itemType))
                    {
                        typeStats[item.itemType] = 0;
                    }
                    typeStats[item.itemType]++;
                }

                builder.AddInfo($"  类型分布:");
                foreach (var kvp in typeStats)
                {
                    builder.AddInfo($"    • {kvp.Key}: {kvp.Value} 个");
                }

                // 列出所有 Item 配置
                builder.AddInfo($"  Item 详情:");
                foreach (var item in _items)
                {
                    string abilityStr = !string.IsNullOrEmpty(item.abilityId) ? $", abilityId={item.abilityId}" : "";
                    string relicStr = !string.IsNullOrEmpty(item.relicId) ? $", relicId={item.relicId}" : "";
                    builder.AddInfo($"    • {item.id}: {item.itemType}, maxStack={item.maxStack}{abilityStr}{relicStr}");
                }

                // 标记为 dirty
                EditorUtility.SetDirty(catalog);
            }
            catch (Exception ex)
            {
                builder.AddError($"创建/更新 ItemCatalog 失败: {ex.Message}");
            }

            return builder.Build();
        }

        /// <summary>
        /// 转换 ItemEntry → ItemDefinition
        /// 解析 consumeEffectJson 并提取 heal 值
        /// </summary>
        private ItemDefinition ConvertToItemDefinition(ItemEntry entry, CdbImportResultBuilder builder)
        {
            var def = new ItemDefinition
            {
                id = entry.id,
                displayName = entry.displayName,
                itemType = ParseItemType(entry.itemType),
                icon = entry.icon,
                abilityId = entry.abilityId,
                relicId = entry.relicId,
                maxStack = entry.maxStack,
                consumeEffect = new ItemConsumeEffect(0), // 默认值
                consumeEffectRawJson = entry.consumeEffectJson,
                uiTag = entry.uiTag
            };

            // 解析 consumeEffectJson
            if (!string.IsNullOrWhiteSpace(entry.consumeEffectJson))
            {
                var parsed = CastleDbJsonUtil.TryParseJsonObject(entry.consumeEffectJson);
                if (parsed != null)
                {
                    if (parsed.ContainsKey("heal"))
                    {
                        try
                        {
                            int healValue = Convert.ToInt32(parsed["heal"]);
                            def.consumeEffect = new ItemConsumeEffect(healValue);
                        }
                        catch (Exception ex)
                        {
                            builder.AddWarning($"Item '{entry.id}' 解析 heal 值失败: {ex.Message}，使用默认值 0");
                        }
                    }
                }
            }

            return def;
        }

        /// <summary>
        /// 解析 itemType 字符串为枚举
        /// </summary>
        private ItemType ParseItemType(string itemTypeStr)
        {
            return itemTypeStr switch
            {
                "Ability" => ItemType.Ability,
                "Consumable" => ItemType.Consumable,
                "Material" => ItemType.Material,
                "Relic" => ItemType.Relic,
                _ => ItemType.Material // 默认值（不应该到达这里，Validate 已校验）
            };
        }

        #endregion

        #region 数据查询

        /// <summary>
        /// 获取指定类型的所有条目
        /// </summary>
        public override List<T> GetAllEntries<T>()
        {
            if (typeof(T) == typeof(ItemEntry))
            {
                return _items.Cast<T>().ToList();
            }

            return new List<T>();
        }

        /// <summary>
        /// 按 ID 获取指定类型的条目
        /// </summary>
        public override T GetEntryById<T>(string id)
        {
            if (typeof(T) == typeof(ItemEntry))
            {
                return _items.FirstOrDefault(i => i.id == id) as T;
            }

            return null;
        }

        /// <summary>
        /// 获取所有 Item 配置
        /// </summary>
        public List<ItemEntry> GetAllItems()
        {
            return new List<ItemEntry>(_items);
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

        #endregion
    }
}
