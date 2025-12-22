using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CastleDB.Runtime
{
    /// <summary>
    /// CastleDB 数据提供者注册表（0.3 版本）
    ///
    /// 职责：
    /// - Provider 注册与获取
    /// - 模块发现（扫描 .cdb 文件）
    /// - 依赖排序（拓扑排序）
    /// - 循环依赖检测
    /// - 全量/单个初始化
    ///
    /// 使用场景：
    /// - Import All：发现所有模块 → 拓扑排序 → 依次初始化
    /// - 单文件导入：获取依赖链 → 初始化依赖 → 初始化目标
    /// - 运行时查询：通过 providerId 获取 Provider
    /// </summary>
    public class CdbDataProviderRegistry
    {
        private static CdbDataProviderRegistry _instance;
        public static CdbDataProviderRegistry Instance => _instance ??= new CdbDataProviderRegistry();

        /// <summary>
        /// 已注册的 Provider 映射（providerId → Provider）
        /// </summary>
        private readonly Dictionary<string, ICdbDataProvider> _providers = new Dictionary<string, ICdbDataProvider>();

        /// <summary>
        /// 已发现的模块描述符映射（providerId → Descriptor）
        /// </summary>
        private readonly Dictionary<string, CdbModuleDescriptor> _descriptors = new Dictionary<string, CdbModuleDescriptor>();

        /// <summary>
        /// 期望的 schema 版本
        /// </summary>
        public const string ExpectedSchemaVersion = "0.3";

        /// <summary>
        /// 默认扫描路径
        /// </summary>
        public const string DefaultScanPath = "Assets/Resources/Data";

        /// <summary>
        /// Legacy 文件排除路径
        /// </summary>
        public const string LegacyExcludePath = "Assets/Resources/Data/CastleDbDemo";

        /// <summary>
        /// 注册 Provider
        /// </summary>
        /// <param name="provider">Provider 实例</param>
        /// <returns>是否注册成功</returns>
        public bool Register(ICdbDataProvider provider)
        {
            if (provider == null)
            {
                Debug.LogError("[CdbRegistry] 尝试注册 null Provider");
                return false;
            }

            var providerId = provider.ExpectedProviderId;
            if (string.IsNullOrEmpty(providerId))
            {
                Debug.LogError("[CdbRegistry] Provider.ExpectedProviderId 不能为空");
                return false;
            }

            if (_providers.ContainsKey(providerId))
            {
                Debug.LogWarning($"[CdbRegistry] Provider '{providerId}' 已注册，将被覆盖");
            }

            _providers[providerId] = provider;
            Debug.Log($"[CdbRegistry] 注册 Provider：{providerId}");
            return true;
        }

        /// <summary>
        /// 注销 Provider
        /// </summary>
        public bool Unregister(string providerId)
        {
            if (_providers.Remove(providerId))
            {
                Debug.Log($"[CdbRegistry] 注销 Provider：{providerId}");
                return true;
            }
            return false;
        }

        /// <summary>
        /// 获取 Provider
        /// </summary>
        public ICdbDataProvider GetProvider(string providerId)
        {
            return _providers.TryGetValue(providerId, out var provider) ? provider : null;
        }

        /// <summary>
        /// 获取所有已注册的 Provider
        /// </summary>
        public IReadOnlyDictionary<string, ICdbDataProvider> GetAllProviders()
        {
            return _providers;
        }

        /// <summary>
        /// 检查 Provider 是否已注册
        /// </summary>
        public bool IsRegistered(string providerId)
        {
            return _providers.ContainsKey(providerId);
        }

        /// <summary>
        /// 获取模块描述符
        /// </summary>
        public CdbModuleDescriptor GetDescriptor(string providerId)
        {
            return _descriptors.TryGetValue(providerId, out var descriptor) ? descriptor : null;
        }

        /// <summary>
        /// 注册模块描述符
        /// </summary>
        public void RegisterDescriptor(CdbModuleDescriptor descriptor)
        {
            if (descriptor == null || string.IsNullOrEmpty(descriptor.ProviderId))
            {
                return;
            }

            if (_descriptors.ContainsKey(descriptor.ProviderId))
            {
                Debug.LogWarning($"[CdbRegistry] Descriptor '{descriptor.ProviderId}' 已存在，将被覆盖");
            }

            _descriptors[descriptor.ProviderId] = descriptor;
        }

        /// <summary>
        /// 清除所有描述符
        /// </summary>
        public void ClearDescriptors()
        {
            _descriptors.Clear();
        }

        /// <summary>
        /// 对模块进行拓扑排序（按依赖顺序）
        /// </summary>
        /// <param name="providerIds">要排序的 Provider ID 列表</param>
        /// <returns>排序结果，若存在循环依赖则返回 null</returns>
        public TopologicalSortResult TopologicalSort(IEnumerable<string> providerIds)
        {
            var ids = providerIds.ToList();
            var result = new List<string>();
            var visited = new HashSet<string>();
            var inStack = new HashSet<string>();
            var cyclePath = new List<string>();

            bool Visit(string id)
            {
                if (inStack.Contains(id))
                {
                    // 检测到循环依赖
                    cyclePath.Add(id);
                    return false;
                }

                if (visited.Contains(id))
                {
                    return true;
                }

                visited.Add(id);
                inStack.Add(id);

                // 获取依赖
                var descriptor = GetDescriptor(id);
                if (descriptor != null)
                {
                    foreach (var dep in descriptor.Dependencies)
                    {
                        if (ids.Contains(dep) && !Visit(dep))
                        {
                            cyclePath.Insert(0, id);
                            return false;
                        }
                    }
                }

                inStack.Remove(id);
                result.Add(id);
                return true;
            }

            foreach (var id in ids)
            {
                if (!visited.Contains(id) && !Visit(id))
                {
                    return TopologicalSortResult.Failure(cyclePath);
                }
            }

            return TopologicalSortResult.Success(result);
        }

        /// <summary>
        /// 获取指定模块的完整依赖链（递归）
        /// </summary>
        /// <param name="providerId">目标模块 ID</param>
        /// <returns>依赖链（按依赖顺序，不含目标自身）</returns>
        public List<string> GetDependencyChain(string providerId)
        {
            var result = new List<string>();
            var visited = new HashSet<string>();

            void CollectDeps(string id)
            {
                if (visited.Contains(id) || id == providerId)
                {
                    return;
                }

                visited.Add(id);

                var descriptor = GetDescriptor(id);
                if (descriptor != null)
                {
                    foreach (var dep in descriptor.Dependencies)
                    {
                        CollectDeps(dep);
                    }
                }

                result.Add(id);
            }

            var targetDescriptor = GetDescriptor(providerId);
            if (targetDescriptor != null)
            {
                foreach (var dep in targetDescriptor.Dependencies)
                {
                    CollectDeps(dep);
                }
            }

            return result;
        }

        /// <summary>
        /// 校验所有模块的 schemaVersion 是否一致
        /// </summary>
        /// <returns>不一致的模块列表（providerId → actualVersion）</returns>
        public Dictionary<string, string> ValidateSchemaVersions()
        {
            var mismatched = new Dictionary<string, string>();

            foreach (var kvp in _descriptors)
            {
                if (!kvp.Value.IsLegacy && kvp.Value.SchemaVersion != ExpectedSchemaVersion)
                {
                    mismatched[kvp.Key] = kvp.Value.SchemaVersion;
                }
            }

            return mismatched;
        }

        /// <summary>
        /// 校验所有模块的依赖是否都已注册
        /// </summary>
        /// <returns>缺失依赖的模块（providerId → 缺失的依赖列表）</returns>
        public Dictionary<string, List<string>> ValidateDependencies()
        {
            var missing = new Dictionary<string, List<string>>();

            foreach (var kvp in _descriptors)
            {
                var descriptor = kvp.Value;
                var missingDeps = new List<string>();

                foreach (var dep in descriptor.Dependencies)
                {
                    if (!_descriptors.ContainsKey(dep) && !_providers.ContainsKey(dep))
                    {
                        missingDeps.Add(dep);
                    }
                }

                if (missingDeps.Count > 0)
                {
                    missing[kvp.Key] = missingDeps;
                }
            }

            return missing;
        }

        /// <summary>
        /// 重置所有 Provider 状态
        /// </summary>
        public void ResetAllProviders()
        {
            foreach (var provider in _providers.Values)
            {
                provider.Reset();
            }
        }

        /// <summary>
        /// 重置注册表（清除 Provider 和描述符）
        /// </summary>
        public void Reset()
        {
            ResetAllProviders();
            _providers.Clear();
            _descriptors.Clear();
        }

        /// <summary>
        /// 获取统计信息
        /// </summary>
        public RegistryStats GetStats()
        {
            var initializedCount = _providers.Values.Count(p => p.IsInitialized);
            return new RegistryStats(
                registeredCount: _providers.Count,
                initializedCount: initializedCount,
                descriptorCount: _descriptors.Count
            );
        }
    }

    /// <summary>
    /// 拓扑排序结果
    /// </summary>
    public class TopologicalSortResult
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool IsSuccess { get; }

        /// <summary>
        /// 排序后的 Provider ID 列表（按依赖顺序）
        /// </summary>
        public IReadOnlyList<string> SortedIds { get; }

        /// <summary>
        /// 循环依赖路径（仅在失败时有值）
        /// </summary>
        public IReadOnlyList<string> CyclePath { get; }

        private TopologicalSortResult(bool isSuccess, List<string> sortedIds, List<string> cyclePath)
        {
            IsSuccess = isSuccess;
            SortedIds = sortedIds ?? new List<string>();
            CyclePath = cyclePath ?? new List<string>();
        }

        public static TopologicalSortResult Success(List<string> sortedIds)
        {
            return new TopologicalSortResult(true, sortedIds, null);
        }

        public static TopologicalSortResult Failure(List<string> cyclePath)
        {
            return new TopologicalSortResult(false, null, cyclePath);
        }

        /// <summary>
        /// 获取循环依赖的描述
        /// </summary>
        public string GetCycleDescription()
        {
            if (IsSuccess || CyclePath.Count == 0)
            {
                return "";
            }
            return string.Join(" → ", CyclePath) + " → " + CyclePath[0];
        }
    }

    /// <summary>
    /// 注册表统计信息
    /// </summary>
    public readonly struct RegistryStats
    {
        public int RegisteredCount { get; }
        public int InitializedCount { get; }
        public int DescriptorCount { get; }

        public RegistryStats(int registeredCount, int initializedCount, int descriptorCount)
        {
            RegisteredCount = registeredCount;
            InitializedCount = initializedCount;
            DescriptorCount = descriptorCount;
        }

        public override string ToString()
        {
            return $"RegistryStats[registered={RegisteredCount}, initialized={InitializedCount}, descriptors={DescriptorCount}]";
        }
    }
}
