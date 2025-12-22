using System;
using System.Collections.Generic;
using System.Linq;

namespace CastleDB.Runtime
{
    /// <summary>
    /// CastleDB 模块描述符（0.3 版本）
    /// 从 .cdb 文件的 Meta Sheet 解析生成
    ///
    /// 职责：
    /// - 存储模块元信息（providerId/dependencies/resourcePath）
    /// - 作为 Provider 初始化/校验/导入的唯一元信息入口
    /// - 由 Registry 统一管理和分发
    ///
    /// 设计原则：
    /// - 不可变对象（创建后不可修改）
    /// - 所有元信息从 Meta Sheet 读取，禁止 Provider 硬编码
    /// </summary>
    public sealed class CdbModuleDescriptor
    {
        /// <summary>
        /// 数据源唯一标识（来自 Meta.providerId）
        /// 必须与 Provider.ExpectedProviderId 完全一致
        /// </summary>
        public string ProviderId { get; }

        /// <summary>
        /// 显示名称（来自 Meta.displayName）
        /// 用于 UI 展示和日志
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        /// Resources 路径（来自 Meta.resourcePath）
        /// 格式：不含扩展名，例如 "Data/MonsterSystem"
        /// </summary>
        public string ResourcePath { get; }

        /// <summary>
        /// Schema 版本号（来自 Meta.schemaVersion）
        /// 用于版本兼容性检查
        /// </summary>
        public string SchemaVersion { get; }

        /// <summary>
        /// 依赖的其他数据源 ID 列表（来自 Meta.dependencies）
        /// 已去重并保留首次出现顺序
        /// </summary>
        public IReadOnlyList<string> Dependencies { get; }

        /// <summary>
        /// 默认/唯一可引用 Sheet（来自 Meta.defaultSheet，可选）
        /// 非空时允许使用 ProviderId/Id 格式的引用
        /// </summary>
        public string DefaultSheet { get; }

        /// <summary>
        /// 作者（来自 Meta.author，可选）
        /// </summary>
        public string Author { get; }

        /// <summary>
        /// 最后修改日期（来自 Meta.lastModified，可选）
        /// </summary>
        public string LastModified { get; }

        /// <summary>
        /// .cdb 文件的实际资产路径
        /// 格式：Assets/Resources/Data/MonsterSystem.cdb
        /// </summary>
        public string AssetPath { get; }

        /// <summary>
        /// 是否为 Legacy 模式（无 providerId 的旧格式文件）
        /// </summary>
        public bool IsLegacy { get; }

        /// <summary>
        /// 私有构造函数，使用 Builder 或静态方法创建
        /// </summary>
        private CdbModuleDescriptor(
            string providerId,
            string displayName,
            string resourcePath,
            string schemaVersion,
            IReadOnlyList<string> dependencies,
            string defaultSheet,
            string author,
            string lastModified,
            string assetPath,
            bool isLegacy)
        {
            ProviderId = providerId;
            DisplayName = displayName;
            ResourcePath = resourcePath;
            SchemaVersion = schemaVersion;
            Dependencies = dependencies ?? Array.Empty<string>();
            DefaultSheet = defaultSheet;
            Author = author;
            LastModified = lastModified;
            AssetPath = assetPath;
            IsLegacy = isLegacy;
        }

        /// <summary>
        /// 从 Meta 条目列表创建描述符
        /// </summary>
        /// <param name="metaEntries">Meta Sheet 的条目列表</param>
        /// <param name="assetPath">.cdb 文件的资产路径</param>
        /// <returns>模块描述符，若缺少 providerId 则 IsLegacy=true</returns>
        public static CdbModuleDescriptor FromMetaEntries(List<MetaEntry> metaEntries, string assetPath)
        {
            var metaDict = new Dictionary<string, string>();
            if (metaEntries != null)
            {
                foreach (var entry in metaEntries)
                {
                    if (!string.IsNullOrEmpty(entry.key))
                    {
                        metaDict[entry.key] = entry.value ?? "";
                    }
                }
            }

            string providerId = GetMetaValue(metaDict, "providerId", "");
            bool isLegacy = string.IsNullOrEmpty(providerId);

            // 解析 dependencies（逗号分隔，Trim，去重）
            var dependencies = ParseDependencies(GetMetaValue(metaDict, "dependencies", ""));

            return new CdbModuleDescriptor(
                providerId: providerId,
                displayName: GetMetaValue(metaDict, "displayName", isLegacy ? "Legacy Module" : providerId),
                resourcePath: GetMetaValue(metaDict, "resourcePath", ""),
                schemaVersion: GetMetaValue(metaDict, "schemaVersion", "unknown"),
                dependencies: dependencies,
                defaultSheet: GetMetaValue(metaDict, "defaultSheet", ""),
                author: GetMetaValue(metaDict, "author", ""),
                lastModified: GetMetaValue(metaDict, "lastModified", ""),
                assetPath: assetPath,
                isLegacy: isLegacy
            );
        }

