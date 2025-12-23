using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using CastleDB.Runtime;
using CastleDB.Runtime.Providers;

namespace CastleDB.Editor
{
    /// <summary>
    /// CastleDB 右键上下文菜单扩展（0.3 版本）
    /// 提供单文件右键导入功能
    ///
    /// 菜单项：
    /// - Assets/CastleDB/Import This File - 仅对 .cdb 文件可见
    ///
    /// 功能：
    /// - 自动检测 Meta Sheet 中的 providerId
    /// - 调用对应的 Provider 执行导入
    /// - 支持依赖检测与递归导入（待实现）
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
                if (descriptor.IsLegacy || string.IsNullOrEmpty(descriptor.ProviderId))
                {
                    Debug.LogError($"[CdbContextMenu] 文件无 providerId，无法使用 Provider 流程：{assetPath}");
                    EditorUtility.DisplayDialog(
                        "导入失败",
                        "文件 Meta Sheet 中缺少 providerId。\n这是 0.2 Legacy 格式，请使用 'Tools/CastleDB/Import All' 导入。",
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

                // 8. 检查依赖
                if (descriptor.Dependencies.Count > 0)
                {
                    string depsStr = string.Join(", ", descriptor.Dependencies);
                    bool proceed = EditorUtility.DisplayDialog(
                        "检测到依赖",
                        $"此文件依赖以下模块：\n{depsStr}\n\n是否继续导入？\n（请确保依赖模块已导入）",
                        "继续导入",
                        "取消");

                    if (!proceed)
                    {
                        Debug.Log("[CdbContextMenu] 用户取消导入");
                        return;
                    }
                }

                // 9. 注册描述符
                registry.RegisterDescriptor(descriptor);

                // 10. 初始化 Provider
                Debug.Log($"[CdbContextMenu] 初始化 Provider：{descriptor.ProviderId}");
                provider.Initialize(source, descriptor);

                // 11. 校验
                var validationErrors = provider.Validate(descriptor);
                if (validationErrors.Count > 0)
                {
                    string errorsStr = string.Join("\n", validationErrors);
                    Debug.LogError($"[CdbContextMenu] 校验失败：\n{errorsStr}");
                    EditorUtility.DisplayDialog(
                        "校验失败",
                        $"数据校验发现 {validationErrors.Count} 个错误：\n\n{errorsStr}",
                        "确定");
                    return;
                }

                // 12. 导入
                Debug.Log($"[CdbContextMenu] 导入数据：{descriptor.ProviderId}");
                var importResult = provider.Import(descriptor);
                importResult.LogToConsole();

                // 13. 保存资产
                if (importResult.Success && importResult.DirtyAssets.Count > 0)
                {
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                }

                // 14. 显示结果
                if (importResult.Success)
                {
                    EditorUtility.DisplayDialog(
                        "导入成功",
                        $"Provider: {descriptor.ProviderId}\n" +
                        $"创建: {importResult.CreatedCount}\n" +
                        $"更新: {importResult.UpdatedCount}\n" +
                        $"跳过: {importResult.SkippedCount}",
                        "确定");
                }
                else
                {
                    string errorsStr = string.Join("\n", importResult.Errors);
                    EditorUtility.DisplayDialog(
                        "导入失败",
                        $"Provider: {descriptor.ProviderId}\n\n错误：\n{errorsStr}",
                        "确定");
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
    }
}
