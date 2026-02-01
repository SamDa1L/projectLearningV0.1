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
}
}
