using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using CastleDB.Runtime;
using CastleDB.Editor.Providers;
using CastleDB.Tests.EditMode.TestHelpers;

namespace CastleDB.Tests.EditMode
{
    /// <summary>
    /// MonsterDataProvider 集成测试（0.3 版本 Phase 2）
    /// 测试 Provider 的初始化、校验、查询功能
    /// </summary>
    public class MonsterDataProviderTests
    {
        private MonsterDataProvider _provider;
        private CdbDataProviderRegistry _registry;

        [SetUp]
        public void Setup()
        {
            _provider = new MonsterDataProvider();
            _registry = new CdbDataProviderRegistry();
            _registry.Register(_provider);
        }

        [TearDown]
        public void Teardown()
        {
            _provider?.Reset();
            _registry?.Reset();
            _provider = null;
            _registry = null;
        }

        #region 基础属性测试

        [Test]
        public void ExpectedProviderId_ReturnsMonster()
        {
            Assert.AreEqual("Monster", _provider.ExpectedProviderId);
        }

        [Test]
        public void IsInitialized_BeforeInit_ReturnsFalse()
        {
            Assert.IsFalse(_provider.IsInitialized);
        }

        #endregion

        #region 初始化测试

        [Test]
        public void Initialize_ValidSource_SetsInitializedTrue()
        {
            // Arrange
            var source = CreateMockSource();
            var descriptor = CreateDescriptor("Monster");

            // Act
            _provider.Initialize(source, descriptor);

            // Assert
            Assert.IsTrue(_provider.IsInitialized);
        }

        [Test]
        public void Initialize_MismatchedProviderId_ThrowsException()
        {
            // Arrange
            var source = CreateMockSource();
            var descriptor = CreateDescriptor("WrongProvider");

            // Act & Assert
            Assert.Throws<System.InvalidOperationException>(() =>
            {
                _provider.Initialize(source, descriptor);
            });
        }

        [Test]
        public void Initialize_ParsesNpcData()
        {
            // Arrange
            var source = CreateMockSource();
            var descriptor = CreateDescriptor("Monster");

            // Act
            _provider.Initialize(source, descriptor);

            // Assert
            var npcs = _provider.GetAllNpcs();
            Assert.AreEqual(2, npcs.Count);
            Assert.AreEqual("M_Knight", npcs[0].id);
            Assert.AreEqual("M_FlyingEye", npcs[1].id);
        }

        [Test]
        public void Initialize_ParsesDetectionZoneData()
        {
            // Arrange
            var source = CreateMockSource();
            var descriptor = CreateDescriptor("Monster");

            // Act
            _provider.Initialize(source, descriptor);

            // Assert
            var zones = _provider.GetAllDetectionZones();
            Assert.AreEqual(2, zones.Count);
            Assert.AreEqual("M_Knight", zones[0].npcId);
        }

        #endregion

        #region 校验测试

        [Test]
        public void Validate_ValidData_ReturnsNoErrors()
        {
            // Arrange
            var source = CreateMockSource();
            var descriptor = CreateDescriptor("Monster");
            _provider.Initialize(source, descriptor);

            // Act
            var errors = _provider.Validate(descriptor);

            // Assert
            Assert.AreEqual(0, errors.Count);
        }

        [Test]
        public void Validate_InvalidNpc_ReturnsErrors()
        {
            // Arrange
            var source = CreateMockSourceWithInvalidNpc();
            var descriptor = CreateDescriptor("Monster");
            _provider.Initialize(source, descriptor);

            // Act
            var errors = _provider.Validate(descriptor);

            // Assert
            Assert.Greater(errors.Count, 0);
            Assert.IsTrue(errors.Any(e => e.Contains("maxHealth")));
        }

        [Test]
        public void Validate_InvalidDetectionZone_ReturnsErrors()
        {
            // Arrange
            var source = CreateMockSourceWithInvalidZone();
            var descriptor = CreateDescriptor("Monster");
            _provider.Initialize(source, descriptor);

            // Act
            var errors = _provider.Validate(descriptor);

            // Assert
            Assert.Greater(errors.Count, 0);
            Assert.IsTrue(errors.Any(e => e.Contains("npcId") || e.Contains("不存在")));
        }

        [Test]
        public void Validate_NotInitialized_ReturnsError()
        {
            // Arrange
            var descriptor = CreateDescriptor("Monster");

            // Act
            var errors = _provider.Validate(descriptor);

            // Assert
            Assert.AreEqual(1, errors.Count);
            Assert.IsTrue(errors[0].Contains("未初始化"));
        }

