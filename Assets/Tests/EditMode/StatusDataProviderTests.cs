using System.Collections.Generic;
using System.IO;
using System.Linq;
using CastleDB.Editor.Providers;
using CastleDB.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class StatusDataProviderTests
{
    private const string TEST_CDB_DIR = "Assets/Tests/EditMode/TestData";
    private const string STATUS_CATALOG_PATH = "Assets/Resources/Config/StatusCatalog.asset";

    private string _backupDirAbsolutePath;
    private string _statusCatalogAbsolutePath;
    private string _statusCatalogMetaAbsolutePath;
    private string _backupStatusCatalogAbsolutePath;
    private string _backupStatusCatalogMetaAbsolutePath;
    private bool _hadOriginalStatusCatalog;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        _backupDirAbsolutePath = Path.Combine(projectRoot, "Temp", "StatusDataProviderTestsBackup");
        Directory.CreateDirectory(_backupDirAbsolutePath);

        _statusCatalogAbsolutePath = Path.Combine(Application.dataPath, "Resources", "Config", "StatusCatalog.asset");
        _statusCatalogMetaAbsolutePath = _statusCatalogAbsolutePath + ".meta";
        _backupStatusCatalogAbsolutePath = Path.Combine(_backupDirAbsolutePath, "StatusCatalog.asset");
        _backupStatusCatalogMetaAbsolutePath = Path.Combine(_backupDirAbsolutePath, "StatusCatalog.asset.meta");

        _hadOriginalStatusCatalog = File.Exists(_statusCatalogAbsolutePath);
        if (_hadOriginalStatusCatalog)
        {
            File.Copy(_statusCatalogAbsolutePath, _backupStatusCatalogAbsolutePath, true);
            if (File.Exists(_statusCatalogMetaAbsolutePath))
            {
                File.Copy(_statusCatalogMetaAbsolutePath, _backupStatusCatalogMetaAbsolutePath, true);
            }
        }
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_backupDirAbsolutePath) && Directory.Exists(_backupDirAbsolutePath))
            {
                Directory.Delete(_backupDirAbsolutePath, true);
            }
        }
        catch
        {
            // Best effort.
        }
    }

    [SetUp]
    public void Setup()
    {
        if (!Directory.Exists(TEST_CDB_DIR))
        {
            Directory.CreateDirectory(TEST_CDB_DIR);
        }

        AssetDatabase.Refresh();
    }

    [TearDown]
    public void TearDown()
    {
        if (AssetDatabase.LoadMainAssetAtPath(STATUS_CATALOG_PATH) != null)
        {
            AssetDatabase.DeleteAsset(STATUS_CATALOG_PATH);
        }

        if (_hadOriginalStatusCatalog && File.Exists(_backupStatusCatalogAbsolutePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_statusCatalogAbsolutePath));
            File.Copy(_backupStatusCatalogAbsolutePath, _statusCatalogAbsolutePath, true);
            if (File.Exists(_backupStatusCatalogMetaAbsolutePath))
            {
                File.Copy(_backupStatusCatalogMetaAbsolutePath, _statusCatalogMetaAbsolutePath, true);
            }
        }

        AssetDatabase.Refresh();
    }

    [Test]
    public void TestInvalidModifiersJsonFailsValidation()
    {
        string[] statusLines =
        {
            @"{""id"":""Freeze"",""displayName"":""Freeze"",""defaultDuration"":1,""stackRule"":3,""maxStacks"":1,""modifiersJson"":""[]""}"
        };

        string cdbPath = CreateTestCdb("test_status_invalid_modifiers.cdb", "0.4", "Status", "", statusLines);

        var provider = new StatusDataProvider();
        var source = new CastleDbFileSource(cdbPath);
        var descriptor = CreateDescriptorFromFile(source, cdbPath);

        provider.Initialize(source, descriptor);
        var errors = provider.Validate(descriptor);

        Assert.IsTrue(errors.Any(e => e.Contains("modifiersJson")), "modifiersJson 非对象时应报错");
    }

    [Test]
    public void TestImportCreatesStatusCatalog()
    {
        string[] statusLines =
        {
            "{\"id\":\"Freeze\",\"displayName\":\"Freeze\",\"defaultDuration\":1,\"stackRule\":3,\"maxStacks\":1,\"modifiersJson\":\"{\\\"moveSpeedMultiplier\\\":0.2}\"}",
            "{\"id\":\"Slow\",\"displayName\":\"Slow\",\"defaultDuration\":5,\"stackRule\":1,\"maxStacks\":3,\"modifiersJson\":\"{\\\"moveSpeedMultiplier\\\":0.5}\"}"
        };

        string cdbPath = CreateTestCdb("test_status_import.cdb", "0.4", "Status", "", statusLines);

        var provider = new StatusDataProvider();
        Assert.IsTrue(TryImport(provider, cdbPath), "导入应成功");

        var catalog = AssetDatabase.LoadAssetAtPath<StatusCatalog>(STATUS_CATALOG_PATH);
        Assert.IsNotNull(catalog, "应生成 StatusCatalog.asset");
        Assert.IsTrue(catalog.IsValid, "StatusCatalog 应有效");
        Assert.AreEqual(2, catalog.statuses.Length, "应生成 2 个状态条目");
    }

    // ===== helpers =====

    private string CreateTestCdb(string filename, string schemaVersion, string providerId, string dependencies, string[] statusLines)
    {
        string cdbPath = $"{TEST_CDB_DIR}/{filename}";
        string linesJson = string.Join(",\n\t\t\t\t", statusLines);

        string cdbContent = $@"{{
	""sheets"": [
		{{
			""name"": ""Status"",
			""columns"": [
				{{""typeStr"": ""0"", ""name"": ""id""}},
				{{""typeStr"": ""1"", ""name"": ""displayName""}},
				{{""typeStr"": ""4"", ""name"": ""defaultDuration""}},
				{{""typeStr"": ""5:Refresh,Add,Ignore,Replace"", ""name"": ""stackRule""}},
				{{""typeStr"": ""3"", ""name"": ""maxStacks""}},
				{{""typeStr"": ""1"", ""name"": ""modifiersJson""}}
			],
			""lines"": [
				{linesJson}
			]
		}},
		{{
			""name"": ""Meta"",
			""columns"": [
				{{""typeStr"": ""1"", ""name"": ""key""}},
				{{""typeStr"": ""1"", ""name"": ""value""}}
			],
			""lines"": [
				{{""key"": ""schemaVersion"", ""value"": ""{schemaVersion}""}},
				{{""key"": ""providerId"", ""value"": ""{providerId}""}},
				{{""key"": ""dependencies"", ""value"": ""{dependencies}""}},
				{{""key"": ""resourcePath"", ""value"": ""Config/Status""}}
			]
		}}
	]
}}";

        File.WriteAllText(cdbPath, cdbContent);
        AssetDatabase.Refresh();
        return cdbPath;
    }

    private static CdbModuleDescriptor CreateDescriptorFromFile(CastleDbFileSource source, string cdbPath)
    {
        var root = source.ReadCastleDbJson();
        var metaSheet = root.sheets?.FirstOrDefault(s => s.name == "Meta");
        var metaEntries = metaSheet.lines?
            .OfType<Dictionary<string, object>>()
            .Select(d => new MetaEntry
            {
                key = d.TryGetValue("key", out var k) ? k?.ToString() ?? "" : "",
                value = d.TryGetValue("value", out var v) ? v?.ToString() ?? "" : ""
            })
            .ToList() ?? new List<MetaEntry>();

        return CdbModuleDescriptor.FromMetaEntries(metaEntries, cdbPath);
    }

    private bool TryImport(StatusDataProvider provider, string cdbPath)
    {
        try
        {
            var source = new CastleDbFileSource(cdbPath);
            var descriptor = CreateDescriptorFromFile(source, cdbPath);

            provider.Initialize(source, descriptor);
            var errors = provider.Validate(descriptor);
            if (errors.Count > 0)
            {
                foreach (var error in errors)
                {
                    Debug.LogError($"Validation Error: {error}");
                }
                return false;
            }

            var result = provider.Import(descriptor);
            if (!result.Success)
            {
                foreach (var error in result.Errors)
                {
                    Debug.LogError($"Import Error: {error}");
                }
                return false;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return true;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Import 捕获异常: {ex.Message}\n{ex.StackTrace}");
            return false;
        }
    }
}
