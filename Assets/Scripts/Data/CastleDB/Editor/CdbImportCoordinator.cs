using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using CastleDB.Runtime;

namespace CastleDB.Editor
{
    /// <summary>
    /// CastleDB 导入协调器（0.4 版本 Phase 4/5）
    /// 负责多文件扫描、拓扑排序、Provider 初始化与导入
    ///
    /// 职责：
    /// - 扫描 Assets/Resources/Data/**/*.cdb 文件
    /// - 解析 Meta Sheet 生成 CdbModuleDescriptor
    /// - 按依赖关系拓扑排序
    /// - 依次调用 Provider 的 Initialize → Validate → Import
    /// - 统一备份与保存
    ///
    /// 使用场景：
    /// - Import All（扫描所有模块）
    /// - 右键导入（单文件 + 依赖链）
    /// </summary>
    public class CdbImportCoordinator
    {
        private const string DEFAULT_SCAN_PATH = "Assets/Resources/Data";
        private const string LEGACY_EXCLUDE_PATH = "Assets/Resources/Data/CastleDbDemo";
        private const string SETTINGS_PATH = "Assets/Settings/CdbImportSettings.asset";

        /// <summary>
        /// CDB 导入根目录（相对 ProjectRoot 或绝对路径）
        /// Phase 12: Excel 导出以此为基准计算镜像路径，从 CdbImportSettings 加载
        /// </summary>
        private string cdbImportRoot = "Assets/Resources";

        private readonly CdbDataProviderRegistry _registry;
        private readonly List<string> _logMessages = new List<string>();

        public CdbImportCoordinator(CdbDataProviderRegistry registry)
        {
            _registry = registry ?? CdbDataProviderRegistry.Instance;

            // 加载 CdbImportSettings（若无则使用默认值）
            LoadImportSettings();
        }

        /// <summary>
        /// 加载导入设置
        /// </summary>
        private void LoadImportSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<CdbImportSettings>(SETTINGS_PATH);
            if (settings != null && !string.IsNullOrWhiteSpace(settings.cdbImportRoot))
            {
                cdbImportRoot = settings.cdbImportRoot;
            }
            // 若无设置文件或字段为空，保持默认值 "Assets/Resources"
        }

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

            excludePaths = excludePaths ?? new[] { LEGACY_EXCLUDE_PATH };

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
                        if (descriptor.IsLegacy)
                        {
                            _logMessages.Add($"  跳过 Legacy 文件：{Path.GetFileName(filePath)}");
                            continue;
                        }

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
                Debug.Log($"[CdbImportCoordinator] 文件无 Meta Sheet（Legacy 格式）：{resourcePath}");
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

        #region Import All 流程

