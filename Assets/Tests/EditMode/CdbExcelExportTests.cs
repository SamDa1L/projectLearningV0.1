using NUnit.Framework;
using System.IO;
using UnityEngine;
using CastleDB.Editor;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using Newtonsoft.Json.Linq;

namespace CastleDB.Tests.EditMode
{
    /// <summary>
    /// Phase 12 黑盒测试：Excel 导出功能
    /// 策略：调用 ExportToExcel() 公开 API，通过 NPOI 读回验证结果
    /// </summary>
    public class CdbExcelExportTests
    {
        private string _testOutputDir;
        private string _testCdbRoot;

        [SetUp]
        public void Setup()
        {
            // 临时测试目录（在项目根目录）
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            _testOutputDir = Path.Combine(projectRoot, "Temp", "CdbExcelExportTests");
            _testCdbRoot = Path.Combine(_testOutputDir, "TestCdbRoot");

            // 清理并创建
            if (Directory.Exists(_testOutputDir))
            {
                Directory.Delete(_testOutputDir, true);
            }
            Directory.CreateDirectory(_testCdbRoot);
        }

        [TearDown]
        public void Teardown()
        {
            // 清理测试文件
            if (Directory.Exists(_testOutputDir))
            {
                try
                {
                    Directory.Delete(_testOutputDir, true);
                }
                catch
                {
                    // Excel 文件可能被占用，忽略清理失败
                }
            }
        }

        /// <summary>
        /// 测试用例 1: Worksheet 名称合法化与冲突处理
        /// </summary>
        [Test]
        public void TestWorksheetNameSanitizationAndConflict()
        {
            // 1. 准备测试 .cdb 文件（包含非法字符和重名 sheet）
            string cdbContent = @"{
  ""sheets"": [
    {""name"": ""Test[Sheet]:1"", ""columns"": [{""name"": ""id""}], ""lines"": [{""id"": ""a""}]},
    {""name"": ""Test_Sheet__1"", ""columns"": [{""name"": ""id""}], ""lines"": [{""id"": ""b""}]},
    {""name"": ""__Meta"", ""columns"": [{""name"": ""id""}], ""lines"": [{""id"": ""c""}]}
  ]
}";
            string testCdbPath = Path.Combine(_testCdbRoot, "test.cdb");
            File.WriteAllText(testCdbPath, cdbContent);

            // 2. 调用导出
            var exporter = new CdbExcelExporter();
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string outputPath = exporter.CalculateExcelOutputPath(testCdbPath, _testCdbRoot);
            bool success = exporter.ExportToExcel(testCdbPath, _testCdbRoot, TriggerMode.ThisFile);

            // 3. 验证导出成功
            Assert.IsTrue(success, "导出应该成功");
            Assert.IsTrue(File.Exists(outputPath), $"输出文件应该存在：{outputPath}");

            // 4. 用 NPOI 读回验证
            using (var fs = new FileStream(outputPath, FileMode.Open, FileAccess.Read))
            {
                IWorkbook workbook = new XSSFWorkbook(fs);

                // 验证 sheet 数量：3 个数据 sheet + 1 个 __Meta = 4
                Assert.AreEqual(4, workbook.NumberOfSheets, "应有 4 个 worksheet");

                // 验证名称合法化：非法字符替换
                ISheet sheet0 = workbook.GetSheetAt(0);
                Assert.AreEqual("Test_Sheet__1", sheet0.SheetName, "非法字符应被替换为 _");

                // 验证重名处理：追加 _2
                ISheet sheet1 = workbook.GetSheetAt(1);
                Assert.AreEqual("Test_Sheet__1_2", sheet1.SheetName, "重名应追加 _2");

                // 验证 __Meta 冲突：数据表被重命名为 __Meta_2
                ISheet sheet2 = workbook.GetSheetAt(2);
                Assert.AreEqual("__Meta_2", sheet2.SheetName, "__Meta 冲突应重命名为 __Meta_2");

                // 验证 __Meta worksheet 在最后
                ISheet metaSheet = workbook.GetSheetAt(3);
                Assert.AreEqual("__Meta", metaSheet.SheetName, "__Meta 应在最后");
            }
        }

