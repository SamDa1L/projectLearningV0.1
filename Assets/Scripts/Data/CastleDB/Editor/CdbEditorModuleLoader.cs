using System;
using System.Collections.Generic;
using System.IO;
using CastleDB.Runtime;
using UnityEditor;
using UnityEngine;

namespace CastleDB.Editor
{
    public static class CdbEditorModuleLoader
    {
        public static bool TryLoadModuleByProviderId(
            string providerId,
            out TextAsset asset,
            out CdbModuleDescriptor descriptor,
            out string error)
        {
            asset = null;
            descriptor = null;
            error = null;

            if (string.IsNullOrWhiteSpace(providerId))
            {
                error = "providerId 不能为空";
                return false;
            }

            var scanRoot = CdbDataProviderRegistry.DefaultScanPath;
            if (!Directory.Exists(scanRoot))
            {
                error = $"扫描目录不存在：{scanRoot}";
                return false;
            }

            var cdbFiles = Directory.GetFiles(scanRoot, "*.cdb", SearchOption.AllDirectories);
            foreach (var filePath in cdbFiles)
            {
                var normalizedPath = filePath.Replace("\\", "/");
                var textAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(normalizedPath);
                if (textAsset == null)
                {
                    continue;
                }

                var source = new CastleDbJsonSource(textAsset);
                var root = source.ReadCastleDbJson();
                if (root == null)
                {
                    continue;
                }

                var metaSheet = root.sheets?.Find(s => s.name == "Meta");
                if (metaSheet?.lines == null)
                {
                    continue;
                }

                var metaEntries = new List<MetaEntry>();
                foreach (var line in metaSheet.lines)
                {
                    if (line is Dictionary<string, object> dict)
                    {
                        metaEntries.Add(new MetaEntry
                        {
                            key = dict.TryGetValue("key", out var keyObj) ? keyObj?.ToString() ?? "" : "",
                            value = dict.TryGetValue("value", out var valueObj) ? valueObj?.ToString() ?? "" : ""
                        });
                    }
                }

                var parsedDescriptor = CdbModuleDescriptor.FromMetaEntries(metaEntries, normalizedPath);
                if (parsedDescriptor == null)
                {
                    continue;
                }

                if (!string.Equals(parsedDescriptor.ProviderId, providerId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(parsedDescriptor.ResourcePath))
                {
                    error = $"模块 '{providerId}' 的 Meta.resourcePath 为空：{normalizedPath}";
                    return false;
                }

                if (parsedDescriptor.SchemaVersion != CdbDataProviderRegistry.ExpectedSchemaVersion)
                {
                    error =
                        $"Schema 版本不匹配：providerId='{providerId}', 期望={CdbDataProviderRegistry.ExpectedSchemaVersion}, 实际={parsedDescriptor.SchemaVersion} (file={normalizedPath})";
                    return false;
                }

                var loadedByResources = Resources.Load<TextAsset>(parsedDescriptor.ResourcePath);
                if (loadedByResources == null)
                {
                    error =
                        $"无法通过 Resources.Load 加载模块：providerId='{providerId}', resourcePath='{parsedDescriptor.ResourcePath}' (file={normalizedPath})";
                    return false;
                }

                asset = loadedByResources;
                descriptor = parsedDescriptor;
                return true;
            }

            error = $"未在 {scanRoot} 下找到 providerId='{providerId}' 的 .cdb 模块";
            return false;
        }

        public static bool TryCreateServiceByProviderId(string providerId, out CastleDbService service, out string error)
        {
            service = null;

            if (!TryLoadModuleByProviderId(providerId, out var asset, out var descriptor, out error))
            {
                return false;
            }

            try
            {
                var source = new CastleDbJsonSource(asset);
                var newService = new CastleDbService();
                newService.Initialize(source);

                var versionInfo = newService.GetVersionInfo();
                if (versionInfo == null || versionInfo.schemaVersion != CdbDataProviderRegistry.ExpectedSchemaVersion)
                {
                    error =
                        $"初始化 CastleDbService 失败：providerId='{providerId}', 期望={CdbDataProviderRegistry.ExpectedSchemaVersion}, 实际={versionInfo?.schemaVersion ?? "null"} (resourcePath={descriptor.ResourcePath})";
                    return false;
                }

                service = newService;
                return true;
            }
            catch (Exception ex)
            {
                error = $"初始化 CastleDbService 异常：providerId='{providerId}': {ex.Message}";
                return false;
            }
        }
    }
}

