using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using CastleDB.Runtime;

namespace CastleDB.Tests.EditMode
{
    /// <summary>
    /// CdbDataProviderRegistry 单元测试（0.3 版本 Phase 1）
    /// 测试 Provider 注册、拓扑排序、依赖管理等功能
    /// </summary>
    public class CdbDataProviderRegistryTests
    {
        private CdbDataProviderRegistry _registry;

        [SetUp]
        public void Setup()
        {
            _registry = new CdbDataProviderRegistry();
        }

        [TearDown]
        public void Teardown()
        {
            _registry.Reset();
            _registry = null;
        }

        #region Provider 注册测试

        [Test]
        public void Register_ValidProvider_ReturnsTrue()
        {
            // Arrange
            var provider = new MockDataProvider("TestProvider");

            // Act
            var result = _registry.Register(provider);

            // Assert
            Assert.IsTrue(result);
            Assert.IsTrue(_registry.IsRegistered("TestProvider"));
        }

        [Test]
        public void Register_NullProvider_ReturnsFalse()
        {
            // Act
            var result = _registry.Register(null);

            // Assert
            Assert.IsFalse(result);
        }

        [Test]
        public void Register_DuplicateProvider_OverwritesAndReturnsTrue()
        {
            // Arrange
            var provider1 = new MockDataProvider("TestProvider");
            var provider2 = new MockDataProvider("TestProvider");

            // Act
            _registry.Register(provider1);
            var result = _registry.Register(provider2);

            // Assert
            Assert.IsTrue(result);
            Assert.AreSame(provider2, _registry.GetProvider("TestProvider"));
        }

        [Test]
        public void GetProvider_Registered_ReturnsProvider()
        {
            // Arrange
            var provider = new MockDataProvider("TestProvider");
            _registry.Register(provider);

            // Act
            var result = _registry.GetProvider("TestProvider");

            // Assert
            Assert.IsNotNull(result);
            Assert.AreSame(provider, result);
        }

        [Test]
        public void GetProvider_NotRegistered_ReturnsNull()
        {
            // Act
            var result = _registry.GetProvider("NonExistent");

            // Assert
            Assert.IsNull(result);
        }

        [Test]
        public void Unregister_Registered_ReturnsTrue()
        {
            // Arrange
            var provider = new MockDataProvider("TestProvider");
            _registry.Register(provider);

            // Act
            var result = _registry.Unregister("TestProvider");

            // Assert
            Assert.IsTrue(result);
            Assert.IsFalse(_registry.IsRegistered("TestProvider"));
        }

        [Test]
        public void Unregister_NotRegistered_ReturnsFalse()
        {
            // Act
            var result = _registry.Unregister("NonExistent");

            // Assert
            Assert.IsFalse(result);
        }

        #endregion

        #region Descriptor 测试

        [Test]
        public void RegisterDescriptor_Valid_CanBeRetrieved()
        {
            // Arrange
            var descriptor = CreateDescriptor("Monster", new string[] { });

            // Act
            _registry.RegisterDescriptor(descriptor);
            var result = _registry.GetDescriptor("Monster");

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual("Monster", result.ProviderId);
        }

        [Test]
        public void ClearDescriptors_RemovesAll()
        {
            // Arrange
            _registry.RegisterDescriptor(CreateDescriptor("Monster", new string[] { }));
            _registry.RegisterDescriptor(CreateDescriptor("Player", new string[] { }));

            // Act
            _registry.ClearDescriptors();

            // Assert
            Assert.IsNull(_registry.GetDescriptor("Monster"));
            Assert.IsNull(_registry.GetDescriptor("Player"));
        }

        #endregion

        #region 拓扑排序测试

        [Test]
        public void TopologicalSort_NoDependencies_ReturnsOriginalOrder()
        {
            // Arrange
            _registry.RegisterDescriptor(CreateDescriptor("A", new string[] { }));
            _registry.RegisterDescriptor(CreateDescriptor("B", new string[] { }));
            _registry.RegisterDescriptor(CreateDescriptor("C", new string[] { }));

            // Act
            var result = _registry.TopologicalSort(new[] { "A", "B", "C" });

            // Assert
            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(3, result.SortedIds.Count);
        }

