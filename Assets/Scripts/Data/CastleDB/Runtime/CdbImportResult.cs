using System.Collections.Generic;
using UnityEngine;

namespace CastleDB.Runtime
{
    /// <summary>
    /// CastleDB 导入结果（0.3 版本）
    /// 封装单个 Provider 的导入操作结果
    ///
    /// 职责：
    /// - 记录导入成功/失败状态
    /// - 统计创建/更新的资产数量
    /// - 收集错误/警告/信息日志
    /// - 追踪被修改的资产（用于回滚）
    ///
    /// 使用场景：
    /// - Provider.Import() 返回此对象
    /// - ImportCoordinator 汇总多个 Provider 的结果
    /// - 用于 UI 显示导入摘要
    /// </summary>
    public sealed class CdbImportResult
    {
        /// <summary>
        /// Provider ID（对应 CdbModuleDescriptor.ProviderId）
        /// </summary>
        public string ProviderId { get; }

        /// <summary>
        /// 导入是否成功
        /// </summary>
        public bool Success { get; }

        /// <summary>
        /// 创建的资产数量
        /// </summary>
        public int CreatedCount { get; }

        /// <summary>
        /// 更新的资产数量
        /// </summary>
        public int UpdatedCount { get; }

        /// <summary>
        /// 跳过的资产数量（无变化）
        /// </summary>
        public int SkippedCount { get; }

        /// <summary>
        /// 错误消息列表（导致导入失败的问题）
        /// </summary>
        public IReadOnlyList<string> Errors { get; }

        /// <summary>
        /// 警告消息列表（不影响导入但需注意的问题）
        /// </summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>
        /// 信息日志列表（导入过程中的详细信息）
        /// </summary>
        public IReadOnlyList<string> InfoLogs { get; }

        /// <summary>
        /// 被修改的资产路径列表（用于回滚）
        /// 格式：Assets/Resources/Profiles/Profile_Knight.asset
        /// </summary>
        public IReadOnlyList<string> DirtyAssets { get; }

        /// <summary>
        /// 私有构造函数，使用静态方法创建
        /// </summary>
        private CdbImportResult(
            string providerId,
            bool success,
            int createdCount,
            int updatedCount,
            int skippedCount,
            IReadOnlyList<string> errors,
            IReadOnlyList<string> warnings,
            IReadOnlyList<string> infoLogs,
            IReadOnlyList<string> dirtyAssets)
        {
            ProviderId = providerId;
            Success = success;
            CreatedCount = createdCount;
            UpdatedCount = updatedCount;
            SkippedCount = skippedCount;
            Errors = errors ?? new List<string>();
            Warnings = warnings ?? new List<string>();
            InfoLogs = infoLogs ?? new List<string>();
            DirtyAssets = dirtyAssets ?? new List<string>();
        }

        /// <summary>
        /// 创建成功的导入结果
        /// </summary>
        public static CdbImportResult Succeeded(
            string providerId,
            int createdCount = 0,
            int updatedCount = 0,
            int skippedCount = 0,
            List<string> warnings = null,
            List<string> infoLogs = null,
            List<string> dirtyAssets = null)
        {
            return new CdbImportResult(
                providerId: providerId,
                success: true,
                createdCount: createdCount,
                updatedCount: updatedCount,
                skippedCount: skippedCount,
                errors: new List<string>(),
                warnings: warnings,
                infoLogs: infoLogs,
                dirtyAssets: dirtyAssets
            );
        }

        /// <summary>
        /// 创建失败的导入结果
        /// </summary>
        public static CdbImportResult Failed(
            string providerId,
            List<string> errors,
            List<string> warnings = null,
            List<string> infoLogs = null)
        {
            return new CdbImportResult(
                providerId: providerId,
                success: false,
                createdCount: 0,
                updatedCount: 0,
                skippedCount: 0,
                errors: errors ?? new List<string> { "未知错误" },
                warnings: warnings,
                infoLogs: infoLogs,
                dirtyAssets: new List<string>()
            );
        }

