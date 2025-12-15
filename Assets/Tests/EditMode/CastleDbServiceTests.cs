using NUnit.Framework;
using System.Collections.Generic;
using CastleDB.Runtime;
using UnityEngine;
using UnityEngine.TestTools;

namespace CastleDB.Tests.EditMode
{
    /// <summary>
    /// CastleDbService 单元测试
    /// 测试 DTO 解析、版本检查、数据查询等功能
    /// </summary>
    public class CastleDbServiceTests
    {
        private CastleDbService _service;
        private MockCastleDbSource _mockSource;

        [SetUp]
        public void Setup()
        {
            _service = new CastleDbService();
            _mockSource = new MockCastleDbSource();
        }

        [TearDown]
        public void Teardown()
        {
            _service = null;
            _mockSource = null;
        }

        /// <summary>
        /// 测试：成功加载有效的 CastleDB 数据
        /// </summary>
        [Test]
        public void TestInitializeWithValidData()
        {
            // Arrange
            _mockSource.SetupValidData();

            // Act
            _service.Initialize(_mockSource);

            // Assert
            Assert.IsNotNull(_service.GetVersionInfo());
            Assert.AreEqual("0.2", _service.GetVersionInfo().schemaVersion);
            Assert.Greater(_service.GetAllNpcs().Count, 0);
        }

        /// <summary>
        /// 测试：版本不匹配时拒绝加载
        /// </summary>
        [Test]
        public void TestInitializeWithVersionMismatch()
        {
            // Arrange
            _mockSource.SetupVersionMismatch();
            bool versionMismatchCalled = false;

            // Act
            _service.OnVersionMismatch += (msg) => versionMismatchCalled = true;
            LogAssert.Expect(LogType.Error, "[CastleDbService] Schema 版本不匹配！期望 0.2，实际 0.1");
            _service.Initialize(_mockSource);

            // Assert
            Assert.IsTrue(versionMismatchCalled);
        }

        /// <summary>
        /// 测试：按 ID 查询 NPC
        /// </summary>
        [Test]
        public void TestGetNpcById()
        {
            // Arrange
            _mockSource.SetupValidData();
            _service.Initialize(_mockSource);

            // Act
            var npc = _service.GetNpcById("M_Knight");

            // Assert
            Assert.IsNotNull(npc);
            Assert.AreEqual("M_Knight", npc.id);
            Assert.AreEqual("Knight", npc.displayName);
            Assert.AreEqual(100, npc.maxHealth);
        }

        /// <summary>
        /// 测试：查询不存在的 NPC 返回 null
        /// </summary>
        [Test]
        public void TestGetNpcByIdNotFound()
        {
            // Arrange
            _mockSource.SetupValidData();
            _service.Initialize(_mockSource);

            // Act
            var npc = _service.GetNpcById("NonExistent");

            // Assert
            Assert.IsNull(npc);
        }

        /// <summary>
        /// 测试：获取所有 NPC
        /// </summary>
        [Test]
        public void TestGetAllNpcs()
        {
            // Arrange
            _mockSource.SetupValidData();
            _service.Initialize(_mockSource);

            // Act
            var npcs = _service.GetAllNpcs();

            // Assert
            Assert.AreEqual(2, npcs.Count);
            Assert.AreEqual("M_Knight", npcs[0].id);
            Assert.AreEqual("M_FlyingEye", npcs[1].id);
        }

        /// <summary>
        /// 测试：按 NPC ID 获取检测区
        /// </summary>
        [Test]
        public void TestGetDetectionZonesByNpcId()
        {
            // Arrange
            _mockSource.SetupValidData();
            _service.Initialize(_mockSource);

            // Act
            var zones = _service.GetDetectionZonesByNpcId("M_Knight");

            // Assert
            Assert.AreEqual(1, zones.Count);
            Assert.AreEqual("M_Knight", zones[0].npcId);
            Assert.AreEqual(0, zones[0].role); // PrimaryAttack
            Assert.AreEqual("HitboxDecetion", zones[0].childId);
        }

        /// <summary>
        /// 测试：获取所有检测区
        /// </summary>
        [Test]
        public void TestGetAllDetectionZones()
        {
            // Arrange
            _mockSource.SetupValidData();
            _service.Initialize(_mockSource);

            // Act
            var zones = _service.GetAllDetectionZones();

            // Assert
            Assert.AreEqual(2, zones.Count);
        }

        /// <summary>
        /// 测试：版本信息正确解析
        /// </summary>
        [Test]
        public void TestVersionInfoParsing()
        {
            // Arrange
            _mockSource.SetupValidData();
            _service.Initialize(_mockSource);

            // Act
            var versionInfo = _service.GetVersionInfo();

            // Assert
            Assert.AreEqual("0.2", versionInfo.schemaVersion);
            Assert.AreEqual("UseNewDamageable=true", versionInfo.featureFlags);
        }