        /// <summary>
        /// Import All：扫描所有模块并导入
        /// </summary>
        public CdbImportAllResult ImportAll()
        {
            var startTime = DateTime.Now;
            _logMessages.Clear();
            _logMessages.Add("=== CastleDB Import All (0.4 多模块流程) ===");
            _logMessages.Add($"开始时间：{startTime:yyyy-MM-dd HH:mm:ss}");

            try
            {
                // 0. 清理注册表（允许重复执行）
                _logMessages.Add("\n【步骤 0/10】清理注册表");
                _registry.ClearDescriptors();
                _logMessages.Add("✓ 注册表已清理，允许重复导入");

                // 0.5. 校验 CdbImportRoot（Phase 12: 失败直接中止）
                _logMessages.Add("\n【步骤 0.5/10】校验 CdbImportRoot");
                string cdbImportRootFullPath = GetCdbImportRootFullPath();
                if (cdbImportRootFullPath == null)
                {
                    _logMessages.Add($"✗ CdbImportRoot 校验失败，中止 Import All");
                    _logMessages.Add($"   配置路径：{cdbImportRoot}");
                    _logMessages.Add($"   请通过 Tools → CastleDB → Settings 修正配置");
                    return CdbImportAllResult.Failure(_logMessages);
                }
                _logMessages.Add($"✓ CdbImportRoot 校验通过：{cdbImportRootFullPath}");

                // 1. 发现所有模块
                _logMessages.Add("\n【步骤 1/10】模块发现");
                var descriptors = DiscoverModules();
                if (descriptors.Count == 0)
                {
                    _logMessages.Add("✗ 未发现任何模块，中止导入");
                    return CdbImportAllResult.Failure(_logMessages);
                }

                // 2. 校验 Provider 注册
                _logMessages.Add("\n【步骤 2/7】校验 Provider 注册");
                var missingProviders = descriptors
                    .Where(d => !_registry.IsRegistered(d.ProviderId))
                    .Select(d => d.ProviderId)
                    .ToList();

                if (missingProviders.Count > 0)
                {
                    _logMessages.Add($"✗ 以下 Provider 未注册：{string.Join(", ", missingProviders)}");
                    _logMessages.Add("请在 CdbProviderBootstrap.RegisterDefaults() 中注册");
                    return CdbImportAllResult.Failure(_logMessages);
                }

                // 3. 校验 schemaVersion 一致性
                _logMessages.Add("\n【步骤 3/7】校验 schemaVersion 一致性");
                var versionMismatches = new List<string>();
                foreach (var descriptor in descriptors)
                {
                    if (descriptor.SchemaVersion != CdbDataProviderRegistry.ExpectedSchemaVersion)
                    {
                        versionMismatches.Add($"{descriptor.ProviderId}: 期望 {CdbDataProviderRegistry.ExpectedSchemaVersion}，实际 {descriptor.SchemaVersion}");
                    }
                }

                if (versionMismatches.Count > 0)
                {
                    _logMessages.Add($"✗ schemaVersion 不一致：");
                    foreach (var mismatch in versionMismatches)
                    {
                        _logMessages.Add($"  • {mismatch}");
                    }
                    _logMessages.Add("所有模块必须使用相同的 schemaVersion");
                    return CdbImportAllResult.Failure(_logMessages);
                }
                _logMessages.Add($"✓ 所有模块 schemaVersion = {CdbDataProviderRegistry.ExpectedSchemaVersion}");

                // 4. 校验 resourcePath 一致性
                _logMessages.Add("\n【步骤 4/7】校验 resourcePath 一致性");
                var pathMismatches = new List<string>();
                foreach (var descriptor in descriptors)
                {
                    // 从文件路径推导期望的 resourcePath
                    var filePath = (descriptor.AssetPath ?? "").Replace("\\", "/");
                    if (!filePath.StartsWith("Assets/Resources/"))
                    {
                        pathMismatches.Add($"{descriptor.ProviderId}: 文件不在 Resources 目录");
                        continue;
                    }

                    var expectedResourcePath = filePath
                        .Replace("Assets/Resources/", "")
                        .Replace(".cdb", "");

                    if (descriptor.ResourcePath != expectedResourcePath)
                    {
                        pathMismatches.Add($"{descriptor.ProviderId}: Meta.resourcePath='{descriptor.ResourcePath}', 期望='{expectedResourcePath}'");
                    }
                }

                if (pathMismatches.Count > 0)
                {
                    _logMessages.Add($"✗ resourcePath 不一致：");
                    foreach (var mismatch in pathMismatches)
                    {
                        _logMessages.Add($"  • {mismatch}");
                    }
                    _logMessages.Add("Meta.resourcePath 必须与文件实际路径一致");
                    return CdbImportAllResult.Failure(_logMessages);
                }
                _logMessages.Add($"✓ 所有模块 resourcePath 一致");

                // 5. 校验依赖完整性
                _logMessages.Add("\n【步骤 5/7】校验依赖完整性");
                var missingDeps = new Dictionary<string, List<string>>();
                foreach (var descriptor in descriptors)
                {
                    foreach (var dep in descriptor.Dependencies)
                    {
                        if (!descriptors.Any(d => d.ProviderId == dep))
                        {
                            if (!missingDeps.ContainsKey(descriptor.ProviderId))
                            {
                                missingDeps[descriptor.ProviderId] = new List<string>();
                            }
                            missingDeps[descriptor.ProviderId].Add(dep);
                        }
                    }
                }

                if (missingDeps.Count > 0)
                {
                    _logMessages.Add($"✗ 依赖缺失：");
                    foreach (var kvp in missingDeps)
                    {
                        _logMessages.Add($"  • {kvp.Key} 依赖 {string.Join(", ", kvp.Value)} (未找到)");
                    }
                    return CdbImportAllResult.Failure(_logMessages);
                }
                _logMessages.Add($"✓ 所有依赖完整");

                // 6. 拓扑排序
                _logMessages.Add("\n【步骤 6/9】拓扑排序");
                var sortResult = _registry.TopologicalSort(descriptors.Select(d => d.ProviderId));
                if (!sortResult.IsSuccess)
                {
                    _logMessages.Add($"✗ 检测到循环依赖：{sortResult.GetCycleDescription()}");
                    return CdbImportAllResult.Failure(_logMessages);
                }

                var sortedIds = sortResult.SortedIds.ToList();
                _logMessages.Add($"导入顺序：{string.Join(" → ", sortedIds)}");

                // 7. 全量初始化
                _logMessages.Add("\n【步骤 7/9】全量初始化");
                foreach (var providerId in sortedIds)
                {
                    var descriptor = _registry.GetDescriptor(providerId);
                    var provider = _registry.GetProvider(providerId);

                    _logMessages.Add($"  初始化：{providerId}");

                    // 加载数据源
                    var resourcePath = descriptor.ResourcePath;
                    var asset = Resources.Load<TextAsset>(resourcePath);
                    if (asset == null)
                    {
                        _logMessages.Add($"  ✗ 无法加载资源：{resourcePath}");
                        return CdbImportAllResult.Failure(_logMessages);
                    }

                    var source = new CastleDbJsonSource(asset);

                    // Initialize
                    try
                    {
                        provider.Initialize(source, descriptor);
                        _logMessages.Add($"  ✓ {providerId} 初始化成功");
                    }
                    catch (Exception ex)
                    {
                        _logMessages.Add($"  ✗ {providerId} 初始化失败：{ex.Message}");
                        return CdbImportAllResult.Failure(_logMessages);
                    }
                }

                // 8. 全量校验（所有模块初始化后再统一校验）
                _logMessages.Add("\n【步骤 8/9】全量校验");
                foreach (var providerId in sortedIds)
                {
                    var descriptor = _registry.GetDescriptor(providerId);
                    var provider = _registry.GetProvider(providerId);

                    _logMessages.Add($"  校验：{providerId}");

                    var errors = provider.Validate(descriptor);
                    if (errors.Count > 0)
                    {
                        _logMessages.Add($"  ✗ {providerId} 校验失败（{errors.Count} 个错误）：");
                        foreach (var error in errors.Take(5))
                        {
                            _logMessages.Add($"    • {error}");
                        }
                        if (errors.Count > 5)
                        {
                            _logMessages.Add($"    ... 以及 {errors.Count - 5} 个其他错误");
                        }
                        return CdbImportAllResult.Failure(_logMessages);
                    }

                    _logMessages.Add($"  ✓ {providerId} 校验通过");
                }

                // 9. 统一备份 → 导入 → 收集 DirtyAssets → 统一保存
                _logMessages.Add("\n【步骤 9/9】备份、导入与保存");

                // 9.1 集中备份
                _logMessages.Add("  备份当前资产...");
                string backupTimestamp = null;
                try
                {
                    backupTimestamp = BackupExistingAssets();
                    if (backupTimestamp != null)
                    {
                        _logMessages.Add($"  ✓ 备份完成：Backup_{backupTimestamp}");
                    }
                    else
                    {
                        _logMessages.Add("  ⚠️ 无资产需要备份");
                    }
                }
                catch (Exception ex)
                {
                    _logMessages.Add($"  ⚠️ 备份失败（继续导入，失败将无法回滚）：{ex.Message}");
                }

                // 9.2 按拓扑顺序导入
                _logMessages.Add("  开始导入...");
                var importResults = new List<CdbImportResult>();
                var allDirtyAssets = new HashSet<string>();

                foreach (var providerId in sortedIds)
                {
                    var descriptor = _registry.GetDescriptor(providerId);
                    var provider = _registry.GetProvider(providerId);

                    _logMessages.Add($"    导入：{providerId}");

                    try
                    {
                        var result = provider.Import(descriptor);
                        importResults.Add(result);

                        if (!result.Success)
                        {
                            _logMessages.Add($"    ✗ {providerId} 导入失败");
                            foreach (var error in result.Errors.Take(3))
                            {
                                _logMessages.Add($"      • {error}");
                            }
                            _logMessages.Add($"  ✗ 导入失败，开始回滚...");

                            // 回滚备份
                            if (backupTimestamp != null)
                            {
                                try
                                {
                                    RestoreFromBackup(backupTimestamp);
                                    _logMessages.Add($"  ✓ 已从备份恢复：Backup_{backupTimestamp}");
                                }
                                catch (Exception rollbackEx)
                                {
                                    _logMessages.Add($"  ✗ 回滚失败：{rollbackEx.Message}");
                                }
                            }
                            else
                            {
                                _logMessages.Add($"  ⚠️ 无备份可回滚，部分变更已写入");
                            }

                            return CdbImportAllResult.Failure(_logMessages);
                        }

                        // 收集 DirtyAssets
                        foreach (var asset in result.DirtyAssets)
                        {
                            allDirtyAssets.Add(asset);
                        }

                        _logMessages.Add($"    ✓ {providerId} 完成（创建：{result.CreatedCount}，更新：{result.UpdatedCount}）");
                    }
                    catch (Exception ex)
                    {
                        _logMessages.Add($"    ✗ {providerId} 导入异常：{ex.Message}");
                        _logMessages.Add($"  ✗ 导入失败，开始回滚...");

                        // 回滚备份
                        if (backupTimestamp != null)
                        {
                            try
                            {
                                RestoreFromBackup(backupTimestamp);
                                _logMessages.Add($"  ✓ 已从备份恢复：Backup_{backupTimestamp}");
                            }
                            catch (Exception rollbackEx)
                            {
                                _logMessages.Add($"  ✗ 回滚失败：{rollbackEx.Message}");
                            }
                        }
                        else
                        {
                            _logMessages.Add($"  ⚠️ 无备份可回滚，部分变更已写入");
                        }

                        return CdbImportAllResult.Failure(_logMessages);
                    }
                }

                // 9.3 统一保存所有 DirtyAssets
                _logMessages.Add($"  保存资产（{allDirtyAssets.Count} 个）...");
                AssetDatabase.SaveAssets();
                _logMessages.Add("  ✓ 所有变更已保存");

                // 9.4 导出到 Excel（CSV 格式）
                _logMessages.Add($"\n【步骤 10/10】导出 Excel");
                var successIds = new HashSet<string>(
                    importResults.Where(r => r.Success).Select(r => r.ProviderId)
                );
                var successModules = sortedIds
                    .Where(id => successIds.Contains(id))
                    .Select(id => _registry.GetDescriptor(id))
                    .Where(desc => desc != null)
                    .ToList();
                ExportModulesToExcel(successModules);

                var elapsed = DateTime.Now - startTime;
                _logMessages.Add($"\n✓ Import All 完成，耗时：{elapsed.TotalSeconds:F2}s");

                return CdbImportAllResult.Success(_logMessages, importResults);
            }
            catch (Exception ex)
            {
                _logMessages.Add($"\n✗ Import All 异常：{ex.Message}");
                _logMessages.Add($"堆栈：{ex.StackTrace}");
                return CdbImportAllResult.Failure(_logMessages);
            }
        }