        #endregion

        #region 查询测试

        [Test]
        public void GetAllEntries_NpcEntry_ReturnsNpcs()
        {
            // Arrange
            var source = CreateMockSource();
            var descriptor = CreateDescriptor("Monster");
            _provider.Initialize(source, descriptor);

            // Act
            var npcs = _provider.GetAllEntries<NpcEntry>();

            // Assert
            Assert.AreEqual(2, npcs.Count);
        }

        [Test]
        public void GetAllEntries_DetectionZoneEntry_ReturnsZones()
        {
            // Arrange
            var source = CreateMockSource();
            var descriptor = CreateDescriptor("Monster");
            _provider.Initialize(source, descriptor);

            // Act
            var zones = _provider.GetAllEntries<DetectionZoneEntry>();

            // Assert
            Assert.AreEqual(2, zones.Count);
        }

        [Test]
        public void GetAllEntries_UnsupportedType_ReturnsEmpty()
        {
            // Arrange
            var source = CreateMockSource();
            var descriptor = CreateDescriptor("Monster");
            _provider.Initialize(source, descriptor);

            // Act
            var entries = _provider.GetAllEntries<PlayerEntry>();

            // Assert
            Assert.AreEqual(0, entries.Count);
        }

        [Test]
        public void GetEntryById_ExistingNpc_ReturnsNpc()
        {
            // Arrange
            var source = CreateMockSource();
            var descriptor = CreateDescriptor("Monster");
            _provider.Initialize(source, descriptor);

            // Act
            var npc = _provider.GetEntryById<NpcEntry>("M_Knight");

            // Assert
            Assert.IsNotNull(npc);
            Assert.AreEqual("M_Knight", npc.id);
            Assert.AreEqual("Knight", npc.displayName);
        }

        [Test]
        public void GetEntryById_NonExistingNpc_ReturnsNull()
        {
            // Arrange
            var source = CreateMockSource();
            var descriptor = CreateDescriptor("Monster");
            _provider.Initialize(source, descriptor);

            // Act
            var npc = _provider.GetEntryById<NpcEntry>("NonExistent");

            // Assert
            Assert.IsNull(npc);
        }

        [Test]
        public void GetDetectionZonesByNpcId_ExistingNpc_ReturnsZones()
        {
            // Arrange
            var source = CreateMockSource();
            var descriptor = CreateDescriptor("Monster");
            _provider.Initialize(source, descriptor);

            // Act
            var zones = _provider.GetDetectionZonesByNpcId("M_Knight");

            // Assert
            Assert.AreEqual(1, zones.Count);
            Assert.AreEqual("PrimaryAttack", zones[0].childId);
        }

        #endregion

        #region 重置测试

        [Test]
        public void Reset_ClearsData()
        {
            // Arrange
            var source = CreateMockSource();
            var descriptor = CreateDescriptor("Monster");
            _provider.Initialize(source, descriptor);

            // Act
            _provider.Reset();

            // Assert
            Assert.IsFalse(_provider.IsInitialized);
            Assert.AreEqual(0, _provider.GetAllNpcs().Count);
            Assert.AreEqual(0, _provider.GetAllDetectionZones().Count);
        }

        #endregion

        #region Registry 集成测试

        [Test]
        public void Registry_RegisterMonsterProvider_Works()
        {
            // Assert
            Assert.IsTrue(_registry.IsRegistered("Monster"));
            Assert.AreSame(_provider, _registry.GetProvider("Monster"));
        }

        #endregion

        #region Helper Methods

        private CdbModuleDescriptor CreateDescriptor(string providerId)
        {
            var metaEntries = new List<MetaEntry>
            {
                new MetaEntry { key = "providerId", value = providerId },
                new MetaEntry { key = "schemaVersion", value = "0.3" },
                new MetaEntry { key = "displayName", value = "怪物数据" },
                new MetaEntry { key = "resourcePath", value = "Data/MonsterSystem" },
                new MetaEntry { key = "dependencies", value = "" }
            };
            return CdbModuleDescriptor.FromMetaEntries(metaEntries, "Assets/Resources/Data/MonsterSystem.cdb");
        }

