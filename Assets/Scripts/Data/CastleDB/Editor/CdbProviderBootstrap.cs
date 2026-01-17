using UnityEngine;
using CastleDB.Runtime;
using CastleDB.Editor.Providers;

namespace CastleDB.Editor
{
    /// <summary>
    /// CastleDB Provider 启动引导（0.3 版本）
    /// 负责在编辑器启动时注册默认 Provider
    ///
    /// 使用方式：
    /// - Editor：在 Import All 入口调用 EnsureRegistered()
    ///
    /// 注意：
    /// - 0.3 版本采用保守策略，运行时仍以导入产物为权威
    /// - Provider 主要服务于 Editor 导入流程
    /// </summary>
    public static class CdbProviderBootstrap
    {
        private static bool _registered = false;

        /// <summary>
        /// 确保所有默认 Provider 已注册
        /// 可重复调用，内部有幂等保护
        /// </summary>
        public static void EnsureRegistered()
        {
            RegisterDefaults();
            _registered = true;
        }

        /// <summary>
        /// 注册所有默认 Provider
        /// </summary>
        public static void RegisterDefaults()
        {
            var registry = CdbDataProviderRegistry.Instance;

            // Phase 2: Monster Provider
            if (!registry.IsRegistered("Monster"))
            {
                registry.Register(new MonsterDataProvider());
                Debug.Log("[CdbProviderBootstrap] 注册 MonsterDataProvider");
            }

            // Phase 4: Player Provider
            if (!registry.IsRegistered("Player"))
            {
                registry.Register(new PlayerDataProvider());
                Debug.Log("[CdbProviderBootstrap] 注册 PlayerDataProvider");
            }

            // Phase 4: Ability Provider
            if (!registry.IsRegistered("PlayerAbility"))
            {
                registry.Register(new AbilityDataProvider());
                Debug.Log("[CdbProviderBootstrap] 注册 AbilityDataProvider");
            }

            // 0.4 Phase 1: Item Provider
            if (!registry.IsRegistered("Item"))
            {
                registry.Register(new ItemDataProvider());
                Debug.Log("[CdbProviderBootstrap] 注册 ItemDataProvider");
            }

            // 0.5 阶段7：遗物 Provider
            if (!registry.IsRegistered("Relic"))
            {
                registry.Register(new RelicDataProvider());
                Debug.Log("[CdbProviderBootstrap] 注册 RelicDataProvider");
            }

            // 0.5 Phase 1-4: Status Provider
            if (!registry.IsRegistered("Status"))
            {
                registry.Register(new StatusDataProvider());
                Debug.Log("[CdbProviderBootstrap] 注册 StatusDataProvider");
            }

            Debug.Log($"[CdbProviderBootstrap] Provider 注册完成，当前注册数：{registry.GetStats().RegisteredCount}");
        }

        /// <summary>
        /// 重置注册状态（用于测试）
        /// </summary>
        public static void Reset()
        {
            _registered = false;
            CdbDataProviderRegistry.Instance.Reset();
        }
    }
}