        [Test]
        public void TopologicalSort_LinearDependencies_ReturnsCorrectOrder()
        {
            // Arrange: C → B → A（A 无依赖，B 依赖 A，C 依赖 B）
            _registry.RegisterDescriptor(CreateDescriptor("A", new string[] { }));
            _registry.RegisterDescriptor(CreateDescriptor("B", new[] { "A" }));
            _registry.RegisterDescriptor(CreateDescriptor("C", new[] { "B" }));

            // Act
            var result = _registry.TopologicalSort(new[] { "C", "B", "A" });

            // Assert
            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(3, result.SortedIds.Count);
            // A 应该在 B 之前，B 应该在 C 之前
            var sortedList = result.SortedIds.ToList();
            var aIndex = sortedList.IndexOf("A");
            var bIndex = sortedList.IndexOf("B");
            var cIndex = sortedList.IndexOf("C");
            Assert.Less(aIndex, bIndex, "A should come before B");
            Assert.Less(bIndex, cIndex, "B should come before C");
        }

        [Test]
        public void TopologicalSort_DiamondDependencies_ReturnsValidOrder()
        {
            // Arrange: D 依赖 B 和 C，B 和 C 都依赖 A
            //     A
            //    / \
            //   B   C
            //    \ /
            //     D
            _registry.RegisterDescriptor(CreateDescriptor("A", new string[] { }));
            _registry.RegisterDescriptor(CreateDescriptor("B", new[] { "A" }));
            _registry.RegisterDescriptor(CreateDescriptor("C", new[] { "A" }));
            _registry.RegisterDescriptor(CreateDescriptor("D", new[] { "B", "C" }));

            // Act
            var result = _registry.TopologicalSort(new[] { "D", "C", "B", "A" });

            // Assert
            Assert.IsTrue(result.IsSuccess);
            Assert.AreEqual(4, result.SortedIds.Count);
            // A 应该在 B 和 C 之前
            var sortedList = result.SortedIds.ToList();
            var aIndex = sortedList.IndexOf("A");
            var bIndex = sortedList.IndexOf("B");
            var cIndex = sortedList.IndexOf("C");
            var dIndex = sortedList.IndexOf("D");
            Assert.Less(aIndex, bIndex);
            Assert.Less(aIndex, cIndex);
            Assert.Less(bIndex, dIndex);
            Assert.Less(cIndex, dIndex);
        }

        [Test]
        public void TopologicalSort_CyclicDependency_ReturnsFailed()
        {
            // Arrange: A → B → C → A（循环）
            _registry.RegisterDescriptor(CreateDescriptor("A", new[] { "C" }));
            _registry.RegisterDescriptor(CreateDescriptor("B", new[] { "A" }));
            _registry.RegisterDescriptor(CreateDescriptor("C", new[] { "B" }));

            // Act
            var result = _registry.TopologicalSort(new[] { "A", "B", "C" });

            // Assert
            Assert.IsFalse(result.IsSuccess);
            Assert.Greater(result.CyclePath.Count, 0);
        }

        [Test]
        public void TopologicalSort_SelfDependency_ReturnsFailed()
        {
            // Arrange: A 依赖自己
            _registry.RegisterDescriptor(CreateDescriptor("A", new[] { "A" }));

            // Act
            var result = _registry.TopologicalSort(new[] { "A" });

            // Assert
            Assert.IsFalse(result.IsSuccess);
        }

        #endregion

        #region 依赖链测试

        [Test]
        public void GetDependencyChain_NoDependencies_ReturnsEmpty()
        {
            // Arrange
            _registry.RegisterDescriptor(CreateDescriptor("A", new string[] { }));

            // Act
            var chain = _registry.GetDependencyChain("A");

            // Assert
            Assert.AreEqual(0, chain.Count);
        }

        [Test]
        public void GetDependencyChain_SingleDependency_ReturnsDependency()
        {
            // Arrange
            _registry.RegisterDescriptor(CreateDescriptor("A", new string[] { }));
            _registry.RegisterDescriptor(CreateDescriptor("B", new[] { "A" }));

            // Act
            var chain = _registry.GetDependencyChain("B");

            // Assert
            Assert.AreEqual(1, chain.Count);
            Assert.Contains("A", chain);
        }

        [Test]
        public void GetDependencyChain_TransitiveDependencies_ReturnsAll()
        {
            // Arrange: C → B → A
            _registry.RegisterDescriptor(CreateDescriptor("A", new string[] { }));
            _registry.RegisterDescriptor(CreateDescriptor("B", new[] { "A" }));
            _registry.RegisterDescriptor(CreateDescriptor("C", new[] { "B" }));

            // Act
            var chain = _registry.GetDependencyChain("C");

            // Assert
            Assert.AreEqual(2, chain.Count);
            Assert.Contains("A", chain);
            Assert.Contains("B", chain);
            // A 应该在 B 之前
            var aIndex = chain.IndexOf("A");
            var bIndex = chain.IndexOf("B");
            Assert.Less(aIndex, bIndex);
        }

        #endregion

        #region 版本校验测试

