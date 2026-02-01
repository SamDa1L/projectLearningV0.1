using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using CastleDB.Runtime;

namespace CastleDB.Editor.Providers
{
    /// <summary>
    /// Status 数据提供者（0.5 Phase 1-4）
    ///
    /// 职责：
    /// - 解析 Status Sheet 数据
    /// - 校验 Status 数据完整性（id/stackRule/maxStacks/modifiersJson）
    /// - 导入生成 StatusCatalog 资产
    /// </summary>
    public class StatusDataProvider : CdbDataProviderBase
    {
        public override string ExpectedProviderId => "Status";

        private const string STATUS_CATALOG_PATH = "Assets/Resources/Config/StatusCatalog.asset";

        private List<StatusRow> _rows = new List<StatusRow>();
        private readonly List<StatusDefinition> _definitions = new List<StatusDefinition>();
        private readonly Dictionary<string, StatusDefinition> _definitionsById = new Dictionary<string, StatusDefinition>();

        private class StatusRow
        {
            public string id;
            public string displayName;
            public float defaultDuration;
            public int stackRule;
            public int maxStacks;
            public string modifiersJson;
        }

        protected override void OnInitialize(CastleDbRoot root, CdbModuleDescriptor descriptor)
        {
            _rows.Clear();
            _definitions.Clear();
            _definitionsById.Clear();

            foreach (var sheet in root.sheets)
            {
                switch (sheet.name)
                {
                    case "Status":
                    case "StatusEffect":
                        _rows = ConvertLinesToStatusRows(sheet.lines);
                        Debug.Log($"[StatusDataProvider] 解析 {sheet.name} Sheet：{_rows.Count} 条");
                        break;

                    case "Meta":
                        break;

                    default:
                        Debug.LogWarning($"[StatusDataProvider] 忽略未知 Sheet：{sheet.name}");
                        break;
                }
            }

            RebuildDefinitionCache();
        }

        private List<StatusRow> ConvertLinesToStatusRows(List<object> lines)
        {
            var result = new List<StatusRow>();
            if (lines == null)
            {
                return result;
            }

            foreach (var line in lines)
            {
                if (line is Dictionary<string, object> dict)
                {
                    result.Add(new StatusRow
                    {
                        id = GetStringValue(dict, "id"),
                        displayName = GetStringValue(dict, "displayName"),
                        defaultDuration = GetFloatValue(dict, "defaultDuration"),
                        stackRule = GetIntValue(dict, "stackRule"),
                        maxStacks = GetIntValue(dict, "maxStacks"),
                        modifiersJson = GetStringValue(dict, "modifiersJson")
                    });
                }
            }

            return result;
        }

