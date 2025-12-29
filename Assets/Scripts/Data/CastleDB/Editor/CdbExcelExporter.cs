using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace CastleDB.Editor
{
    /// <summary>
    /// CastleDB Excel 导出器（Phase 12 - v8.0）
    ///
    /// 契约 [C-Tool-2]：
    /// - 触发时机：Import All / Import This File 成功后
    /// - 输出路径：ProjectRoot/Docs/cdbExcel/（禁止 Assets/）
    /// - 数据来源：源 .cdb 文件（禁止从产物反向导出）
    /// - 文件格式：单个 .xlsx 包含多个 worksheet + 固定 __Meta
    /// - 类型映射：string/bool/number 使用 Excel 原生类型
    /// - 原子替换：.tmp.xlsx + File.Replace 优先策略
    /// </summary>
    public class CdbExcelExporter
    {
        private const string TOOL_VERSION = "0.4.12";

        // 一次性日志去重
        private readonly HashSet<string> _loggedErrors = new HashSet<string>();

        #region Public API

        /// <summary>
        /// 导出单个 .cdb 文件到 Excel（无状态设计，显式传递所有参数）
        /// </summary>
        /// <param name="cdbFilePath">.cdb 文件路径（相对或绝对）</param>
        /// <param name="cdbImportRootFullPath">CdbImportRoot 的完整路径（已解析）</param>
        /// <param name="mode">触发模式（All/ThisFile）</param>
        /// <returns>成功返回 true，失败返回 false（不影响导入结果）</returns>
        public bool ExportToExcel(string cdbFilePath, string cdbImportRootFullPath, TriggerMode mode)
        {
            try
            {
                // 0. 校验 CdbImportRoot（Phase 12: 不可访问时直接失败，不走 Orphaned）
                if (!ValidateCdbImportRoot(cdbImportRootFullPath))
                {
                    Debug.LogError($"[ExcelExporter] CdbImportRoot 不可访问，中止导出：{cdbImportRootFullPath}");
                    return false;
                }

                // 1. 读取源 .cdb 文件
                string fullCdbPath = Path.GetFullPath(cdbFilePath);
                if (!File.Exists(fullCdbPath))
                {
                    Debug.LogError($"[ExcelExporter] 文件不存在：{fullCdbPath}");
                    return false;
                }

                string jsonContent = File.ReadAllText(fullCdbPath);
                JObject root = JObject.Parse(jsonContent);

                // 2. 计算输出路径
                string outputPath = CalculateExcelOutputPath(cdbFilePath, cdbImportRootFullPath);

                // 3. 生成 .xlsx 文件
                return GenerateXlsxFile(root, outputPath, fullCdbPath, cdbImportRootFullPath, mode);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ExcelExporter] 导出失败：{ex}");
                return false;
            }
        }

        /// <summary>
        /// 校验 CdbImportRoot 目录存在性与可访问性
        /// </summary>
        private bool ValidateCdbImportRoot(string fullRootPath)
        {
            if (string.IsNullOrWhiteSpace(fullRootPath))
            {
                return false;
            }

            if (!Directory.Exists(fullRootPath))
            {
                return false;
            }

            try
            {
                // 轻量访问性探测
                Directory.EnumerateFileSystemEntries(fullRootPath).FirstOrDefault();
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 计算 Excel 输出路径（公开用于测试）
        /// </summary>
        public string CalculateExcelOutputPath(string cdbFilePath, string cdbImportRootFullPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string excelOutputDir = Path.Combine(projectRoot, "Docs", "cdbExcel");
            string orphanedDir = "__Orphaned";

            // 检查是否不安全路径
            if (IsUnsafePath(cdbFilePath, cdbImportRootFullPath))
            {
                return SanitizeUnsafePath(cdbFilePath, excelOutputDir, orphanedDir);
            }

            // 计算相对路径
            try
            {
                string rootFull = Path.GetFullPath(cdbImportRootFullPath);
                string fileFull = Path.GetFullPath(cdbFilePath);
                string relativePath = Path.GetRelativePath(rootFull, fileFull);

                // 构建镜像路径
                string fileNameWithoutExt = Path.GetFileNameWithoutExtension(relativePath);
                string directory = Path.GetDirectoryName(relativePath) ?? "";

                if (string.IsNullOrEmpty(directory))
                {
                    return Path.Combine(excelOutputDir, fileNameWithoutExt + ".xlsx");
                }
                else
                {
                    return Path.Combine(excelOutputDir, directory, fileNameWithoutExt + ".xlsx");
                }
            }
            catch
            {
                // 路径计算失败，视为不安全
                return SanitizeUnsafePath(cdbFilePath, excelOutputDir, orphanedDir);
            }
        }

        #endregion

        #region Path Safety

        /// <summary>
        /// 判断路径是否不安全（按 segment 判定 ".."）
        /// </summary>
        private bool IsUnsafePath(string cdbFilePath, string cdbImportRoot)
        {
            try
            {
                string rootFull = Path.GetFullPath(cdbImportRoot);
                string fileFull = Path.GetFullPath(cdbFilePath);

                // 计算相对路径
                string rel = Path.GetRelativePath(rootFull, fileFull);

                // 1) 若返回绝对路径（跨盘符），直接不安全
                if (Path.IsPathRooted(rel)) return true;

                // 2) 按 segment 判定 ".."
                var segs = rel.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                return segs.Any(s => s == "..");
            }
            catch
            {
                // 路径解析失败，视为不安全
                return true;
            }
        }

        /// <summary>
        /// 安全化不安全路径（格式：<name>__<hash8>.xlsx）
        /// </summary>
        private string SanitizeUnsafePath(string cdbFilePath, string excelOutputDir, string orphanedDir)
        {
            // 计算 SHA1 hash（取前 8 位）
            string fullPath = Path.GetFullPath(cdbFilePath);
            using var sha1 = SHA1.Create();
            byte[] hashBytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(fullPath));
            string hashHex = BitConverter.ToString(hashBytes).Replace("-", "");
            string hash8 = hashHex.Substring(0, 8);

            // 提取文件名
            string fileName = Path.GetFileName(cdbFilePath);
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(fileName);

            // 格式：<name>__<hash8>.xlsx（hash 在后，双下划线）
            return Path.Combine(excelOutputDir, orphanedDir, $"{fileNameWithoutExt}__{hash8}.xlsx");
        }

        #endregion

        #region Worksheet Name Sanitization

        /// <summary>
        /// 合法化 worksheet 名称
        /// </summary>
        private string SanitizeWorksheetName(string name)
        {
            if (string.IsNullOrEmpty(name))
                name = "";

            // 1. Trim 首尾空格
            name = name.Trim();

            // 2. 非法字符替换
            char[] illegalChars = { '[', ']', ':', '*', '?', '/', '\\' };
            foreach (char c in illegalChars)
            {
                name = name.Replace(c, '_');
            }

            // 3. 长度截断至 31 字符
            if (name.Length > 31)
            {
                name = name.Substring(0, 31);
            }

            // 4. 若为空，兜底为 "Sheet"
            if (string.IsNullOrEmpty(name))
            {
                name = "Sheet";
            }

            return name;
        }

        /// <summary>
        /// 生成唯一 worksheet 名称（重名追加 _2/_3...）
        /// </summary>
        private string MakeUniqueWorksheetName(string name, HashSet<string> usedNames)
        {
            if (!usedNames.Contains(name))
            {
                return name;
            }

            int counter = 2;
            string uniqueName;
            do
            {
                uniqueName = $"{name}_{counter}";
                counter++;
            } while (usedNames.Contains(uniqueName));

            return uniqueName;
        }

        #endregion

        #region Excel Generation

        /// <summary>
        /// 生成 .xlsx 文件（替代 GenerateExcelCompatibleFile）
        /// </summary>
        private bool GenerateXlsxFile(JObject root, string targetPath, string sourceCdbPathFull,
            string cdbImportRootFullPath, TriggerMode mode)
        {
            try
            {
                var sheets = root["sheets"] as JArray;
                if (sheets == null || sheets.Count == 0)
                {
                    Debug.LogWarning($"[ExcelExporter] 无有效 sheets，跳过导出：{sourceCdbPathFull}");
                    return false;
                }

                // 创建 NPOI Workbook
                IWorkbook workbook = new XSSFWorkbook();

                // 预占 __Meta 名称（确保数据 sheet 无法占用）
                var usedNames = new HashSet<string>();
                usedNames.Add("__Meta");

                // 记录映射信息（用于 __Meta worksheet）
                var sheetMappings = new List<(string original, string sanitized, string final, int rowCount, int colCount)>();

                // 遍历 sheets，创建 worksheet
                foreach (var sheetToken in sheets)
                {
                    var sheet = sheetToken as JObject;
                    if (sheet == null) continue;

                    string originalName = sheet["name"]?.ToString() ?? "Sheet";
                    string sanitized = SanitizeWorksheetName(originalName);
                    string finalName = MakeUniqueWorksheetName(sanitized, usedNames);

                    // 处理 __Meta 冲突：数据表被重命名为 __Meta_2
                    if (finalName == "__Meta")
                    {
                        finalName = MakeUniqueWorksheetName("__Meta_2", usedNames);
                    }

                    usedNames.Add(finalName);

                    // 创建 worksheet 并写入数据
                    int rowCount, colCount;
                    CreateWorksheet(workbook, finalName, sheet, out rowCount, out colCount);

                    sheetMappings.Add((originalName, sanitized, finalName, rowCount, colCount));
                }

                // 创建 __Meta worksheet
                CreateMetaWorksheet(workbook, sheetMappings, sourceCdbPathFull, cdbImportRootFullPath, mode);

                // 写入文件（原子替换）
                return WriteWithAtomicReplacement(workbook, targetPath);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ExcelExporter] GenerateXlsxFile 失败：{ex}");
                return false;
            }
        }

        /// <summary>
        /// 创建 worksheet 并写入数据
        /// </summary>
        private void CreateWorksheet(IWorkbook workbook, string sheetName, JObject sheetData,
            out int rowCount, out int colCount)
        {
            ISheet sheet = workbook.CreateSheet(sheetName);

            var columns = sheetData["columns"] as JArray;
            var lines = sheetData["lines"] as JArray;

            if (columns == null || lines == null)
            {
                rowCount = 0;
                colCount = 0;
                return;
            }

            // 写入列头
            IRow headerRow = sheet.CreateRow(0);
            var columnNames = new List<string>();
            int colIndex = 0;
            foreach (var colToken in columns)
            {
                var col = colToken as JObject;
                if (col != null)
                {
                    string colName = col["name"]?.ToString() ?? "Unknown";
                    columnNames.Add(colName);
                    ICell cell = headerRow.CreateCell(colIndex++);
                    cell.SetCellValue(colName);
                }
            }

            // 写入数据行
            int rowIndex = 1;
            foreach (var lineToken in lines)
            {
                var line = lineToken as JObject;
                if (line == null) continue;

                IRow row = sheet.CreateRow(rowIndex++);
                colIndex = 0;
                foreach (var colName in columnNames)
                {
                    ICell cell = row.CreateCell(colIndex++);
                    var value = line[colName];
                    WriteCellValue(cell, value);
                }
            }

            rowCount = lines.Count;
            colCount = columnNames.Count;
        }

        /// <summary>
        /// 写入单元格值（类型映射）
        /// </summary>
        private void WriteCellValue(ICell cell, JToken value)
        {
            if (value == null || value.Type == JTokenType.Null)
            {
                cell.SetCellType(CellType.Blank);
                return;
            }

            switch (value.Type)
            {
                case JTokenType.Boolean:
                    cell.SetCellValue(value.Value<bool>());
                    break;

                case JTokenType.Integer:
                    // 使用 long 避免 Int32 溢出，Excel 数值本质是 double
                    long longValue = value.Value<long>();
                    cell.SetCellValue((double)longValue);
                    break;

                case JTokenType.Float:
                    cell.SetCellValue(value.Value<double>());
                    break;

                case JTokenType.String:
                    // 关键：直接写入，禁止二次序列化
                    cell.SetCellValue(value.Value<string>());
                    break;

                case JTokenType.Array:
                case JTokenType.Object:
                    // 数组/对象以紧凑 JSON 序列化为字符串
                    cell.SetCellValue(value.ToString(Formatting.None));
                    break;

                default:
                    // 其他类型转为字符串
                    cell.SetCellValue(value.ToString());
                    break;
            }
        }

        /// <summary>
        /// 创建 __Meta worksheet（固定 schema）
        /// </summary>
        private void CreateMetaWorksheet(IWorkbook workbook,
            List<(string original, string sanitized, string final, int rowCount, int colCount)> sheetMappings,
            string sourceCdbPathFull, string cdbImportRootFullPath, TriggerMode mode)
        {
            ISheet metaSheet = workbook.CreateSheet("__Meta");

            int rowIndex = 0;

            // Key-Value 区
            CreateMetaRow(metaSheet, rowIndex++, "exportTimeUtc", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            CreateMetaRow(metaSheet, rowIndex++, "triggerMode", mode.ToString());
            CreateMetaRow(metaSheet, rowIndex++, "toolVersion", TOOL_VERSION);

            // 计算相对路径（安全路径时填写）
            bool isOrphaned = IsUnsafePath(sourceCdbPathFull, cdbImportRootFullPath);
            if (!isOrphaned)
            {
                try
                {
                    string rootFull = Path.GetFullPath(cdbImportRootFullPath);
                    string fileFull = Path.GetFullPath(sourceCdbPathFull);
                    string relativePath = Path.GetRelativePath(rootFull, fileFull);
                    CreateMetaRow(metaSheet, rowIndex++, "sourceCdbPathRel", relativePath);
                }
                catch
                {
                    // 相对路径计算失败，跳过
                }
            }

            // orphan 时记录完整路径和 hash
            if (isOrphaned)
            {
                CreateMetaRow(metaSheet, rowIndex++, "sourceCdbPathFull", Path.GetFullPath(sourceCdbPathFull));

                // 计算 hash8
                using var sha1 = SHA1.Create();
                byte[] hashBytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(sourceCdbPathFull)));
                string hashHex = BitConverter.ToString(hashBytes).Replace("-", "");
                string hash8 = hashHex.Substring(0, 8);
                CreateMetaRow(metaSheet, rowIndex++, "orphanHash8", hash8);
            }

            CreateMetaRow(metaSheet, rowIndex++, "isOrphaned", isOrphaned.ToString().ToLower());
            CreateMetaRow(metaSheet, rowIndex++, "unityVersion", Application.unityVersion);

            // 空行分隔
            rowIndex++;

            // 映射表区
            IRow mappingHeaderRow = metaSheet.CreateRow(rowIndex++);
            mappingHeaderRow.CreateCell(0).SetCellValue("originalSheetName");
            mappingHeaderRow.CreateCell(1).SetCellValue("sanitizedSheetName");
            mappingHeaderRow.CreateCell(2).SetCellValue("finalWorksheetName");
            mappingHeaderRow.CreateCell(3).SetCellValue("rowCount");
            mappingHeaderRow.CreateCell(4).SetCellValue("colCount");

            foreach (var (original, sanitized, final, rows, cols) in sheetMappings)
            {
                IRow mappingRow = metaSheet.CreateRow(rowIndex++);
                mappingRow.CreateCell(0).SetCellValue(original);
                mappingRow.CreateCell(1).SetCellValue(sanitized);
                mappingRow.CreateCell(2).SetCellValue(final);
                mappingRow.CreateCell(3).SetCellValue(rows);
                mappingRow.CreateCell(4).SetCellValue(cols);
            }
        }

        /// <summary>
        /// 创建 __Meta 的 Key-Value 行
        /// </summary>
        private void CreateMetaRow(ISheet sheet, int rowIndex, string key, string value)
        {
            IRow row = sheet.CreateRow(rowIndex);
            row.CreateCell(0).SetCellValue(key);
            row.CreateCell(1).SetCellValue(value);
        }

        #endregion

        #region Atomic Replacement

        /// <summary>
        /// 写入文件（原子替换：.tmp.xlsx + File.Replace）
        /// </summary>
        private bool WriteWithAtomicReplacement(IWorkbook workbook, string targetPath)
        {
            string tmpPath = Path.Combine(
                Path.GetDirectoryName(targetPath),
                Path.GetFileNameWithoutExtension(targetPath) + ".tmp.xlsx"
            );

            try
            {
                // 0. 确保输出目录存在
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath));

                // 1. 写入临时文件
                using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    workbook.Write(fs);
                }

                // 2. 原子替换
                if (File.Exists(targetPath))
                {
                    string backupPath = targetPath + ".bak";

                    try
                    {
                        File.Replace(tmpPath, targetPath, backupPath);
                        if (File.Exists(backupPath)) File.Delete(backupPath);
                    }
                    catch (IOException ioEx)
                    {
                        // 典型：Excel 占用/锁定。保持旧文件不变，只清理 tmp
                        if (File.Exists(tmpPath)) File.Delete(tmpPath);
                        Debug.LogWarning($"[ExcelExporter] 目标文件可能被占用，未覆盖旧文件：{targetPath} ({ioEx.GetType().Name})");
                        return false;
                    }
                    catch (PlatformNotSupportedException)
                    {
                        // 安全降级：先备份旧文件，再覆盖；失败可回滚
                        if (File.Exists(backupPath)) File.Delete(backupPath);
                        if (File.Exists(targetPath)) File.Move(targetPath, backupPath);

                        try
                        {
                            File.Move(tmpPath, targetPath);
                            if (File.Exists(backupPath)) File.Delete(backupPath);
                        }
                        catch
                        {
                            // 回滚
                            if (File.Exists(targetPath)) File.Delete(targetPath);
                            if (File.Exists(backupPath)) File.Move(backupPath, targetPath);
                            if (File.Exists(tmpPath)) File.Delete(tmpPath);
                            throw;
                        }
                    }
                }
                else
                {
                    File.Move(tmpPath, targetPath);
                }

                Debug.Log($"[ExcelExporter] ✓ 导出成功：{targetPath}");
                return true;
            }
            catch (Exception ex)
            {
                // 3. 失败清理
                if (File.Exists(tmpPath))
                    File.Delete(tmpPath);

                // 4. 记录错误但不中断导入
                Debug.LogError($"[ExcelExporter] 导出失败：{ex}");
                return false;
            }
        }

        #endregion
    }
}