        /// <summary>
        /// 创建包含有效数据的 Mock Source
        /// </summary>
        private MockCastleDbSource CreateMockSource()
        {
            return new MockCastleDbSource(new CastleDbRoot
            {
                sheets = new List<SheetData>
                {
                    new SheetData
                    {
                        name = "NPC",
                        lines = new List<object>
                        {
                            new Dictionary<string, object>
                            {
                                { "id", "M_Knight" },
                                { "displayName", "Knight" },
                                { "faction", 1 },
                                { "prefabName", "Knight" },
                                { "animationTrigger", "Attack" },
                                { "maxHealth", 100f },
                                { "attackDamage", 20f },
                                { "moveSpeed", 3f },
                                { "attackRange", 1.5f },
                                { "attackCooldown", 1f },
                                { "invincibleDuration", 0.3f },
                                { "knockbackMultiplier", 1f },
                                { "knockbackToPlayer", 1f },
                                { "enableDeathAnimation", true },
                                { "useLegacyLogicFallback", false },
                                { "perceptionRadius", 5f }
                            },
                            new Dictionary<string, object>
                            {
                                { "id", "M_FlyingEye" },
                                { "displayName", "Flying Eye" },
                                { "faction", 1 },
                                { "prefabName", "FlyingEye" },
                                { "animationTrigger", "Attack" },
                                { "maxHealth", 50f },
                                { "attackDamage", 10f },
                                { "moveSpeed", 4f },
                                { "attackRange", 1f },
                                { "attackCooldown", 0.8f },
                                { "invincibleDuration", 0.2f },
                                { "knockbackMultiplier", 0.8f },
                                { "knockbackToPlayer", 0.5f },
                                { "enableDeathAnimation", true },
                                { "useLegacyLogicFallback", false },
                                { "perceptionRadius", 6f }
                            }
                        }
                    },
                    new SheetData
                    {
                        name = "DetectionZone",
                        lines = new List<object>
                        {
                            new Dictionary<string, object>
                            {
                                { "id", "Z_Knight_Primary" },
                                { "npcId", "M_Knight" },
                                { "role", 0 },
                                { "childId", "PrimaryAttack" }
                            },
                            new Dictionary<string, object>
                            {
                                { "id", "Z_FlyingEye_Primary" },
                                { "npcId", "M_FlyingEye" },
                                { "role", 0 },
                                { "childId", "PrimaryAttack" }
                            }
                        }
                    },
                    new SheetData
                    {
                        name = "Meta",
                        lines = new List<object>
                        {
                            new Dictionary<string, object> { { "key", "schemaVersion" }, { "value", "0.3" } },
                            new Dictionary<string, object> { { "key", "providerId" }, { "value", "Monster" } }
                        }
                    }
                }
            });
        }

        /// <summary>
        /// 创建包含无效 NPC 数据的 Mock Source
        /// </summary>
        private MockCastleDbSource CreateMockSourceWithInvalidNpc()
        {
            return new MockCastleDbSource(new CastleDbRoot
            {
                sheets = new List<SheetData>
                {
                    new SheetData
                    {
                        name = "NPC",
                        lines = new List<object>
                        {
                            new Dictionary<string, object>
                            {
                                { "id", "M_InvalidNpc" },
                                { "displayName", "Invalid" },
                                { "faction", 1 },
                                { "animationTrigger", "Attack" },
                                { "maxHealth", 0f },  // 无效：maxHealth <= 0
                                { "attackDamage", 10f },
                                { "moveSpeed", 3f },
                                { "attackRange", 1f },
                                { "attackCooldown", 1f }
                            }
                        }
                    }
                }
            });
        }

        /// <summary>
        /// 创建包含无效 DetectionZone 数据的 Mock Source
        /// </summary>
        private MockCastleDbSource CreateMockSourceWithInvalidZone()
        {
            return new MockCastleDbSource(new CastleDbRoot
            {
                sheets = new List<SheetData>
                {
                    new SheetData
                    {
                        name = "NPC",
                        lines = new List<object>
                        {
                            new Dictionary<string, object>
                            {
                                { "id", "M_Knight" },
                                { "displayName", "Knight" },
                                { "faction", 1 },
                                { "animationTrigger", "Attack" },
                                { "maxHealth", 100f },
                                { "attackDamage", 20f },
                                { "moveSpeed", 3f },
                                { "attackRange", 1f },
                                { "attackCooldown", 1f }
                            }
                        }
                    },
                    new SheetData
                    {
                        name = "DetectionZone",
                        lines = new List<object>
                        {
                            new Dictionary<string, object>
                            {
                                { "id", "Z_Invalid" },
                                { "npcId", "M_NonExistent" },  // 无效：引用不存在的 NPC
                                { "role", 0 },
                                { "childId", "Attack" }
                            }
                        }
                    }
                }
            });
        }

        #endregion
    }
}