        [Test]
        public void ValidateSchemaVersions_AllMatch_ReturnsEmpty()
        {
            // Arrange
            _registry.RegisterDescriptor(CreateDescriptor("A", new string[] { }, "0.3"));
            _registry.RegisterDescriptor(CreateDescriptor("B", new string[] { }, "0.3"));

            // Act
            var mismatched = _registry.ValidateSchemaVersions();

            // Assert
            Assert.AreEqual(0, mismatched.Count);
        }

        [Test]
        public void ValidateSchemaVersions_Mismatch_ReturnsMismatched()
        {
            // Arrange
            _registry.RegisterDescriptor(CreateDescriptor("A", new string[] { }, "0.3"));
            _registry.RegisterDescriptor(CreateDescriptor("B", new string[] { }, "0.2"));

            // Act
            var mismatched = _registry.ValidateSchemaVersions();

            // Assert
            Assert.AreEqual(1, mismatched.Count);
            Assert.IsTrue(mismatched.ContainsKey("B"));
            Assert.AreEqual("0.2", mismatched["B"]);
        }

        #endregion

        #region 依赖校验测试

        [Test]
        public void ValidateDependencies_AllExist_ReturnsEmpty()
        {
            // Arrange
            _registry.RegisterDescriptor(CreateDescriptor("A", new string[] { }));
            _registry.RegisterDescriptor(CreateDescriptor("B", new[] { "A" }));

            // Act
            var missing = _registry.ValidateDependencies();

            // Assert
            Assert.AreEqual(0, missing.Count);
        }

        [Test]
        public void ValidateDependencies_Missing_ReturnsMissing()
        {
            // Arrange
            _registry.RegisterDescriptor(CreateDescriptor("B", new[] { "A", "C" }));

            // Act
            var missing = _registry.ValidateDependencies();

            // Assert
            Assert.AreEqual(1, missing.Count);
            Assert.IsTrue(missing.ContainsKey("B"));
            Assert.AreEqual(2, missing["B"].Count);
            Assert.Contains("A", missing["B"]);
            Assert.Contains("C", missing["B"]);
        }

        #endregion

        #region 统计测试

        [Test]
        public void GetStats_ReturnsCorrectCounts()
        {
            // Arrange
            var provider1 = new MockDataProvider("A");
            var provider2 = new MockDataProvider("B");
            provider1.SetInitialized(true);

            _registry.Register(provider1);
            _registry.Register(provider2);
            _registry.RegisterDescriptor(CreateDescriptor("A", new string[] { }));
            _registry.RegisterDescriptor(CreateDescriptor("B", new string[] { }));
            _registry.RegisterDescriptor(CreateDescriptor("C", new string[] { }));

            // Act
            var stats = _registry.GetStats();

            // Assert
            Assert.AreEqual(2, stats.RegisteredCount);
            Assert.AreEqual(1, stats.InitializedCount);
            Assert.AreEqual(3, stats.DescriptorCount);
        }

        #endregion

        #region Helper Methods

        private CdbModuleDescriptor CreateDescriptor(string providerId, string[] dependencies, string schemaVersion = "0.3")
        {
            var metaEntries = new List<MetaEntry>
            {
                new MetaEntry { key = "providerId", value = providerId },
                new MetaEntry { key = "schemaVersion", value = schemaVersion },
                new MetaEntry { key = "displayName", value = providerId },
                new MetaEntry { key = "resourcePath", value = $"Data/{providerId}" },
                new MetaEntry { key = "dependencies", value = string.Join(",", dependencies) }
            };
            return CdbModuleDescriptor.FromMetaEntries(metaEntries, $"Assets/Resources/Data/{providerId}.cdb");
        }

        #endregion
    }

    #region Mock Classes

    /// <summary>
    /// Mock Provider 用于测试
    /// </summary>
    public class MockDataProvider : ICdbDataProvider
    {
        private readonly string _providerId;
        private bool _initialized;

        public MockDataProvider(string providerId)
        {
            _providerId = providerId;
        }

        public string ExpectedProviderId => _providerId;
        public bool IsInitialized => _initialized;

        public void SetInitialized(bool value) => _initialized = value;

        public void Initialize(ICastleDbSource source, CdbModuleDescriptor descriptor)
        {
            _initialized = true;
        }

        public List<string> Validate(CdbModuleDescriptor descriptor)
        {
            return new List<string>();
        }

#if UNITY_EDITOR
        public CdbImportResult Import(CdbModuleDescriptor descriptor)
        {
            return CdbImportResult.Succeeded(_providerId);
        }
#endif

        public List<T> GetAllEntries<T>() where T : class
        {
            return new List<T>();
        }

        public T GetEntryById<T>(string id) where T : class
        {
            return null;
        }

        public void Reset()
        {
            _initialized = false;
        }
    }

    #endregion
}