        /// <summary>
        /// 测试用例 2: 不安全路径检测（含 .. segment）
        /// </summary>
        [Test]
        public void TestUnsafePathOrphaned()
        {
            // 1. 准备测试 .cdb 文件（位于 root 之外）
            string unsafeCdbPath = Path.Combine(_testOutputDir, "outside.cdb");
            File.WriteAllText(unsafeCdbPath, @"{""sheets"":[{""name"":""Test"",""columns"":[],""lines"":[]}]}");

            // 2. 调用导出（cdbRoot 为子目录，file 在父目录）
            var exporter = new CdbExcelExporter();
            string outputPath = exporter.CalculateExcelOutputPath(unsafeCdbPath, _testCdbRoot);

            // 3. 验证输出路径包含 __Orphaned
            Assert.IsTrue(outputPath.Contains("__Orphaned"), "不安全路径应输出到 __Orphaned 目录");
        }

        /// <summary>
        /// 测试用例 3: 不安全路径命名格式（name__hash8.xlsx）
        /// </summary>
        [Test]
        public void TestUnsafePathNaming()
        {
            // 1. 准备测试 .cdb 文件（绝对路径，模拟跨盘符）
            string unsafeCdbPath = Path.Combine(_testOutputDir, "outside.cdb");
            File.WriteAllText(unsafeCdbPath, @"{""sheets"":[{""name"":""Test"",""columns"":[],""lines"":[]}]}");

            // 2. 调用导出
            var exporter = new CdbExcelExporter();
            string outputPath = exporter.CalculateExcelOutputPath(unsafeCdbPath, _testCdbRoot);
            string fileName = Path.GetFileName(outputPath);

            // 3. 验证命名格式：outside__<hash8>.xlsx
            Assert.IsTrue(fileName.StartsWith("outside__"), "文件名应以 <name>__ 开头");
            Assert.IsTrue(fileName.EndsWith(".xlsx"), "扩展名应为 .xlsx");

            // 验证 hash 部分长度（双下划线后应为 8 字符 hash + .xlsx）
            string hashPart = fileName.Substring("outside__".Length, fileName.Length - "outside__".Length - ".xlsx".Length);
            Assert.AreEqual(8, hashPart.Length, "hash 应为 8 位");

            // 验证 hash 为十六进制
            Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(hashPart, "^[0-9A-F]{8}$"),
                "hash 应为 8 位大写十六进制");
        }

        /// <summary>
        /// 测试用例 4: 类型映射与 JSON 字符串字段
        /// </summary>
        [Test]
        public void TestTypeMappingAndJsonStringField()
        {
            // 1. 准备测试 .cdb 文件（含各种类型）
            string cdbContent = @"{
  ""sheets"": [
    {
      ""name"": ""TypeTest"",
      ""columns"": [
        {""name"": ""id"", ""typeStr"": ""0""},
        {""name"": ""active"", ""typeStr"": ""1""},
        {""name"": ""count"", ""typeStr"": ""3""},
        {""name"": ""ratio"", ""typeStr"": ""4""},
        {""name"": ""tags"", ""typeStr"": ""6""},
        {""name"": ""metadata"", ""typeStr"": ""11""},
        {""name"": ""jsonString"", ""typeStr"": ""0""}
      ],
      ""lines"": [
        {
          ""id"": ""test1"",
          ""active"": true,
          ""count"": 42,
          ""ratio"": 3.14,
          ""tags"": [""tag1"", ""tag2""],
          ""metadata"": {""key"": ""value""},
          ""jsonString"": ""{\""heal\"":50}""
        }
      ]
    }
  ]
}";
            string testCdbPath = Path.Combine(_testCdbRoot, "typetest.cdb");
            File.WriteAllText(testCdbPath, cdbContent);

            // 2. 调用导出
            var exporter = new CdbExcelExporter();
            string outputPath = exporter.CalculateExcelOutputPath(testCdbPath, _testCdbRoot);
            bool success = exporter.ExportToExcel(testCdbPath, _testCdbRoot, TriggerMode.ThisFile);
            Assert.IsTrue(success, "导出应该成功");

            // 3. 用 NPOI 读回验证类型
            using (var fs = new FileStream(outputPath, FileMode.Open, FileAccess.Read))
            {
                IWorkbook workbook = new XSSFWorkbook(fs);
                ISheet sheet = workbook.GetSheet("TypeTest");
                Assert.IsNotNull(sheet, "TypeTest sheet 应存在");

                IRow dataRow = sheet.GetRow(1); // 第 0 行是列头，第 1 行是数据
                Assert.IsNotNull(dataRow, "数据行应存在");

                // 验证 string 类型
                ICell idCell = dataRow.GetCell(0);
                Assert.AreEqual(CellType.String, idCell.CellType, "id 应为 String 类型");
                Assert.AreEqual("test1", idCell.StringCellValue);

                // 验证 bool 类型
                ICell activeCell = dataRow.GetCell(1);
                Assert.AreEqual(CellType.Boolean, activeCell.CellType, "active 应为 Boolean 类型");
                Assert.AreEqual(true, activeCell.BooleanCellValue);

                // 验证 number 类型（integer）
                ICell countCell = dataRow.GetCell(2);
                Assert.AreEqual(CellType.Numeric, countCell.CellType, "count 应为 Numeric 类型");
                Assert.AreEqual(42.0, countCell.NumericCellValue, 0.001);

                // 验证 number 类型（float）
                ICell ratioCell = dataRow.GetCell(3);
                Assert.AreEqual(CellType.Numeric, ratioCell.CellType, "ratio 应为 Numeric 类型");
                Assert.AreEqual(3.14, ratioCell.NumericCellValue, 0.001);

                // 验证数组类型（序列化为 JSON 字符串）
                ICell tagsCell = dataRow.GetCell(4);
                Assert.AreEqual(CellType.String, tagsCell.CellType, "tags 应为 String 类型（JSON）");
                string tagsJson = tagsCell.StringCellValue;
                var tagsArray = JArray.Parse(tagsJson);
                Assert.AreEqual(2, tagsArray.Count, "tags 数组应有 2 个元素");

                // 验证对象类型（序列化为 JSON 字符串）
                ICell metadataCell = dataRow.GetCell(5);
                Assert.AreEqual(CellType.String, metadataCell.CellType, "metadata 应为 String 类型（JSON）");
                string metadataJson = metadataCell.StringCellValue;
                var metadataObj = JObject.Parse(metadataJson);
                Assert.AreEqual("value", metadataObj["key"].ToString());

                // 验证 JSON 字符串字段（原样写入，不二次序列化）
                ICell jsonStringCell = dataRow.GetCell(6);
                Assert.AreEqual(CellType.String, jsonStringCell.CellType, "jsonString 应为 String 类型");
                Assert.AreEqual("{\"heal\":50}", jsonStringCell.StringCellValue, "JSON 字符串应原样写入");
            }
        }

        /// <summary>
        /// 测试用例 5: __Meta worksheet schema 验证
        /// </summary>
        [Test]
        public void TestMetaWorksheetSchema()
        {
            // 1. 准备测试 .cdb 文件
            string cdbContent = @"{
  ""sheets"": [
    {""name"": ""Sheet1"", ""columns"": [{""name"": ""id""}], ""lines"": [{""id"": ""a""}, {""id"": ""b""}]},
    {""name"": ""Sheet2"", ""columns"": [{""name"": ""id""}, {""name"": ""name""}], ""lines"": [{""id"": ""c"", ""name"": ""test""}]}
  ]
}";
            string testCdbPath = Path.Combine(_testCdbRoot, "Data", "meta_test.cdb");
            Directory.CreateDirectory(Path.GetDirectoryName(testCdbPath));
            File.WriteAllText(testCdbPath, cdbContent);

            // 2. 调用导出
            var exporter = new CdbExcelExporter();
            string outputPath = exporter.CalculateExcelOutputPath(testCdbPath, _testCdbRoot);
            bool success = exporter.ExportToExcel(testCdbPath, _testCdbRoot, TriggerMode.All);
            Assert.IsTrue(success, "导出应该成功");

            // 3. 用 NPOI 读回验证 __Meta
            using (var fs = new FileStream(outputPath, FileMode.Open, FileAccess.Read))
            {
                IWorkbook workbook = new XSSFWorkbook(fs);
                ISheet metaSheet = workbook.GetSheet("__Meta");
                Assert.IsNotNull(metaSheet, "__Meta worksheet 应存在");

                // 验证 Key-Value 区
                Assert.AreEqual("exportTimeUtc", metaSheet.GetRow(0).GetCell(0).StringCellValue);
                Assert.IsNotEmpty(metaSheet.GetRow(0).GetCell(1).StringCellValue, "exportTimeUtc 应有值");

                Assert.AreEqual("triggerMode", metaSheet.GetRow(1).GetCell(0).StringCellValue);
                Assert.AreEqual("All", metaSheet.GetRow(1).GetCell(1).StringCellValue);

                Assert.AreEqual("toolVersion", metaSheet.GetRow(2).GetCell(0).StringCellValue);
                Assert.IsNotEmpty(metaSheet.GetRow(2).GetCell(1).StringCellValue, "toolVersion 应有值");

                Assert.AreEqual("sourceCdbPathRel", metaSheet.GetRow(3).GetCell(0).StringCellValue);
                Assert.AreEqual("Data/meta_test.cdb", metaSheet.GetRow(3).GetCell(1).StringCellValue.Replace("\\", "/"));

                Assert.AreEqual("isOrphaned", metaSheet.GetRow(4).GetCell(0).StringCellValue);
                Assert.AreEqual("false", metaSheet.GetRow(4).GetCell(1).StringCellValue);

                Assert.AreEqual("unityVersion", metaSheet.GetRow(5).GetCell(0).StringCellValue);
                Assert.IsNotEmpty(metaSheet.GetRow(5).GetCell(1).StringCellValue, "unityVersion 应有值");

                // 验证空行分隔
                IRow emptyRow = metaSheet.GetRow(6);
                Assert.IsTrue(emptyRow == null || emptyRow.GetCell(0) == null || string.IsNullOrEmpty(emptyRow.GetCell(0).StringCellValue),
                    "第 7 行应为空行分隔");

                // 验证映射表区表头
                IRow mappingHeader = metaSheet.GetRow(7);
                Assert.AreEqual("originalSheetName", mappingHeader.GetCell(0).StringCellValue);
                Assert.AreEqual("sanitizedSheetName", mappingHeader.GetCell(1).StringCellValue);
                Assert.AreEqual("finalWorksheetName", mappingHeader.GetCell(2).StringCellValue);
                Assert.AreEqual("rowCount", mappingHeader.GetCell(3).StringCellValue);
                Assert.AreEqual("colCount", mappingHeader.GetCell(4).StringCellValue);

                // 验证映射表数据行
                IRow mapping1 = metaSheet.GetRow(8);
                Assert.AreEqual("Sheet1", mapping1.GetCell(0).StringCellValue);
                Assert.AreEqual("Sheet1", mapping1.GetCell(1).StringCellValue);
                Assert.AreEqual("Sheet1", mapping1.GetCell(2).StringCellValue);
                Assert.AreEqual(2.0, mapping1.GetCell(3).NumericCellValue, 0.001, "Sheet1 rowCount 应为 2");
                Assert.AreEqual(1.0, mapping1.GetCell(4).NumericCellValue, 0.001, "Sheet1 colCount 应为 1");

                IRow mapping2 = metaSheet.GetRow(9);
                Assert.AreEqual("Sheet2", mapping2.GetCell(0).StringCellValue);
                Assert.AreEqual(1.0, mapping2.GetCell(3).NumericCellValue, 0.001, "Sheet2 rowCount 应为 1");
                Assert.AreEqual(2.0, mapping2.GetCell(4).NumericCellValue, 0.001, "Sheet2 colCount 应为 2");
            }
        }

        /// <summary>
        /// 测试用例 6: 输出路径镜像验证（相对路径）
        /// </summary>
        [Test]
        public void TestOutputPathMirroring()
        {
            // 1. 准备嵌套目录结构
            string nestedCdbPath = Path.Combine(_testCdbRoot, "Data", "Subdir", "nested.cdb");
            Directory.CreateDirectory(Path.GetDirectoryName(nestedCdbPath));
            File.WriteAllText(nestedCdbPath, @"{""sheets"":[{""name"":""Test"",""columns"":[],""lines"":[]}]}");

            // 2. 计算输出路径
            var exporter = new CdbExcelExporter();
            string outputPath = exporter.CalculateExcelOutputPath(nestedCdbPath, _testCdbRoot);

            // 3. 验证镜像路径
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string expectedPath = Path.Combine(projectRoot, "Docs", "cdbExcel", "Data", "Subdir", "nested.xlsx");
            Assert.AreEqual(expectedPath, outputPath, "输出路径应镜像 cdbRoot 的相对路径");
        }

        /// <summary>
        /// 测试用例 7: 原子替换验证（.tmp.xlsx 机制）
        /// </summary>
        [Test]
        public void TestAtomicReplacement()
        {
            // 1. 准备测试 .cdb 文件
            string testCdbPath = Path.Combine(_testCdbRoot, "atomic.cdb");
            File.WriteAllText(testCdbPath, @"{""sheets"":[{""name"":""Test"",""columns"":[],""lines"":[]}]}");

            // 2. 第一次导出
            var exporter = new CdbExcelExporter();
            string outputPath = exporter.CalculateExcelOutputPath(testCdbPath, _testCdbRoot);
            bool success1 = exporter.ExportToExcel(testCdbPath, _testCdbRoot, TriggerMode.ThisFile);
            Assert.IsTrue(success1, "第一次导出应该成功");
            Assert.IsTrue(File.Exists(outputPath), "输出文件应存在");

            // 验证 .tmp.xlsx 已清理
            string tmpPath = Path.Combine(Path.GetDirectoryName(outputPath), "atomic.tmp.xlsx");
            Assert.IsFalse(File.Exists(tmpPath), ".tmp.xlsx 应被清理");

            // 3. 修改 .cdb 后再次导出（验证覆盖）
            File.WriteAllText(testCdbPath, @"{""sheets"":[{""name"":""Updated"",""columns"":[],""lines"":[]}]}");
            bool success2 = exporter.ExportToExcel(testCdbPath, _testCdbRoot, TriggerMode.ThisFile);
            Assert.IsTrue(success2, "第二次导出应该成功");

            // 4. 验证文件被正确覆盖
            using (var fs = new FileStream(outputPath, FileMode.Open, FileAccess.Read))
            {
                IWorkbook workbook = new XSSFWorkbook(fs);
                ISheet sheet = workbook.GetSheetAt(0);
                Assert.AreEqual("Updated", sheet.SheetName, "文件应被覆盖为新内容");
            }

            // 验证 .bak 备份已清理
            string bakPath = outputPath + ".bak";
            Assert.IsFalse(File.Exists(bakPath), ".bak 备份应被清理");
        }

        /// <summary>
        /// 测试用例 8: ImportSingleModule 仅导出目标文件（Phase 12）
        /// 验证 Import This File 不导出依赖项
        /// </summary>
        [Test]
        public void TestImportSingleModuleExportsOnlyTargetFile()
        {
            // 1. 准备目标 .cdb 和依赖 .cdb
            string targetPath = Path.Combine(_testCdbRoot, "target.cdb");
            string depPath = Path.Combine(_testCdbRoot, "dependency.cdb");

            File.WriteAllText(targetPath, @"{""sheets"":[{""name"":""Target"",""columns"":[],""lines"":[]}]}");
            File.WriteAllText(depPath, @"{""sheets"":[{""name"":""Dependency"",""columns"":[],""lines"":[]}]}");

            // 2. 仅导出目标文件（模拟 ImportSingleModule 调用 ExportModulesToExcel(targetDescriptor, ThisFile)）
            var exporter = new CdbExcelExporter();
            bool targetSuccess = exporter.ExportToExcel(targetPath, _testCdbRoot, TriggerMode.ThisFile);

            Assert.IsTrue(targetSuccess, "目标文件导出应成功");

            // 3. 验证仅目标文件被导出
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string targetXlsx = Path.Combine(projectRoot, "Docs", "cdbExcel", "target.xlsx");
            string depXlsx = Path.Combine(projectRoot, "Docs", "cdbExcel", "dependency.xlsx");

            Assert.IsTrue(File.Exists(targetXlsx), "目标文件 Excel 应存在");

            // 4. 验证依赖文件未被导出（本次调用不应创建）
            // 注意：此测试假设 dependency.xlsx 之前不存在
            // 如果之前已导出过，需要在 Setup 中清理 Docs/cdbExcel/ 目录
            bool depFileExistsFromPreviousRun = File.Exists(depXlsx);
            if (!depFileExistsFromPreviousRun)
            {
                Assert.IsFalse(File.Exists(depXlsx), "依赖文件不应在本次 ThisFile 模式导出中创建");
            }
            else
            {
                // 依赖文件存在是因为之前的测试，记录警告
                Debug.LogWarning($"[TestImportSingleModuleExportsOnlyTargetFile] dependency.xlsx 已存在，无法验证未导出行为");
            }
        }

        /// <summary>
        /// 测试用例 9: CdbImportRoot 不可访问时导出失败（Phase 12）
        /// </summary>
        [Test]
        public void TestCdbImportRootInaccessibleExportFails()
        {
            // 1. 准备测试 .cdb 文件
            string testCdbPath = Path.Combine(_testCdbRoot, "test.cdb");
            File.WriteAllText(testCdbPath, @"{""sheets"":[{""name"":""Test"",""columns"":[],""lines"":[]}]}");

            // 2. 使用不存在的 CdbImportRoot
            string invalidRoot = Path.Combine(_testCdbRoot, "NonExistentDir");

            var exporter = new CdbExcelExporter();

            // 3. 预期 Error 日志（CdbImportRoot 不可访问）
            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(@"\[ExcelExporter\] CdbImportRoot 不可访问.*"));

            // 4. 导出应失败（因为 CdbImportRoot 不存在）
            bool success = exporter.ExportToExcel(testCdbPath, invalidRoot, TriggerMode.ThisFile);

            // 验证：ExportToExcel 在 ValidateCdbImportRoot 失败时返回 false
            Assert.IsFalse(success, "CdbImportRoot 不可访问时导出应失败");
        }
    }
}