        #endregion

        #region 单模块导入（供右键导入使用）

        /// <summary>
        /// 依赖导入模式
        /// </summary>
        public enum DependencyImportMode
        {
            /// <summary>
            /// 仅初始化依赖链：依赖链执行 Initialize + Validate，目标文件执行完整导入
            /// </summary>
            InitializeOnly,

            /// <summary>
            /// 递归导入依赖链：依赖链和目标文件都按拓扑顺序执行完整导入
            /// </summary>
            RecursiveImport,

            /// <summary>
            /// 直接导入：仅导入目标文件（无依赖时使用）
            /// </summary>
            DirectImport
        }

        /// <summary>
        /// 导入单个模块（供右键导入使用）
        /// 实现与 ImportAll 相同的原子流程：依赖收集 → 初始化 → 校验 → 备份 → 导入 → 统一保存
        /// </summary>
        /// <param name="targetDescriptor">目标模块描述符</param>
        /// <param name="mode">导入模式</param>
        /// <returns>导入结果</returns>
        public CdbImportResult ImportSingleModule(CdbModuleDescriptor targetDescriptor, DependencyImportMode mode)
        {
            var startTime = DateTime.Now;
            _logMessages.Clear();
            _logMessages.Add($"=== CastleDB 单模块导入: {targetDescriptor.ProviderId} ===");
            _logMessages.Add($"模式：{mode}");
            _logMessages.Add($"开始时间：{startTime:yyyy-MM-dd HH:mm:ss}");

            try
            {
                // 0. 清理注册表（允许重复执行）
                _logMessages.Add("\n【步骤 0/11】清理注册表");
                _registry.ClearDescriptors();
                _logMessages.Add("✓ 注册表已清理，允许重复导入");

                // 0.5. 校验 CdbImportRoot（Phase 12: 失败直接中止）
                _logMessages.Add("\n【步骤 0.5/11】校验 CdbImportRoot");
                string cdbImportRootFullPath = GetCdbImportRootFullPath();
                if (cdbImportRootFullPath == null)
                {
                    _logMessages.Add($"✗ CdbImportRoot 校验失败，中止导入");
                    _logMessages.Add($"   配置路径：{cdbImportRoot}");
                    _logMessages.Add($"   请通过 Tools → CastleDB → Settings 修正配置");
                    return CdbImportResult.FailedWithLogs(targetDescriptor.ProviderId, _logMessages);
                }
                _logMessages.Add($"✓ CdbImportRoot 校验通过：{cdbImportRootFullPath}");

                // 1. 注册目标描述符
                _logMessages.Add("\n【步骤 1/11】注册目标模块");
                try
                {
                    _registry.RegisterDescriptor(targetDescriptor);
                    _logMessages.Add($"✓ 已注册：{targetDescriptor.ProviderId}");
                }
                catch (InvalidOperationException ex)
                {
                    _logMessages.Add($"✗ 注册失败（重复 providerId）：{ex.Message}");
                    return CdbImportResult.FailedWithLogs(targetDescriptor.ProviderId, _logMessages);
                }

                // 2. 收集依赖链（包含扫描和加载依赖 .cdb 文件）
                _logMessages.Add("\n【步骤 2/9】收集依赖链");
                var allModuleIds = new List<string>();

                if (targetDescriptor.Dependencies.Count > 0)
                {
                    _logMessages.Add($"目标模块依赖：{string.Join(", ", targetDescriptor.Dependencies)}");

                    // 扫描并加载依赖 .cdb 文件
                    foreach (var depId in targetDescriptor.Dependencies)
                    {
                        if (_registry.GetDescriptor(depId) == null)
                        {
                            _logMessages.Add($"  扫描依赖：{depId}");
                            var depDescriptor = FindAndLoadDependencyModule(depId);

                            if (depDescriptor == null)
                            {
                                _logMessages.Add($"  ✗ 未找到依赖模块：{depId}");
                                return CdbImportResult.FailedWithLogs(targetDescriptor.ProviderId, _logMessages);
                            }

                            try
                            {
                                _registry.RegisterDescriptor(depDescriptor);
                                _logMessages.Add($"  ✓ 已注册依赖：{depId}");
                            }
                            catch (InvalidOperationException ex)
                            {
                                _logMessages.Add($"  ✗ 注册依赖失败：{ex.Message}");
                                return CdbImportResult.FailedWithLogs(targetDescriptor.ProviderId, _logMessages);
                            }
                        }

                        allModuleIds.Add(depId);
                    }
                }

                // 根据模式决定是否包含目标模块
                if (mode == DependencyImportMode.RecursiveImport || mode == DependencyImportMode.DirectImport)
                {
                    allModuleIds.Add(targetDescriptor.ProviderId);
                }

                if (allModuleIds.Count == 0)
                {
                    _logMessages.Add("无模块需要处理");
                    return CdbImportResult.FailedWithLogs(targetDescriptor.ProviderId, _logMessages);
                }

                // 3. Meta 校验（schemaVersion/resourcePath 一致性）
                _logMessages.Add("\n【步骤 3/10】Meta 校验");

                // 3.1 schemaVersion 校验
                _logMessages.Add("  检查 schemaVersion 一致性...");
                var allDescriptors = new List<CdbModuleDescriptor> { targetDescriptor };
                foreach (var depId in targetDescriptor.Dependencies)
                {
                    var depDesc = _registry.GetDescriptor(depId);
                    if (depDesc != null)
                    {
                        allDescriptors.Add(depDesc);
                    }
                }

                foreach (var desc in allDescriptors)
                {
                    if (desc.SchemaVersion != CdbDataProviderRegistry.ExpectedSchemaVersion)
                    {
                        _logMessages.Add($"  ✗ {desc.ProviderId}: schemaVersion={desc.SchemaVersion}, 期望={CdbDataProviderRegistry.ExpectedSchemaVersion}");
                        return CdbImportResult.FailedWithLogs(targetDescriptor.ProviderId, _logMessages);
                    }
                }
                _logMessages.Add($"  ✓ 所有模块 schemaVersion = {CdbDataProviderRegistry.ExpectedSchemaVersion}");

                // 3.2 resourcePath 校验
                _logMessages.Add("  检查 resourcePath 一致性...");
                foreach (var desc in allDescriptors)
                {
                    var filePath = (desc.AssetPath ?? "").Replace("\\", "/");
                    if (!filePath.StartsWith("Assets/Resources/"))
                    {
                        _logMessages.Add($"  ✗ {desc.ProviderId}: 文件不在 Resources 目录");
                        return CdbImportResult.FailedWithLogs(targetDescriptor.ProviderId, _logMessages);
                    }

                    var expectedResourcePath = filePath
                        .Replace("Assets/Resources/", "")
                        .Replace(".cdb", "");

                    if (desc.ResourcePath != expectedResourcePath)
                    {
                        _logMessages.Add($"  ✗ {desc.ProviderId}: Meta.resourcePath='{desc.ResourcePath}', 期望='{expectedResourcePath}'");
                        return CdbImportResult.FailedWithLogs(targetDescriptor.ProviderId, _logMessages);
                    }
                }
                _logMessages.Add("  ✓ 所有模块 resourcePath 一致");

                // 4. 拓扑排序
                _logMessages.Add("\n【步骤 4/10】拓扑排序");
                var sortResult = _registry.TopologicalSort(allModuleIds);
                if (!sortResult.IsSuccess)
                {
                    _logMessages.Add($"✗ 检测到循环依赖：{sortResult.GetCycleDescription()}");
                    return CdbImportResult.FailedWithLogs(targetDescriptor.ProviderId, _logMessages);
                }

                var sortedIds = sortResult.SortedIds.ToList();
                _logMessages.Add($"处理顺序：{string.Join(" → ", sortedIds)}");

                // 4. 全量初始化
                _logMessages.Add("\n【步骤 5/10】全量初始化");
                foreach (var providerId in sortedIds)
                {
                    var descriptor = _registry.GetDescriptor(providerId);
                    var provider = _registry.GetProvider(providerId);

                    if (provider == null)
                    {
                        _logMessages.Add($"  ✗ 未找到 Provider：{providerId}");
                        return CdbImportResult.FailedWithLogs(targetDescriptor.ProviderId, _logMessages);
                    }

                    _logMessages.Add($"  初始化：{providerId}");

                    var asset = Resources.Load<TextAsset>(descriptor.ResourcePath);
                    if (asset == null)
                    {
                        _logMessages.Add($"  ✗ 无法加载资源：{descriptor.ResourcePath}");
                        return CdbImportResult.FailedWithLogs(targetDescriptor.ProviderId, _logMessages);
                    }

                    var source = new CastleDbJsonSource(asset);

                    try
                    {
                        provider.Initialize(source, descriptor);
                        _logMessages.Add($"  ✓ {providerId} 初始化成功");
                    }
                    catch (Exception ex)
                    {
                        _logMessages.Add($"  ✗ {providerId} 初始化失败：{ex.Message}");
                        return CdbImportResult.FailedWithLogs(targetDescriptor.ProviderId, _logMessages);
                    }
                }

                // 特殊处理 InitializeOnly 模式：需要初始化目标模块
                if (mode == DependencyImportMode.InitializeOnly)
                {
                    var targetProvider = _registry.GetProvider(targetDescriptor.ProviderId);
                    if (targetProvider == null)
                    {
                        _logMessages.Add($"  ✗ 未找到目标 Provider：{targetDescriptor.ProviderId}");
                        return CdbImportResult.FailedWithLogs(targetDescriptor.ProviderId, _logMessages);
                    }

                    _logMessages.Add($"  初始化目标：{targetDescriptor.ProviderId}");

                    var targetAsset = Resources.Load<TextAsset>(targetDescriptor.ResourcePath);
                    if (targetAsset == null)
                    {
                        _logMessages.Add($"  ✗ 无法加载目标资源：{targetDescriptor.ResourcePath}");
                        return CdbImportResult.FailedWithLogs(targetDescriptor.ProviderId, _logMessages);
                    }

                    var targetSource = new CastleDbJsonSource(targetAsset);

                    try
                    {
                        targetProvider.Initialize(targetSource, targetDescriptor);
                        _logMessages.Add($"  ✓ {targetDescriptor.ProviderId} 初始化成功");
                    }
                    catch (Exception ex)
                    {
                        _logMessages.Add($"  ✗ {targetDescriptor.ProviderId} 初始化失败：{ex.Message}");
                        return CdbImportResult.FailedWithLogs(targetDescriptor.ProviderId, _logMessages);
                    }
                }

                // 5. 全量校验
                _logMessages.Add("\n【步骤 6/10】全量校验");

                // 校验依赖链
                foreach (var providerId in sortedIds)
                {
                    var descriptor = _registry.GetDescriptor(providerId);
                    var provider = _registry.GetProvider(providerId);

                    _logMessages.Add($"  校验：{providerId}");

                    var errors = provider.Validate(descriptor);
                    if (errors.Count > 0)
                    {
                        _logMessages.Add($"  ✗ {providerId} 校验失败（{errors.Count} 个错误）：");
                        foreach (var error in errors.Take(5))
                        {
                            _logMessages.Add($"    • {error}");
                        }
                        if (errors.Count > 5)
                        {
                            _logMessages.Add($"    ... 以及 {errors.Count - 5} 个其他错误");
                        }
                        return CdbImportResult.FailedWithLogs(targetDescriptor.ProviderId, _logMessages);
                    }

                    _logMessages.Add($"  ✓ {providerId} 校验通过");
                }

                // InitializeOnly 模式：额外校验目标模块
                if (mode == DependencyImportMode.InitializeOnly)
                {
                    var targetProvider = _registry.GetProvider(targetDescriptor.ProviderId);
                    _logMessages.Add($"  校验目标：{targetDescriptor.ProviderId}");

                    var targetErrors = targetProvider.Validate(targetDescriptor);
                    if (targetErrors.Count > 0)
                    {
                        _logMessages.Add($"  ✗ 目标校验失败（{targetErrors.Count} 个错误）：");
                        foreach (var error in targetErrors.Take(5))
                        {
                            _logMessages.Add($"    • {error}");
                        }
                        return CdbImportResult.FailedWithLogs(targetDescriptor.ProviderId, _logMessages);
                    }

                    _logMessages.Add($"  ✓ {targetDescriptor.ProviderId} 校验通过");
                }

                // 7. 集中备份
                _logMessages.Add("\n【步骤 7/10】集中备份");
                string backupTimestamp = null;
                try
                {
                    backupTimestamp = BackupExistingAssets();
                    if (backupTimestamp != null)
                    {
                        _logMessages.Add($"  ✓ 备份完成：Backup_{backupTimestamp}");
                    }
                    else
                    {
                        _logMessages.Add("  ⚠️ 无资产需要备份");
                    }
                }
                catch (Exception ex)
                {
                    _logMessages.Add($"  ⚠️ 备份失败（继续导入，失败将无法回滚）：{ex.Message}");
                }

                // 7. 按模式导入
                _logMessages.Add($"\n【步骤 8/10】导入（模式：{mode}）");
                var allDirtyAssets = new HashSet<string>();
                var importResults = new List<CdbImportResult>();

                if (mode == DependencyImportMode.RecursiveImport)
                {
                    // 递归导入：依赖链和目标都导入
                    foreach (var providerId in sortedIds)
                    {
                        var descriptor = _registry.GetDescriptor(providerId);
                        var provider = _registry.GetProvider(providerId);

                        _logMessages.Add($"  导入：{providerId}");

                        try
                        {
                            var result = provider.Import(descriptor);
                            importResults.Add(result);

                            if (!result.Success)
                            {
                                _logMessages.Add($"  ✗ {providerId} 导入失败");
                                foreach (var error in result.Errors.Take(3))
                                {
                                    _logMessages.Add($"    • {error}");
                                }
                                _logMessages.Add($"  ✗ 导入失败，开始回滚...");

                                // 回滚备份
                                if (backupTimestamp != null)
                                {
                                    try
                                    {
                                        RestoreFromBackup(backupTimestamp);
                                        _logMessages.Add($"  ✓ 已从备份恢复：Backup_{backupTimestamp}");
                                    }
                                    catch (Exception rollbackEx)
                                    {
                                        _logMessages.Add($"  ✗ 回滚失败：{rollbackEx.Message}");
                                    }
                                }
                                else
                                {
                                    _logMessages.Add($"  ⚠️ 无备份可回滚，部分变更已写入");
                                }

                                return CdbImportResult.FailedWithLogs(targetDescriptor.ProviderId, _logMessages);
                            }

                            foreach (var asset in result.DirtyAssets)
                            {
                                allDirtyAssets.Add(asset);
                            }

                            _logMessages.Add($"  ✓ {providerId} 完成（创建：{result.CreatedCount}，更新：{result.UpdatedCount}）");
                        }
                        catch (Exception ex)
                        {
                            _logMessages.Add($"  ✗ {providerId} 导入异常：{ex.Message}");
                            _logMessages.Add($"  ✗ 导入失败，开始回滚...");

                            // 回滚备份
                            if (backupTimestamp != null)
                            {
                                try
                                {
                                    RestoreFromBackup(backupTimestamp);
                                    _logMessages.Add($"  ✓ 已从备份恢复：Backup_{backupTimestamp}");
                                }
                                catch (Exception rollbackEx)
                                {
                                    _logMessages.Add($"  ✗ 回滚失败：{rollbackEx.Message}");
                                }
                            }
                            else
                            {
                                _logMessages.Add($"  ⚠️ 无备份可回滚，部分变更已写入");
                            }

                            return CdbImportResult.FailedWithLogs(targetDescriptor.ProviderId, _logMessages);
                        }
                    }
                }
                else if (mode == DependencyImportMode.InitializeOnly || mode == DependencyImportMode.DirectImport)
                {
                    // 仅导入目标模块
                    var targetProvider = _registry.GetProvider(targetDescriptor.ProviderId);
                    _logMessages.Add($"  导入目标：{targetDescriptor.ProviderId}");

                    try
                    {
                        var result = targetProvider.Import(targetDescriptor);
                        importResults.Add(result);

                        if (!result.Success)
                        {
                            _logMessages.Add($"  ✗ 目标导入失败");
                            foreach (var error in result.Errors.Take(3))
                            {
                                _logMessages.Add($"    • {error}");
                            }
                            _logMessages.Add($"  ✗ 导入失败，开始回滚...");

                            // 回滚备份
                            if (backupTimestamp != null)
                            {
                                try
                                {
                                    RestoreFromBackup(backupTimestamp);
                                    _logMessages.Add($"  ✓ 已从备份恢复：Backup_{backupTimestamp}");
                                }
                                catch (Exception rollbackEx)
                                {
                                    _logMessages.Add($"  ✗ 回滚失败：{rollbackEx.Message}");
                                }
                            }
                            else
                            {
                                _logMessages.Add($"  ⚠️ 无备份可回滚，部分变更已写入");
                            }

                            return CdbImportResult.FailedWithLogs(targetDescriptor.ProviderId, _logMessages);
                        }

                        foreach (var asset in result.DirtyAssets)
                        {
                            allDirtyAssets.Add(asset);
                        }

                        _logMessages.Add($"  ✓ {targetDescriptor.ProviderId} 完成（创建：{result.CreatedCount}，更新：{result.UpdatedCount}）");
                    }
                    catch (Exception ex)
                    {
                        _logMessages.Add($"  ✗ 目标导入异常：{ex.Message}");
                        _logMessages.Add($"  ✗ 导入失败，开始回滚...");

                        // 回滚备份
                        if (backupTimestamp != null)
                        {
                            try
                            {
                                RestoreFromBackup(backupTimestamp);
                                _logMessages.Add($"  ✓ 已从备份恢复：Backup_{backupTimestamp}");
                            }
                            catch (Exception rollbackEx)
                            {
                                _logMessages.Add($"  ✗ 回滚失败：{rollbackEx.Message}");
                            }
                        }
                        else
                        {
                            _logMessages.Add($"  ⚠️ 无备份可回滚，部分变更已写入");
                        }

                        return CdbImportResult.FailedWithLogs(targetDescriptor.ProviderId, _logMessages);
                    }
                }

                // 8. 统一保存
                _logMessages.Add($"\n【步骤 9/10】统一保存");
                _logMessages.Add($"  保存资产（{allDirtyAssets.Count} 个）...");
                AssetDatabase.SaveAssets();
                _logMessages.Add("  ✓ 所有变更已保存");

                // 9. 导出到 Excel（仅目标 .cdb）
                _logMessages.Add($"\n【步骤 10/10】导出 Excel");
                ExportModulesToExcel(new List<CdbModuleDescriptor> { targetDescriptor }, TriggerMode.ThisFile);

                var elapsed = DateTime.Now - startTime;
                _logMessages.Add($"\n✓ 单模块导入完成，耗时：{elapsed.TotalSeconds:F2}s");

                // 返回合并后的导入结果
                return CdbImportResult.SucceededWithLogs(
                    targetDescriptor.ProviderId,
                    _logMessages,
                    importResults.Sum(r => r.CreatedCount),
                    importResults.Sum(r => r.UpdatedCount),
                    allDirtyAssets.ToList()
                );
            }
            catch (Exception ex)
            {
                _logMessages.Add($"\n✗ 单模块导入异常：{ex.Message}");
                _logMessages.Add($"堆栈：{ex.StackTrace}");
                return CdbImportResult.FailedWithLogs(targetDescriptor.ProviderId, _logMessages);
            }
        }

