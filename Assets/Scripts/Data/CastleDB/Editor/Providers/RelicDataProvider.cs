using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using CastleDB.Runtime;

namespace CastleDB.Editor.Providers
{
    /// <summary>
    /// 遗物数据提供者（0.5 阶段7）
    /// 处理 Relic.cdb 中的 Relic 表，导入生成 RelicCatalog 资产。
    /// </summary>
    public class RelicDataProvider : CdbDataProviderBase
    {
        public override string ExpectedProviderId => "Relic";

        private const string RELIC_CATALOG_PATH = "Assets/Resources/Config/RelicCatalog.asset";

        private readonly List<RelicDefinition> _relics = new List<RelicDefinition>();

        protected override void OnInitialize(CastleDbRoot root, CdbModuleDescriptor descriptor)
        {
            _relics.Clear();

            foreach (var sheet in root.sheets)
            {
                switch (sheet.name)
                {
                    case "Relic":
                        _relics.AddRange(ConvertLinesToRelicDefinitions(sheet.lines));
                        Debug.Log($"[RelicDataProvider] 解析 Relic 表：{_relics.Count} 条");
                        break;

                    case "Meta":
                        break;

                    default:
                        Debug.LogWarning($"[RelicDataProvider] 忽略未知 Sheet: {sheet.name}");
                        break;
                }
            }
        }

        private List<RelicDefinition> ConvertLinesToRelicDefinitions(List<object> lines)
        {
            var result = new List<RelicDefinition>();
            if (lines == null)
            {
                return result;
            }

            foreach (var line in lines)
            {
                if (line is Dictionary<string, object> dict)
                {
                    string kindStr = GetStringValue(dict, "kind");
                    result.Add(new RelicDefinition
                    {
                        id = GetStringValue(dict, "id"),
                        kind = ParseRelicKind(kindStr),
                        paramsJson = GetStringValue(dict, "paramsJson")
                    });
                }
            }

            return result;
        }

        protected override List<string> OnValidate(CdbModuleDescriptor descriptor)
        {
            var errors = new List<string>();

            // 1) id 唯一/非空
            var idSet = new HashSet<string>();
            foreach (var r in _relics)
            {
                if (r == null || string.IsNullOrWhiteSpace(r.id))
                {
                    errors.Add("Relic.id 不能为空");
                    continue;
                }

                if (!idSet.Add(r.id))
                {
                    errors.Add($"Relic.id 重复: '{r.id}'");
                }
            }

            // 2) kind 支持 + paramsJson 合法
            foreach (var r in _relics)
            {
                if (r == null || string.IsNullOrWhiteSpace(r.id))
                    continue;

                if ((int)r.kind < 0)
                {
                    errors.Add($"Relic '{r.id}' kind 不支持或为空（当前仅支持 Shield）");
                    continue;
                }

                // kind：目前仅支持 Shield
                if (r.kind != RelicKind.Shield)
                {
                    errors.Add($"Relic '{r.id}' kind 不支持: {r.kind}（当前仅支持 Shield）");
                    continue;
                }

                // paramsJson 必须是 JSON 对象
                var obj = CastleDbJsonUtil.TryParseJsonObject(r.paramsJson);
                if (obj == null)
                {
                    errors.Add($"Relic '{r.id}' paramsJson 必须是 JSON 对象: {r.paramsJson}");
                    continue;
                }

                // Shield 参数最小校验（0.5 最小闭环）
                if (!obj.TryGetValue("shieldMaxHp", out var shieldMaxHpObj))
                {
                    errors.Add($"Relic '{r.id}' 缺少 paramsJson.shieldMaxHp");
                    continue;
                }

                int shieldMaxHp = 0;
                try
                {
                    shieldMaxHp = Convert.ToInt32(shieldMaxHpObj);
                }
                catch
                {
                    errors.Add($"Relic '{r.id}' paramsJson.shieldMaxHp 必须是整数");
                    continue;
                }

                if (shieldMaxHp <= 0)
                {
                    errors.Add($"Relic '{r.id}' paramsJson.shieldMaxHp 必须 > 0（当前={shieldMaxHp}）");
                }

                // regenCooldown >= 0（可选）
                if (obj.TryGetValue("regenCooldown", out var regenCooldownObj))
                {
                    try
                    {
                        float regenCooldown = Convert.ToSingle(regenCooldownObj);
                        if (regenCooldown < 0f)
                        {
                            errors.Add($"Relic '{r.id}' paramsJson.regenCooldown 必须 >= 0（当前={regenCooldown}）");
                        }
                    }
                    catch
                    {
                        errors.Add($"Relic '{r.id}' paramsJson.regenCooldown 必须是数字");
                    }
                }
            }

            return errors;
        }

#if UNITY_EDITOR
        protected override CdbImportResult OnImport(CdbModuleDescriptor descriptor)
        {
            var builder = new CdbImportResultBuilder(ExpectedProviderId);

            try
            {
                RelicCatalog catalog = AssetDatabase.LoadAssetAtPath<RelicCatalog>(RELIC_CATALOG_PATH);
                bool created = false;

                if (catalog == null)
                {
                    catalog = ScriptableObject.CreateInstance<RelicCatalog>();
                    AssetDatabase.CreateAsset(catalog, RELIC_CATALOG_PATH);
                    created = true;
                }

                // 写入数据
                catalog.ApplyFromCastleDb(_relics.ToList());
                EditorUtility.SetDirty(catalog);

                if (created)
                {
                    builder.Created(RELIC_CATALOG_PATH);
                }
                else
                {
                    builder.Updated(RELIC_CATALOG_PATH);
                }

                builder.AddInfo($"RelicCatalog: {_relics.Count} 个遗物定义");
            }
            catch (Exception ex)
            {
                builder.AddError($"创建/更新 RelicCatalog 失败: {ex.Message}");
            }

            return builder.Build();
        }
#endif

        public override List<T> GetAllEntries<T>()
        {
            if (typeof(T) == typeof(RelicDefinition))
            {
                return _relics.Cast<T>().ToList();
            }

            return new List<T>();
        }

        public override T GetEntryById<T>(string id)
        {
            if (typeof(T) == typeof(RelicDefinition))
            {
                return _relics.FirstOrDefault(r => r.id == id) as T;
            }

            return null;
        }

        private static RelicKind ParseRelicKind(string kindStr)
        {
            return kindStr switch
            {
                "Shield" => RelicKind.Shield,
                _ => (RelicKind)(-1)
            };
        }

        private static string GetStringValue(Dictionary<string, object> dict, string key)
        {
            if (dict != null && dict.TryGetValue(key, out var value))
            {
                return value?.ToString() ?? "";
            }
            return "";
        }
    }
}
