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
}
}