        /// <summary>
        /// 创建 Legacy 模式描述符（用于兼容 0.2 版本的单文件）
        /// </summary>
        public static CdbModuleDescriptor CreateLegacy(string assetPath, string schemaVersion = "0.2")
        {
            return new CdbModuleDescriptor(
                providerId: "",
                displayName: "Legacy Module",
                resourcePath: ExtractResourcePath(assetPath),
                schemaVersion: schemaVersion,
                dependencies: Array.Empty<string>(),
                defaultSheet: "",
                author: "",
                lastModified: "",
                assetPath: assetPath,
                isLegacy: true
            );
        }

        /// <summary>
        /// 解析 dependencies 字符串
        /// </summary>
        private static List<string> ParseDependencies(string dependenciesStr)
        {
            if (string.IsNullOrWhiteSpace(dependenciesStr))
            {
                return new List<string>();
            }

            var result = new List<string>();
            var seen = new HashSet<string>();

            foreach (var dep in dependenciesStr.Split(','))
            {
                var trimmed = dep.Trim();
                if (!string.IsNullOrEmpty(trimmed) && !seen.Contains(trimmed))
                {
                    seen.Add(trimmed);
                    result.Add(trimmed);
                }
            }

            return result;
        }

        /// <summary>
        /// 从资产路径提取 Resources 路径
        /// Assets/Resources/Data/MonsterSystem.cdb → Data/MonsterSystem
        /// </summary>
        private static string ExtractResourcePath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return "";
            }

            const string resourcesPrefix = "Assets/Resources/";
            if (assetPath.StartsWith(resourcesPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var relativePath = assetPath.Substring(resourcesPrefix.Length);
                // 移除扩展名
                var lastDot = relativePath.LastIndexOf('.');
                if (lastDot > 0)
                {
                    return relativePath.Substring(0, lastDot);
                }
                return relativePath;
            }

            return assetPath;
        }

        /// <summary>
        /// 获取 Meta 值
        /// </summary>
        private static string GetMetaValue(Dictionary<string, string> metaDict, string key, string defaultValue)
        {
            return metaDict.TryGetValue(key, out var value) ? value : defaultValue;
        }

        /// <summary>
        /// 校验描述符的必需字段
        /// </summary>
        /// <returns>错误列表，空列表表示通过</returns>
        public List<string> ValidateRequired()
        {
            var errors = new List<string>();

            if (IsLegacy)
            {
                // Legacy 模式跳过大部分校验
                return errors;
            }

            if (string.IsNullOrEmpty(ProviderId))
            {
                errors.Add("Meta.providerId 不能为空");
            }

            if (string.IsNullOrEmpty(SchemaVersion))
            {
                errors.Add("Meta.schemaVersion 不能为空");
            }

            if (string.IsNullOrEmpty(ResourcePath))
            {
                errors.Add("Meta.resourcePath 不能为空");
            }

            if (string.IsNullOrEmpty(DisplayName))
            {
                errors.Add("Meta.displayName 不能为空");
            }

            return errors;
        }

        /// <summary>
        /// 校验 resourcePath 与实际资产路径是否一致
        /// </summary>
        /// <returns>错误消息，若一致返回 null</returns>
        public string ValidateResourcePath()
        {
            if (IsLegacy || string.IsNullOrEmpty(ResourcePath))
            {
                return null;
            }

            var expectedResourcePath = ExtractResourcePath(AssetPath);
            if (!string.Equals(ResourcePath, expectedResourcePath, StringComparison.Ordinal))
            {
                return $"Meta.resourcePath '{ResourcePath}' 与实际路径 '{expectedResourcePath}' 不一致";
            }

            return null;
        }

        public override string ToString()
        {
            if (IsLegacy)
            {
                return $"CdbModuleDescriptor[Legacy, path={AssetPath}]";
            }
            return $"CdbModuleDescriptor[{ProviderId}, version={SchemaVersion}, deps=[{string.Join(",", Dependencies)}]]";
        }
    }
}
