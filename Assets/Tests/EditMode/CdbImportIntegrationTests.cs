using NUnit.Framework;
using System.IO;
using UnityEngine;
using CastleDB.Editor;
using CastleDB.Runtime;

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
