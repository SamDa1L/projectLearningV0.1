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
    public partial class CdbImportCoordinator
    {
        private const string DEFAULT_SCAN_PATH = "Assets/Resources/Data";
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

    }
}
