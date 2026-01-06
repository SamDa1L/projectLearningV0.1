using System;
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CastleDB.Runtime;
using static CastleDB.Editor.CdbImportCoordinator;

namespace CastleDB.Editor
{
    /// <summary>
    /// CastleDB 右键上下文菜单扩展（0.3 版本）
    /// 提供单文件右键导入功能
    ///
    /// 菜单项：
    /// - Assets/CastleDB/Import This File - 仅对 .cdb 文件可见
    ///
    /// 功能（Phase 5）：
    /// - 自动检测 Meta Sheet 中的 providerId
    /// - 收集完整依赖链（多层递归）
    /// - 提供 3 种导入模式：仅初始化依赖链/递归导入依赖链/直接导入
    /// - 循环依赖检测
    /// - 集成 ImportCoordinator 的备份回滚机制
    /// </summary>
    public static class CdbContextMenuExtension
    {
        private const string MENU_PATH = "Assets/CastleDB/Import This File";
        private const int MENU_PRIORITY = 100;

        /// <summary>
        /// 右键导入单个 .cdb 文件
        /// </summary>
        [MenuItem(MENU_PATH, priority = MENU_PRIORITY)]
        public static void ImportSelectedCdbFile()
        {
            var selectedObject = Selection.activeObject;
            if (selectedObject == null)
            {
                Debug.LogWarning("[CdbContextMenu] 请先选择一个 .cdb 文件");
                return;
            }

            string assetPath = AssetDatabase.GetAssetPath(selectedObject);
            if (string.IsNullOrEmpty(assetPath) || !assetPath.EndsWith(".cdb"))
            {
                Debug.LogWarning($"[CdbContextMenu] 选中的不是 .cdb 文件：{assetPath}");
                return;
            }

            ImportCdbFile(assetPath);
        }

        /// <summary>
        /// 验证菜单项是否可见
        /// 仅当选中 .cdb 文件时显示
        /// </summary>
        [MenuItem(MENU_PATH, validate = true)]
        public static bool ValidateImportSelectedCdbFile()
        {
            var selectedObject = Selection.activeObject;
            if (selectedObject == null) return false;

            string assetPath = AssetDatabase.GetAssetPath(selectedObject);
            return !string.IsNullOrEmpty(assetPath) && assetPath.EndsWith(".cdb");
        }

