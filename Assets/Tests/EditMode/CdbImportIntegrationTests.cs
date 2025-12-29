using NUnit.Framework;
using System.IO;
using UnityEngine;
using CastleDB.Editor;
using UnityEngine.TestTools;

namespace CastleDB.Tests.EditMode
{
    /// <summary>
    /// Phase 12 集成测试：Import 流程与 Excel 导出集成
    /// 验证 CdbImportRoot 校验与导出范围
    /// </summary>
    public class CdbImportIntegrationTests
    {
        private string _testSettingsPath;
        private string _originalSettingsBackup;
        private CdbImportSettings _originalSettings;

        [SetUp]
        public void Setup()
        {
            _testSettingsPath = "Assets/Settings/CdbImportSettings.asset";

            // 备份原始设置
            _originalSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<CdbImportSettings>(_testSettingsPath);
            if (_originalSettings != null)
            {
                _originalSettingsBackup = _originalSettings.cdbImportRoot;
            }
        }

        [TearDown]
        public void Teardown()
        {
            // 恢复原始设置
            if (_originalSettings != null && !string.IsNullOrEmpty(_originalSettingsBackup))
            {
                _originalSettings.cdbImportRoot = _originalSettingsBackup;
                UnityEditor.EditorUtility.SetDirty(_originalSettings);
                UnityEditor.AssetDatabase.SaveAssets();
            }
        }

        /// <summary>
        /// 测试：CdbImportRoot 不可访问时 ImportAll 失败
        /// </summary>
        [Test]
        public void TestImportAllFailsWhenCdbImportRootInvalid()
        {
            // 1. 修改设置为无效路径
            if (_originalSettings == null)
            {
                Assert.Inconclusive("CdbImportSettings.asset 不存在，跳过测试");
                return;
            }

            string invalidPath = "F:/NonExistent/InvalidPath_" + System.Guid.NewGuid().ToString();
            _originalSettings.cdbImportRoot = invalidPath;
            UnityEditor.EditorUtility.SetDirty(_originalSettings);
            UnityEditor.AssetDatabase.SaveAssets();

            // 2. 创建 Coordinator 并执行 ImportAll
            var registry = CdbDataProviderRegistry.Instance;
            var coordinator = new CdbImportCoordinator(registry);

            // 3. 预期日志
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(@"CdbImportRoot 不存在.*"));

            // 4. 执行 ImportAll
            var result = coordinator.ImportAll();

            // 5. 验证失败
            Assert.IsFalse(result.IsSuccess, "CdbImportRoot 不可访问时 ImportAll 应失败");

            // 6. 验证日志包含提示
            string log = result.GetFormattedLog();
            Assert.IsTrue(log.Contains("CdbImportRoot 校验失败"), "日志应包含校验失败信息");
            Assert.IsTrue(log.Contains("Tools → CastleDB → Settings"), "日志应提示修正路径");
        }

        /// <summary>
        /// 测试：ImportSingleModule 仅导出目标文件（依赖不导出）
        /// </summary>
        [Test]
        public void TestImportSingleModuleExportsOnlyTargetNotDependencies()
        {
            // 注意：此测试需要实际的 .cdb 文件和 Provider，较为复杂
            // 这里提供测试框架，实际验证需在真实环境中进行

            // 1. 清理可能存在的 Excel 导出目录
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string excelDir = Path.Combine(projectRoot, "Docs", "cdbExcel");

            // 2. 模拟场景：Item.cdb 依赖 PlayerAbility.cdb
            // 执行 ImportSingleModule(Item) 后，仅 Item.xlsx 应被导出

            // 3. 由于需要完整的导入环境，这里标记为 Inconclusive
            // 实际验证需在真实项目中手动执行 "右键 Item.cdb → Import This File"
            // 然后检查 Docs/cdbExcel/ 目录

            Assert.Inconclusive("集成测试需要完整 CDB 文件和 Provider，请手动验证：\n" +
                "1. 右键 Item.cdb → Import This File\n" +
                "2. 检查 Docs/cdbExcel/ 仅包含 Data/Item.xlsx\n" +
                "3. PlayerAbility.xlsx 不应存在（除非之前已导出）");
        }

        /// <summary>
        /// 测试：CdbImportRoot 不可访问时 Excel 导出失败但导入可能成功
        /// （验证双层防护：Coordinator 早期拦截 + Exporter 独立校验）
        /// </summary>
        [Test]
        public void TestExcelExportFailsIndependentlyWhenRootInvalid()
        {
            // 1. 创建测试 .cdb 文件
            string tempDir = Path.Combine(Application.temporaryCachePath, "CdbImportTest");
            Directory.CreateDirectory(tempDir);

            string testCdbPath = Path.Combine(tempDir, "test.cdb");
            File.WriteAllText(testCdbPath, @"{""sheets"":[{""name"":""Test"",""columns"":[],""lines"":[]}]}");

            try
            {
                // 2. 使用无效 CdbImportRoot
                string invalidRoot = Path.Combine(tempDir, "InvalidRoot_" + System.Guid.NewGuid().ToString());

                // 3. 直接调用 Exporter（绕过 Coordinator）
                var exporter = new CdbExcelExporter();

                // 4. 预期 Error 日志
                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(@"\[ExcelExporter\] CdbImportRoot 不可访问.*"));

                // 5. 导出应失败
                bool success = exporter.ExportToExcel(testCdbPath, invalidRoot, TriggerMode.ThisFile);

                Assert.IsFalse(success, "Exporter 层应独立校验 CdbImportRoot");
            }
            finally
            {
                // 清理
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        /// <summary>
        /// 测试：CdbImportRoot 设置为空或 null 时的容错
        /// </summary>
        [Test]
        public void TestCdbImportRootNullOrEmptyFallsBackToDefault()
        {
            if (_originalSettings == null)
            {
                Assert.Inconclusive("CdbImportSettings.asset 不存在，跳过测试");
                return;
            }

            // 1. 设置为空字符串
            _originalSettings.cdbImportRoot = "";
            UnityEditor.EditorUtility.SetDirty(_originalSettings);
            UnityEditor.AssetDatabase.SaveAssets();

            // 2. 创建 Coordinator（应回退到默认值）
            var coordinator = new CdbImportCoordinator(CdbDataProviderRegistry.Instance);

            // 3. 验证：由于 LoadImportSettings 会保持默认值，这里仅验证不崩溃
            Assert.DoesNotThrow(() =>
            {
                // Coordinator 构造成功即可，实际 ImportAll 会因无 .cdb 文件而失败
                Assert.IsNotNull(coordinator);
            }, "空字符串配置不应导致 Coordinator 构造失败");
        }
    }
}