        /// <summary>
        /// 获取导入摘要（用于日志输出）
        /// </summary>
        public string GetSummary()
        {
            if (Success)
            {
                return $"[{ProviderId}] 导入成功：创建 {CreatedCount}，更新 {UpdatedCount}，跳过 {SkippedCount}";
            }
            else
            {
                return $"[{ProviderId}] 导入失败：{Errors.Count} 个错误";
            }
        }

        /// <summary>
        /// 输出详细日志到 Unity Console
        /// </summary>
        public void LogToConsole()
        {
            if (Success)
            {
                Debug.Log($"[CastleDB Import] {GetSummary()}");
            }
            else
            {
                Debug.LogError($"[CastleDB Import] {GetSummary()}");
            }

            foreach (var error in Errors)
            {
                Debug.LogError($"  [ERROR] {error}");
            }

            foreach (var warning in Warnings)
            {
                Debug.LogWarning($"  [WARN] {warning}");
            }

            // InfoLogs 通常不输出到 Console，仅写入日志文件
        }

        public override string ToString()
        {
            return GetSummary();
        }
    }

    /// <summary>
    /// 导入结果构建器
    /// 用于在 Provider.Import() 过程中逐步收集结果
    /// </summary>
    public class CdbImportResultBuilder
    {
        private readonly string _providerId;
        private int _createdCount;
        private int _updatedCount;
        private int _skippedCount;
        private readonly List<string> _errors = new List<string>();
        private readonly List<string> _warnings = new List<string>();
        private readonly List<string> _infoLogs = new List<string>();
        private readonly List<string> _dirtyAssets = new List<string>();

        public CdbImportResultBuilder(string providerId)
        {
            _providerId = providerId;
        }

        /// <summary>
        /// 记录创建资产
        /// </summary>
        public CdbImportResultBuilder Created(string assetPath)
        {
            _createdCount++;
            _dirtyAssets.Add(assetPath);
            _infoLogs.Add($"创建：{assetPath}");
            return this;
        }

        /// <summary>
        /// 记录更新资产
        /// </summary>
        public CdbImportResultBuilder Updated(string assetPath)
        {
            _updatedCount++;
            _dirtyAssets.Add(assetPath);
            _infoLogs.Add($"更新：{assetPath}");
            return this;
        }

        /// <summary>
        /// 记录跳过资产
        /// </summary>
        public CdbImportResultBuilder Skipped(string assetPath, string reason = null)
        {
            _skippedCount++;
            _infoLogs.Add($"跳过：{assetPath}" + (reason != null ? $" ({reason})" : ""));
            return this;
        }

        /// <summary>
        /// 添加错误
        /// </summary>
        public CdbImportResultBuilder AddError(string message)
        {
            _errors.Add(message);
            return this;
        }

        /// <summary>
        /// 添加警告
        /// </summary>
        public CdbImportResultBuilder AddWarning(string message)
        {
            _warnings.Add(message);
            return this;
        }

        /// <summary>
        /// 添加信息日志
        /// </summary>
        public CdbImportResultBuilder AddInfo(string message)
        {
            _infoLogs.Add(message);
            return this;
        }

        /// <summary>
        /// 添加脏资产（不计入创建/更新计数）
        /// </summary>
        public CdbImportResultBuilder AddDirtyAsset(string assetPath)
        {
            if (!_dirtyAssets.Contains(assetPath))
            {
                _dirtyAssets.Add(assetPath);
            }
            return this;
        }

        /// <summary>
        /// 是否有错误
        /// </summary>
        public bool HasErrors => _errors.Count > 0;

        /// <summary>
        /// 构建结果对象
        /// </summary>
        public CdbImportResult Build()
        {
            if (_errors.Count > 0)
            {
                return CdbImportResult.Failed(
                    _providerId,
                    _errors,
                    _warnings,
                    _infoLogs
                );
            }

            return CdbImportResult.Succeeded(
                _providerId,
                _createdCount,
                _updatedCount,
                _skippedCount,
                _warnings,
                _infoLogs,
                _dirtyAssets
            );
        }
    }
}