        /// <summary>
        /// 查找并加载依赖模块（扫描 .cdb 文件）
        /// </summary>
        private CdbModuleDescriptor FindAndLoadDependencyModule(string dependencyId)
        {
            // 扫描 Assets/Resources/Data/**/*.cdb
            var cdbFiles = Directory.GetFiles(DEFAULT_SCAN_PATH, "*.cdb", SearchOption.AllDirectories)
                .Where(f => !f.Replace("\\", "/").Contains(LEGACY_EXCLUDE_PATH.Replace("\\", "/")))
                .ToList();

            foreach (var filePath in cdbFiles)
            {
                try
                {
                    var descriptor = LoadModuleDescriptor(filePath);
                    if (descriptor != null && descriptor.ProviderId == dependencyId)
                    {
                        return descriptor;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[CdbImportCoordinator] 扫描依赖时解析文件失败：{filePath} - {ex.Message}");
                }
            }

            return null;
        }

        #endregion

        #region 备份管理

        private const string BACKUP_DIR = "Logs/CastleDBImport/Backups";
        private const string PROFILE_OUTPUT_DIR = "Assets/Resources/Profiles";
        private const string PLAYER_CONFIG_PATH = "Assets/Resources/Config/PlayerConfig.asset";
        private const string ABILITY_CATALOG_PATH = "Assets/Resources/Config/AbilityCatalog.asset";

        /// <summary>
        /// 备份现有资产（Profile/PlayerConfig/AbilityCatalog）
        /// </summary>
        /// <returns>备份时间戳（用于后续回滚），如果备份失败返回 null</returns>
        private string BackupExistingAssets()
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string backupPath = Path.Combine(BACKUP_DIR, $"Backup_{timestamp}");

            int backupCount = 0;

            // 1. 备份 EnemyTuningProfile 文件
            if (Directory.Exists(PROFILE_OUTPUT_DIR))
            {
                var profileFiles = Directory.GetFiles(PROFILE_OUTPUT_DIR, "Profile_*.asset");
                if (profileFiles.Length > 0)
                {
                    Directory.CreateDirectory(backupPath);
                    foreach (var file in profileFiles)
                    {
                        string fileName = Path.GetFileName(file);
                        string destPath = Path.Combine(backupPath, fileName);
                        File.Copy(file, destPath, true);
                        backupCount++;
                    }
                }
            }

            // 2. 备份 PlayerConfig 文件
            if (File.Exists(PLAYER_CONFIG_PATH))
            {
                if (!Directory.Exists(backupPath))
                {
                    Directory.CreateDirectory(backupPath);
                }

                string fileName = Path.GetFileName(PLAYER_CONFIG_PATH);
                string destPath = Path.Combine(backupPath, fileName);
                File.Copy(PLAYER_CONFIG_PATH, destPath, true);
                backupCount++;
            }

            // 3. 备份 AbilityCatalog 文件
            if (File.Exists(ABILITY_CATALOG_PATH))
            {
                if (!Directory.Exists(backupPath))
                {
                    Directory.CreateDirectory(backupPath);
                }

                string fileName = Path.GetFileName(ABILITY_CATALOG_PATH);
                string destPath = Path.Combine(backupPath, fileName);
                File.Copy(ABILITY_CATALOG_PATH, destPath, true);
                backupCount++;
            }

            if (backupCount > 0)
            {
                Debug.Log($"[CdbImportCoordinator] 备份完成: {backupPath} ({backupCount} 个文件)");
                return timestamp;
            }

            return null;
        }