        /// <summary>
        /// 导入指定的 .cdb 文件
        /// </summary>
        public static void ImportCdbFile(string assetPath)
        {
            Debug.Log($"[CdbContextMenu] 开始导入：{assetPath}");

            try
            {
                // 1. 加载文件
                var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
                if (asset == null)
                {
                    Debug.LogError($"[CdbContextMenu] 无法加载文件：{assetPath}");
                    return;
                }

                // 2. 解析数据
                var source = new CastleDbJsonSource(asset);
                var root = source.ReadCastleDbJson();
                if (root == null)
                {
                    Debug.LogError($"[CdbContextMenu] 无法解析 CastleDB 数据：{assetPath}");
                    return;
                }

                // 3. 提取 Meta Sheet
                var metaSheet = root.sheets.Find(s => s.name == "Meta");
                if (metaSheet == null || metaSheet.lines == null)
                {
                    Debug.LogError($"[CdbContextMenu] 文件缺少 Meta Sheet：{assetPath}");
                    EditorUtility.DisplayDialog(
                        "导入失败",
                        "文件缺少 Meta Sheet。\n请确保 .cdb 文件包含 Meta Sheet 并定义 providerId。",
                        "确定");
                    return;
                }

                // 4. 解析 Meta 条目
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

                var descriptor = CdbModuleDescriptor.FromMetaEntries(metaEntries, assetPath);

                // 5. 检查 providerId
                if (descriptor == null || string.IsNullOrEmpty(descriptor.ProviderId))
                {
                    Debug.LogError($"[CdbContextMenu] 文件缺少 providerId，无法导入：{assetPath}");
                    EditorUtility.DisplayDialog(
                        "导入失败",
                        "文件 Meta Sheet 中缺少 providerId。\n请先在 CastleDB 的 Meta Sheet 中补齐 providerId/schemaVersion/resourcePath 后再导入。",
                        "确定");
                    return;
                }

                // 6. 确保 Provider 已注册
                CdbProviderBootstrap.EnsureRegistered();
                var registry = CdbDataProviderRegistry.Instance;

                // 7. 获取 Provider
                var provider = registry.GetProvider(descriptor.ProviderId);
                if (provider == null)
                {
                    Debug.LogError($"[CdbContextMenu] 未找到 Provider：{descriptor.ProviderId}");
                    EditorUtility.DisplayDialog(
                        "导入失败",
                        $"未找到对应的 Provider：{descriptor.ProviderId}\n请确保 Provider 已在 CdbProviderBootstrap 中注册。",
                        "确定");
                    return;
                }

                // 8. 检查依赖并让用户选择处理方式
                var importMode = DependencyImportMode.DirectImport;

                if (descriptor.Dependencies.Count > 0)
                {
                    string depsStr = string.Join(", ", descriptor.Dependencies);
                    importMode = ShowDependencyImportDialog(descriptor.ProviderId, depsStr);

                    if (importMode == DependencyImportMode.Cancel)
                    {
                        Debug.Log("[CdbContextMenu] 用户取消导入");
                        return;
                    }
                }

                // 9. 调用 ImportCoordinator 执行原子性导入
                Debug.Log($"[CdbContextMenu] 调用 ImportCoordinator.ImportSingleModule（模式：{importMode}）");

                // 转换导入模式枚举
                CdbImportCoordinator.DependencyImportMode coordinatorMode;
                switch (importMode)
                {
                    case DependencyImportMode.InitializeOnly:
                        coordinatorMode = CdbImportCoordinator.DependencyImportMode.InitializeOnly;
                        break;
                    case DependencyImportMode.RecursiveImport:
                        coordinatorMode = CdbImportCoordinator.DependencyImportMode.RecursiveImport;
                        break;
                    case DependencyImportMode.DirectImport:
                        coordinatorMode = CdbImportCoordinator.DependencyImportMode.DirectImport;
                        break;
                    default:
                        Debug.LogError($"[CdbContextMenu] 未知的导入模式：{importMode}");
                        return;
                }

                var coordinator = new CdbImportCoordinator(registry);
                var result = coordinator.ImportSingleModule(descriptor, coordinatorMode);

                // 10. 显示结果
                if (result.Success)
                {
                    string summary = $"导入成功！\n\n" +
                        $"模块：{descriptor.ProviderId}\n" +
                        $"创建：{result.CreatedCount} 个资产\n" +
                        $"更新：{result.UpdatedCount} 个资产\n\n" +
                        $"详细日志：\n{string.Join("\n", result.LogMessages.Take(10))}";

                    if (result.LogMessages.Count > 10)
                    {
                        summary += $"\n... 以及 {result.LogMessages.Count - 10} 条其他日志";
                    }

                    EditorUtility.DisplayDialog("导入成功", summary, "确定");
                }
                else
                {
                    string errorSummary = $"导入失败！\n\n" +
                        $"模块：{descriptor.ProviderId}\n\n" +
                        $"错误日志：\n{string.Join("\n", result.LogMessages.Skip(Math.Max(0, result.LogMessages.Count - 15)))}";

                    EditorUtility.DisplayDialog("导入失败", errorSummary, "确定");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[CdbContextMenu] 导入异常：{ex.Message}\n{ex.StackTrace}");
                EditorUtility.DisplayDialog(
                    "导入异常",
                    $"发生异常：\n{ex.Message}",
                    "确定");
            }
        }

        #region 依赖链处理

        /// <summary>
        /// 显示依赖导入选项对话框
        /// </summary>
        private static DependencyImportMode ShowDependencyImportDialog(string targetProviderId, string dependencies)
        {
            int choice = EditorUtility.DisplayDialogComplex(
                "检测到依赖",
                $"目标模块：{targetProviderId}\n" +
                $"依赖：{dependencies}\n\n" +
                $"请选择导入模式：\n\n" +
                $"• 仅初始化依赖链：\n" +
                $"  依赖链执行 Initialize + Validate（不导入）\n" +
                $"  目标文件执行完整导入\n\n" +
                $"• 递归导入依赖链：\n" +
                $"  依赖链和目标文件都按顺序完整导入\n" +
                $"  确保整条链路数据最新\n\n" +
                $"• 取消：\n" +
                $"  不执行任何操作",
                "仅初始化依赖链",
                "取消",
                "递归导入依赖链");

            switch (choice)
            {
                case 0:
                    return DependencyImportMode.InitializeOnly;
                case 1:
                    return DependencyImportMode.Cancel;
                case 2:
                    return DependencyImportMode.RecursiveImport;
                default:
                    return DependencyImportMode.Cancel;
            }
        }

        /// <summary>
        /// 定义 Cancel 模式以支持对话框取消操作
        /// </summary>
        private enum DependencyImportMode
        {
            InitializeOnly = CdbImportCoordinator.DependencyImportMode.InitializeOnly,
            RecursiveImport = CdbImportCoordinator.DependencyImportMode.RecursiveImport,
            DirectImport = CdbImportCoordinator.DependencyImportMode.DirectImport,
            Cancel = 999
        }

        #endregion
    }
}
