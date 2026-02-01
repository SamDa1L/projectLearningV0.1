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

                    // 初始化
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
}
}