        /// <summary>
        /// 测试：NPC 数据完整性
        /// </summary>
        [Test]
        public void TestNpcDataIntegrity()
        {
            // Arrange
            _mockSource.SetupValidData();
            _service.Initialize(_mockSource);

            // Act
            var knight = _service.GetNpcById("M_Knight");

            // Assert
            Assert.AreEqual("Knight", knight.displayName);
            Assert.AreEqual("KnightEnemy", knight.prefabName);
            Assert.AreEqual("hasTarget", knight.animationTrigger);
            Assert.AreEqual(100, knight.maxHealth);
            Assert.AreEqual(20, knight.attackDamage);
            Assert.AreEqual(3, knight.moveSpeed);
            Assert.AreEqual(10, knight.attackRange);
            Assert.AreEqual(0.25f, knight.attackCooldown);
            Assert.AreEqual(3, knight.invincibleDuration);
            Assert.AreEqual(2, knight.knockbackMultiplier);
            Assert.IsTrue(knight.enableDeathAnimation);
            Assert.IsTrue(knight.useLegacyLogicFallback);
        }
    }

    /// <summary>
    /// 模拟 CastleDB 数据源
    /// 用于单元测试
    /// 填充 lines 为 Dictionary<string, object> 列表，与真实加载路径一致
    /// </summary>
    public class MockCastleDbSource : ICastleDbSource
    {
        private CastleDbRoot _root;

        public void SetupValidData()
        {
            _root = new CastleDbRoot();

            // 创建 NPC Sheet，填充 lines 为字典列表
            var npcSheet = new SheetData { name = "NPC" };
            npcSheet.lines = new List<object>
            {
                new Dictionary<string, object>
                {
                    { "id", "M_Knight" },
                    { "displayName", "Knight" },
                    { "prefabName", "KnightEnemy" },
                    { "animationTrigger", "hasTarget" },
                    { "maxHealth", 100.0 },
                    { "attackDamage", 20.0 },
                    { "moveSpeed", 3.0 },
                    { "attackRange", 10.0 },
                    { "attackCooldown", 0.25 },
                    { "invincibleDuration", 3.0 },
                    { "knockbackMultiplier", 2.0 },
                    { "enableDeathAnimation", true },
                    { "useLegacyLogicFallback", true }
                },
                new Dictionary<string, object>
                {
                    { "id", "M_FlyingEye" },
                    { "displayName", "FlyingEye" },
                    { "prefabName", "FlyingEye" },
                    { "animationTrigger", "hasTarget" },
                    { "maxHealth", 50.0 },
                    { "attackDamage", 30.0 },
                    { "moveSpeed", 4.0 },
                    { "attackRange", 10.0 },
                    { "attackCooldown", 0.25 },
                    { "invincibleDuration", 3.0 },
                    { "knockbackMultiplier", 2.0 },
                    { "enableDeathAnimation", true },
                    { "useLegacyLogicFallback", true }
                }
            };

            // 创建 DetectionZone Sheet，填充 lines 为字典列表
            var dzSheet = new SheetData { name = "DetectionZone" };
            dzSheet.lines = new List<object>
            {
                new Dictionary<string, object>
                {
                    { "id", "M_Knight_DZ" },
                    { "npcId", "M_Knight" },
                    { "role", 0 }, // PrimaryAttack
                    { "childId", "HitboxDecetion" }
                },
                new Dictionary<string, object>
                {
                    { "id", "M_FlyingEye_DZ" },
                    { "npcId", "M_FlyingEye" },
                    { "role", 0 }, // PrimaryAttack
                    { "childId", "AttackDetectionZone" }
                }
            };

            // 创建 Meta Sheet，填充 lines 为字典列表
            var metaSheet = new SheetData { name = "Meta" };
            metaSheet.lines = new List<object>
            {
                new Dictionary<string, object>
                {
                    { "key", "schemaVersion" },
                    { "value", "0.2" }
                },
                new Dictionary<string, object>
                {
                    { "key", "featureFlags" },
                    { "value", "UseNewDamageable=true" }
                }
            };

            _root.sheets.Add(npcSheet);
            _root.sheets.Add(dzSheet);
            _root.sheets.Add(metaSheet);
        }

        public void SetupVersionMismatch()
        {
            _root = new CastleDbRoot();

            var metaSheet = new SheetData { name = "Meta" };
            metaSheet.lines = new List<object>
            {
                new Dictionary<string, object>
                {
                    { "key", "schemaVersion" },
                    { "value", "0.1" } // 版本不匹配
                }
            };

            _root.sheets.Add(metaSheet);
        }

        public CastleDbRoot ReadCastleDbJson()
        {
            return _root;
        }

        public string GetSourceDescription()
        {
            return "MockCastleDbSource";
        }
    }
}
