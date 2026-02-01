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
        #region 模块发现

        /// <summary>
        /// 发现所有 .cdb 文件并生成模块描述符
        /// </summary>
        /// <param name="scanPath">扫描根路径（默认 Assets/Resources/Data）</param>
        /// <param name="excludePaths">排除路径列表</param>
        /// <returns>发现的模块描述符列表</returns>
        public List<CdbModuleDescriptor> DiscoverModules(
            string scanPath = DEFAULT_SCAN_PATH,
            string[] excludePaths = null)
        {
            _logMessages.Clear();
            _logMessages.Add($"[DiscoverModules] 开始扫描：{scanPath}");

            excludePaths = excludePaths ?? Array.Empty<string>();

            // 递归查找所有 .cdb 文件
            var cdbFiles = Directory.GetFiles(scanPath, "*.cdb", SearchOption.AllDirectories)
                .Where(f => !excludePaths.Any(exclude => f.Replace("\\", "/").Contains(exclude.Replace("\\", "/"))))
                .ToList();

            _logMessages.Add($"[DiscoverModules] 找到 {cdbFiles.Count} 个 .cdb 文件");

            var descriptors = new List<CdbModuleDescriptor>();

            foreach (var filePath in cdbFiles)
            {
                try
                {
                    var descriptor = LoadModuleDescriptor(filePath);
                    if (descriptor != null)
                    {
                        descriptors.Add(descriptor);
                        _registry.RegisterDescriptor(descriptor);
                        _logMessages.Add($"  ✓ {descriptor.ProviderId}: {filePath}");
                    }
                    else
                    {
                        _logMessages.Add($"  ⚠ 跳过无效文件：{filePath}");
    }
}
                catch (Exception ex)
                {
                    _logMessages.Add($"  ✗ 解析失败：{filePath} - {ex.Message}");
                }
            }

            _logMessages.Add($"[DiscoverModules] 发现 {descriptors.Count} 个有效模块");
            return descriptors;
        }

        /// <summary>
        /// 加载单个 .cdb 文件的模块描述符
        /// </summary>
        private CdbModuleDescriptor LoadModuleDescriptor(string filePath)
        {
            // 将文件路径转换为 Resources 路径
            var normalizedPath = filePath.Replace("\\", "/");
            if (!normalizedPath.StartsWith("Assets/Resources/"))
            {
                Debug.LogWarning($"[CdbImportCoordinator] 文件不在 Resources 目录：{filePath}");
                return null;
            }

            var resourcePath = normalizedPath
                .Replace("Assets/Resources/", "")
                .Replace(".cdb", "");

            // 加载 TextAsset
            var asset = Resources.Load<TextAsset>(resourcePath);
            if (asset == null)
            {
                Debug.LogWarning($"[CdbImportCoordinator] 无法加载资源：{resourcePath}");
                return null;
            }

            // 解析 JSON
            var source = new CastleDbJsonSource(asset);
            var root = source.ReadCastleDbJson();
            if (root == null)
            {
                Debug.LogWarning($"[CdbImportCoordinator] 无法解析 JSON：{resourcePath}");
                return null;
            }

            // 查找 Meta Sheet
            var metaSheet = root.sheets.Find(s => s.name == "Meta");
            if (metaSheet == null || metaSheet.lines == null)
            {
                Debug.LogWarning($"[CdbImportCoordinator] 文件缺少 Meta Sheet：{resourcePath}");
                return null;
            }

            // 解析 Meta 条目
            var metaEntries = new List<MetaEntry>();
            foreach (var line in metaSheet.lines)
            {
                if (line is Dictionary<string, object> dict)
                {
                    metaEntries.Add(new MetaEntry
                    {
                        key = dict.TryGetValue("key", out var k) ? k?.ToString() ?? "" : "",
                        value = dict.TryGetValue("value", out var v) ? v?.ToString() ?? "" : ""
                    });
                }
            }

            return CdbModuleDescriptor.FromMetaEntries(metaEntries, filePath);
        }

        #endregion
}
}
