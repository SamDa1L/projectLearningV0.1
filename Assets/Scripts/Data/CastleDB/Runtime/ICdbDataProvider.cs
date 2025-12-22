using System.Collections.Generic;

namespace CastleDB.Runtime
{
    /// <summary>
    /// CastleDB 数据提供者接口（0.3 版本）
    /// 每个 .cdb 文件对应一个 Provider 实现
    ///
    /// 职责：
    /// - 解析特定 .cdb 文件的数据
    /// - 校验数据完整性和引用有效性
    /// - 导入数据生成 Unity 资产（仅 Editor 模式）
    /// - 提供运行时数据查询接口
    ///
    /// 生命周期：
    /// 1. Registry 读取 Meta Sheet 生成 CdbModuleDescriptor
    /// 2. Provider.Initialize() 解析 .cdb 数据
    /// 3. Provider.Validate() 校验数据（含跨文件引用）
    /// 4. Provider.Import() 生成 Unity 资产（仅 Editor）
    ///
    /// 注意事项：
    /// - Provider 不持有资源路径与依赖信息，这些由 descriptor 提供
    /// - Provider 仅声明 ExpectedProviderId 并做一致性断言
    /// - Import 阶段禁止直接调用 AssetDatabase.SaveAssets()
    /// </summary>
    public interface ICdbDataProvider
    {
        /// <summary>
        /// Provider 期望的 providerId（与 Meta Sheet 中的 providerId 对应）
        /// 用于 Registry 注册时的一致性检查
        /// </summary>
        string ExpectedProviderId { get; }

        /// <summary>
        /// Provider 是否已初始化
        /// </summary>
        bool IsInitialized { get; }

        /// <summary>
        /// 初始化 Provider
        /// 解析 .cdb 文件数据并缓存
        /// </summary>
        /// <param name="source">数据源</param>
        /// <param name="descriptor">模块描述符（包含 providerId/dependencies/resourcePath）</param>
        void Initialize(ICastleDbSource source, CdbModuleDescriptor descriptor);

        /// <summary>
        /// 校验数据完整性
        /// 在 Import 之前调用，返回空列表表示校验通过
        /// </summary>
        /// <param name="descriptor">模块描述符</param>
        /// <returns>校验错误列表，空列表表示通过</returns>
        List<string> Validate(CdbModuleDescriptor descriptor);

#if UNITY_EDITOR
        /// <summary>
        /// 导入数据生成 Unity 资产（仅 Editor 模式）
        ///
        /// 注意：
        /// - 此方法仅在 Editor 模式下调用
        /// - 禁止直接调用 AssetDatabase.SaveAssets()
        /// - 返回 CdbImportResult 包含创建/更新的资产列表
        /// - 由 ImportCoordinator 统一处理保存和回滚
        /// </summary>
        /// <param name="descriptor">模块描述符</param>
        /// <returns>导入结果</returns>
        CdbImportResult Import(CdbModuleDescriptor descriptor);
#endif

        /// <summary>
        /// 获取指定类型的所有条目
        /// </summary>
        /// <typeparam name="T">条目类型（如 NpcEntry、PlayerEntry）</typeparam>
        /// <returns>条目列表，若类型不匹配则返回空列表</returns>
        List<T> GetAllEntries<T>() where T : class;

        /// <summary>
        /// 按 ID 获取指定类型的条目
        /// </summary>
        /// <typeparam name="T">条目类型</typeparam>
        /// <param name="id">条目 ID</param>
        /// <returns>条目对象，若未找到则返回 null</returns>
        T GetEntryById<T>(string id) where T : class;

        /// <summary>
        /// 重置 Provider 状态
        /// 清除缓存数据，将 IsInitialized 设为 false
        /// </summary>
        void Reset();
    }

    /// <summary>
    /// CdbDataProvider 基类
    /// 提供通用实现，子类只需实现特定逻辑
    /// </summary>
    public abstract class CdbDataProviderBase : ICdbDataProvider
    {
        /// <summary>
        /// Provider 期望的 providerId
        /// </summary>
        public abstract string ExpectedProviderId { get; }

        /// <summary>
        /// Provider 是否已初始化
        /// </summary>
        public bool IsInitialized { get; protected set; }

        /// <summary>
        /// 当前模块描述符
        /// </summary>
        protected CdbModuleDescriptor CurrentDescriptor { get; private set; }

        /// <summary>
        /// 初始化 Provider
        /// </summary>
        public void Initialize(ICastleDbSource source, CdbModuleDescriptor descriptor)
        {
            // 一致性检查
            if (descriptor.ProviderId != ExpectedProviderId)
            {
                throw new System.InvalidOperationException(
                    $"Provider '{ExpectedProviderId}' 收到不匹配的 descriptor.ProviderId: '{descriptor.ProviderId}'");
            }

            CurrentDescriptor = descriptor;

            // 读取并解析数据
            var root = source.ReadCastleDbJson();
            if (root == null)
            {
                throw new System.Exception($"无法读取 CastleDB 数据：{descriptor.ResourcePath}");
            }

            // 调用子类的具体初始化逻辑
            OnInitialize(root, descriptor);

            IsInitialized = true;
        }

        /// <summary>
        /// 子类实现具体的初始化逻辑
        /// </summary>
        protected abstract void OnInitialize(CastleDbRoot root, CdbModuleDescriptor descriptor);

        /// <summary>
        /// 校验数据
        /// </summary>
        public List<string> Validate(CdbModuleDescriptor descriptor)
        {
            if (!IsInitialized)
            {
                return new List<string> { $"Provider '{ExpectedProviderId}' 未初始化" };
            }

            return OnValidate(descriptor);
        }

        /// <summary>
        /// 子类实现具体的校验逻辑
        /// </summary>
        protected abstract List<string> OnValidate(CdbModuleDescriptor descriptor);

#if UNITY_EDITOR
        /// <summary>
        /// 导入数据
        /// </summary>
        public CdbImportResult Import(CdbModuleDescriptor descriptor)
        {
            if (!IsInitialized)
            {
                return CdbImportResult.Failed(ExpectedProviderId,
                    new List<string> { $"Provider '{ExpectedProviderId}' 未初始化" });
            }

            return OnImport(descriptor);
        }

        /// <summary>
        /// 子类实现具体的导入逻辑
        /// </summary>
        protected abstract CdbImportResult OnImport(CdbModuleDescriptor descriptor);
#endif

        /// <summary>
        /// 获取所有条目（子类需实现）
        /// </summary>
        public abstract List<T> GetAllEntries<T>() where T : class;

        /// <summary>
        /// 按 ID 获取条目（子类需实现）
        /// </summary>
        public abstract T GetEntryById<T>(string id) where T : class;

        /// <summary>
        /// 重置 Provider
        /// </summary>
        public virtual void Reset()
        {
            IsInitialized = false;
            CurrentDescriptor = null;
            OnReset();
        }

        /// <summary>
        /// 子类实现具体的重置逻辑
        /// </summary>
        protected virtual void OnReset() { }
    }
}