        protected override List<string> OnValidate(CdbModuleDescriptor descriptor)
        {
            var errors = new List<string>();

            // 1) id 非空唯一
            var idSet = new HashSet<string>();
            foreach (var row in _rows)
            {
                if (string.IsNullOrWhiteSpace(row.id))
                {
                    errors.Add("Status 存在空 id（id 不能为空）");
                    continue;
                }

                if (!idSet.Add(row.id))
                {
                    errors.Add($"Status id 重复: '{row.id}'");
                }
            }

            // 2) stackRule 合法（0-3）
            foreach (var row in _rows)
            {
                if (row.stackRule < 0 || row.stackRule > 3)
                {
                    errors.Add($"Status '{row.id}' 的 stackRule 超出范围 (0-3): {row.stackRule}");
                }
            }

            // 3) maxStacks >= 1
            foreach (var row in _rows)
            {
                if (row.maxStacks < 1)
                {
                    errors.Add($"Status '{row.id}' 的 maxStacks 必须 >= 1（当前 {row.maxStacks}）");
                }
            }

            // 4) defaultDuration >= 0（<=0 表示永久）
            foreach (var row in _rows)
            {
                if (row.defaultDuration < 0f)
                {
                    errors.Add($"Status '{row.id}' 的 defaultDuration 不能为负数（当前 {row.defaultDuration}）");
                }
            }

            // 5) modifiersJson 校验（若非空必须为对象）
            foreach (var row in _rows)
            {
                if (string.IsNullOrWhiteSpace(row.modifiersJson))
                {
                    continue;
                }

                var parsed = CastleDbJsonUtil.TryParseJsonObject(row.modifiersJson);
                if (parsed == null)
                {
                    errors.Add($"Status '{row.id}' 的 modifiersJson 必须是 JSON 对象 ({{...}})");
                    continue;
                }

                // 允许键（Phase 1-4 最小集）
                foreach (var key in parsed.Keys)
                {
                    if (!string.Equals(key, "moveSpeedMultiplier", StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.LogWarning($"[StatusDataProvider] Status '{row.id}' 的 modifiersJson 包含未知键 '{key}'（将被忽略）");
                    }
                }

                if (parsed.TryGetValue("moveSpeedMultiplier", out object value) && value != null)
                {
                    try
                    {
                        float mult = Convert.ToSingle(value);
                        if (mult < 0f)
                        {
                            errors.Add($"Status '{row.id}' 的 moveSpeedMultiplier 不能为负数（当前 {mult}）");
                        }
                    }
                    catch
                    {
                        errors.Add($"Status '{row.id}' 的 moveSpeedMultiplier 必须为数字");
                    }
                }
            }

            return errors;
        }

        protected override CdbImportResult OnImport(CdbModuleDescriptor descriptor)
        {
            var builder = new CdbImportResultBuilder(ExpectedProviderId);

            try
            {
                string catalogDir = Path.GetDirectoryName(STATUS_CATALOG_PATH);
                if (!string.IsNullOrEmpty(catalogDir) && !Directory.Exists(catalogDir))
                {
                    Directory.CreateDirectory(catalogDir);
                    builder.AddInfo($"创建目录：{catalogDir}");
                }

                StatusCatalog catalog = AssetDatabase.LoadAssetAtPath<StatusCatalog>(STATUS_CATALOG_PATH);
                bool isNew = catalog == null;
                if (isNew)
                {
                    catalog = ScriptableObject.CreateInstance<StatusCatalog>();
                    AssetDatabase.CreateAsset(catalog, STATUS_CATALOG_PATH);
                    builder.Created(STATUS_CATALOG_PATH);
                }
                else
                {
                    builder.Updated(STATUS_CATALOG_PATH);
                }

                List<StatusDefinition> defs = ConvertRowsToDefinitions(_rows, builder);
                catalog.ApplyFromCastleDb(defs);

                builder.AddInfo($"StatusCatalog: {defs.Count} 个状态配置");

                EditorUtility.SetDirty(catalog);
            }
            catch (Exception ex)
            {
                builder.AddError($"创建/更新 StatusCatalog 失败: {ex.Message}");
            }

            return builder.Build();
        }

        private static List<StatusDefinition> ConvertRowsToDefinitions(List<StatusRow> rows, CdbImportResultBuilder builder)
        {
            var result = new List<StatusDefinition>();
            if (rows == null)
            {
                return result;
            }

            foreach (var row in rows)
            {
                var def = new StatusDefinition
                {
                    id = row.id ?? "",
                    displayName = string.IsNullOrWhiteSpace(row.displayName) ? (row.id ?? "") : row.displayName,
                    defaultDuration = Mathf.Max(0f, row.defaultDuration),
                    stackRule = (StatusStackRule)Mathf.Clamp(row.stackRule, 0, 3),
                    maxStacks = Mathf.Max(1, row.maxStacks),
                    modifiers = StatusModifiers.Default
                };

                if (!string.IsNullOrWhiteSpace(row.modifiersJson))
                {
                    var parsed = CastleDbJsonUtil.TryParseJsonObject(row.modifiersJson);
                    if (parsed != null && parsed.TryGetValue("moveSpeedMultiplier", out object value) && value != null)
                    {
                        try
                        {
                            def.modifiers = new StatusModifiers(moveSpeedMultiplier: Mathf.Max(0f, Convert.ToSingle(value)));
                        }
                        catch (Exception ex)
                        {
                            builder.AddWarning($"Status '{row.id}' 解析 moveSpeedMultiplier 失败: {ex.Message}，使用默认值 1");
                        }
                    }
                }

                result.Add(def);
            }

            return result;
        }

        public override List<T> GetAllEntries<T>()
        {
            if (typeof(T) == typeof(StatusDefinition))
            {
                var list = new List<T>(_definitions.Count);
                for (int i = 0; i < _definitions.Count; i++)
                {
                    list.Add(_definitions[i] as T);
                }
                return list;
            }

            return new List<T>();
        }

        public override T GetEntryById<T>(string id)
        {
            if (typeof(T) == typeof(StatusDefinition))
            {
                return _definitionsById.TryGetValue(id, out var def) ? def as T : null;
            }

            return null;
        }

        protected override void OnReset()
        {
            _rows.Clear();
            _definitions.Clear();
            _definitionsById.Clear();
        }

        private void RebuildDefinitionCache()
        {
            _definitions.Clear();
            _definitionsById.Clear();

            if (_rows == null)
            {
                return;
            }

            foreach (var row in _rows)
            {
                var def = new StatusDefinition
                {
                    id = row.id ?? "",
                    displayName = string.IsNullOrWhiteSpace(row.displayName) ? (row.id ?? "") : row.displayName,
                    defaultDuration = Mathf.Max(0f, row.defaultDuration),
                    stackRule = (StatusStackRule)Mathf.Clamp(row.stackRule, 0, 3),
                    maxStacks = Mathf.Max(1, row.maxStacks),
                    modifiers = StatusModifiers.Default
                };

                if (!string.IsNullOrWhiteSpace(row.modifiersJson))
                {
                    var parsed = CastleDbJsonUtil.TryParseJsonObject(row.modifiersJson);
                    if (parsed != null && parsed.TryGetValue("moveSpeedMultiplier", out object value) && value != null)
                    {
                        try
                        {
                            def.modifiers = new StatusModifiers(moveSpeedMultiplier: Mathf.Max(0f, Convert.ToSingle(value)));
                        }
                        catch
                        {
                            // Best effort: ignore and keep default modifiers.
                        }
                    }
                }

                _definitions.Add(def);
                if (!string.IsNullOrWhiteSpace(def.id) && !_definitionsById.ContainsKey(def.id))
                {
                    _definitionsById.Add(def.id, def);
                }
            }
        }

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
    }
}