        /// <summary>
        /// 从最新备份恢复资产（导入失败时调用）
        /// </summary>
        /// <param name="backupTimestamp">备份时间戳（可选，不提供则使用最新备份）</param>
        private void RestoreFromBackup(string backupTimestamp = null)
        {
            string backupPath;

            if (!string.IsNullOrEmpty(backupTimestamp))
            {
                backupPath = Path.Combine(BACKUP_DIR, $"Backup_{backupTimestamp}");
            }
            else
            {
                // 查找最新备份
                if (!Directory.Exists(BACKUP_DIR))
                {
                    Debug.LogWarning("[CdbImportCoordinator] 备份目录不存在，无法回滚");
                    return;
                }

                var backupDirs = Directory.GetDirectories(BACKUP_DIR, "Backup_*")
                    .OrderByDescending(d => d)
                    .ToList();

                if (backupDirs.Count == 0)
                {
                    Debug.LogWarning("[CdbImportCoordinator] 未找到备份，无法回滚");
                    return;
                }

                backupPath = backupDirs[0];
            }

            if (!Directory.Exists(backupPath))
            {
                Debug.LogWarning($"[CdbImportCoordinator] 备份路径不存在：{backupPath}");
                return;
            }

            Debug.Log($"[CdbImportCoordinator] 开始从备份恢复：{backupPath}");

            int restoredCount = 0;

            // 1. 恢复 EnemyTuningProfile 文件
            if (Directory.Exists(PROFILE_OUTPUT_DIR))
            {
                var currentProfileFiles = Directory.GetFiles(PROFILE_OUTPUT_DIR, "Profile_*.asset");
                foreach (var file in currentProfileFiles)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[CdbImportCoordinator] 删除当前文件失败：{file} - {ex.Message}");
                    }
                }
            }

            var backupProfileFiles = Directory.GetFiles(backupPath, "Profile_*.asset");
            foreach (var backupFile in backupProfileFiles)
            {
                try
                {
                    string fileName = Path.GetFileName(backupFile);
                    string destPath = Path.Combine(PROFILE_OUTPUT_DIR, fileName);
                    File.Copy(backupFile, destPath, true);
                    restoredCount++;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[CdbImportCoordinator] 恢复文件失败：{backupFile} - {ex.Message}");
                }
            }

            // 2. 恢复 PlayerConfig 文件
            string backupPlayerConfig = Path.Combine(backupPath, "PlayerConfig.asset");
            if (File.Exists(backupPlayerConfig))
            {
                try
                {
                    if (File.Exists(PLAYER_CONFIG_PATH))
                    {
                        File.Delete(PLAYER_CONFIG_PATH);
                    }
                    File.Copy(backupPlayerConfig, PLAYER_CONFIG_PATH, true);
                    restoredCount++;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[CdbImportCoordinator] 恢复 PlayerConfig 失败：{ex.Message}");
                }
            }

            // 3. 恢复 AbilityCatalog 文件
            string backupAbilityCatalog = Path.Combine(backupPath, "AbilityCatalog.asset");
            if (File.Exists(backupAbilityCatalog))
            {
                try
                {
                    if (File.Exists(ABILITY_CATALOG_PATH))
                    {
                        File.Delete(ABILITY_CATALOG_PATH);
                    }
                    File.Copy(backupAbilityCatalog, ABILITY_CATALOG_PATH, true);
                    restoredCount++;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[CdbImportCoordinator] 恢复 AbilityCatalog 失败：{ex.Message}");
                }
            }

            // 4. 刷新 AssetDatabase
            AssetDatabase.Refresh();

            Debug.Log($"[CdbImportCoordinator] 回滚完成：恢复了 {restoredCount} 个文件");
        }

        #endregion

        #region Excel 导出

        /// <summary>
        /// 获取 CdbImportRoot 完整路径（解析相对/绝对路径）
        /// </summary>
        /// <returns>完整路径，验证失败返回 null</returns>
        private string GetCdbImportRootFullPath()
        {
            string projectRoot = Directory.GetParent(UnityEngine.Application.dataPath).FullName;

            string path = Path.IsPathRooted(cdbImportRoot)
                ? cdbImportRoot
                : Path.Combine(projectRoot, cdbImportRoot);

            if (!ValidateCdbImportRoot(path))
            {
                return null; // 中止导出
            }

            return path;
        }

        /// <summary>
        /// 校验 CdbImportRoot 目录存在性与可访问性
        /// </summary>
        private bool ValidateCdbImportRoot(string fullRootPath)
        {
            if (!Directory.Exists(fullRootPath))
            {
                Debug.LogError($"[CdbImportCoordinator] CdbImportRoot 不存在：{fullRootPath}");
                return false;
            }

            try
            {
                // 轻量访问性探测
                Directory.EnumerateFileSystemEntries(fullRootPath).FirstOrDefault();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[CdbImportCoordinator] CdbImportRoot 不可访问：{fullRootPath} ({e.GetType().Name})");
                return false;
            }
        }

        /// <summary>
        /// 导出模块到 Excel（.xlsx 格式）
        /// </summary>
        /// <param name="modules">要导出的模块列表</param>
        private void ExportModulesToExcel(List<CdbModuleDescriptor> modules, TriggerMode triggerMode = TriggerMode.All)
        {
            if (modules == null || modules.Count == 0)
            {
                _logMessages.Add("  无模块需要导出，跳过");
                return;
            }

            // 获取并校验 CdbImportRoot
            string cdbImportRootFullPath = GetCdbImportRootFullPath();
            if (cdbImportRootFullPath == null)
            {
                _logMessages.Add("  ✗ CdbImportRoot 校验失败，跳过 Excel 导出");
                return;
            }

            var exporter = new CdbExcelExporter();
            int successCount = 0;
            int failureCount = 0;

            foreach (var module in modules)
            {
                try
                {
                    // 获取相对路径（相对于 ProjectRoot）
                    string relativePath = module.AssetPath;
                    if (Path.IsPathRooted(relativePath))
                    {
                        // 转换为相对路径
                        string projectRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
                        if (relativePath.StartsWith(projectRoot))
                        {
                            relativePath = relativePath.Substring(projectRoot.Length + 1).Replace("\\", "/");
                        }
                    }

                    bool success = exporter.ExportToExcel(relativePath, cdbImportRootFullPath, triggerMode);
                    if (success)
                    {
                        successCount++;
                        _logMessages.Add($"  ✓ {module.ProviderId}");
                    }
                    else
                    {
                        failureCount++;
                        _logMessages.Add($"  ✗ {module.ProviderId} 导出失败");
                    }
                }
                catch (Exception ex)
                {
                    failureCount++;
                    _logMessages.Add($"  ✗ {module.ProviderId} 异常：{ex.Message}");
                }
            }

            _logMessages.Add($"  导出完成：成功 {successCount}，失败 {failureCount}");
        }

        #endregion

        #region 日志访问

        /// <summary>
        /// 获取所有日志消息
        /// </summary>
        public IReadOnlyList<string> GetLogMessages()
        {
            return _logMessages.AsReadOnly();
        }

        #endregion
    }

    /// <summary>
    /// Import All 结果
    /// </summary>
    public class CdbImportAllResult
    {
        public bool IsSuccess { get; }
        public IReadOnlyList<string> LogMessages { get; }
        public IReadOnlyList<CdbImportResult> ProviderResults { get; }

        private CdbImportAllResult(bool success, List<string> logMessages, List<CdbImportResult> providerResults)
        {
            IsSuccess = success;
            LogMessages = logMessages ?? new List<string>();
            ProviderResults = providerResults ?? new List<CdbImportResult>();
        }

        public static CdbImportAllResult Success(List<string> logMessages, List<CdbImportResult> providerResults)
        {
            return new CdbImportAllResult(true, logMessages, providerResults);
        }

        public static CdbImportAllResult Failure(List<string> logMessages)
        {
            return new CdbImportAllResult(false, logMessages, null);
        }

        /// <summary>
        /// 获取格式化的日志文本
        /// </summary>
        public string GetFormattedLog()
        {
            return string.Join("\n", LogMessages);
        }
    }
}
