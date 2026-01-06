using NUnit.Framework;
using System.Collections.Generic;
using CastleDB.Runtime;
using CastleDB.Tests.EditMode.TestHelpers;
using UnityEngine;
using UnityEngine.TestTools;

namespace CastleDB.Tests.EditMode
{
    /// <summary>
        /// CastleDbService 单元测试
        /// 覆盖 DTO 解析、版本校验、数据查询等功能
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
        /// 测试：成功初始化有效的 CastleDB 数据
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
            Assert.AreEqual(CdbDataProviderRegistry.ExpectedSchemaVersion, _service.GetVersionInfo().schemaVersion);
            Assert.Greater(_service.GetAllNpcs().Count, 0);
        }

        /// <summary>
        /// 测试：版本不匹配时拒绝初始化
        /// </summary>
        [Test]
        public void TestInitializeWithVersionMismatch()
        {
            // Arrange
            _mockSource.SetupVersionMismatch();
            bool versionMismatchCalled = false;

            // Act
            _service.OnVersionMismatch += (msg) => versionMismatchCalled = true;
            LogAssert.Expect(LogType.Error, $"[CastleDbService] Schema 版本不匹配！期望 {CdbDataProviderRegistry.ExpectedSchemaVersion}，实际 0.1");
            _service.Initialize(_mockSource);

            // Assert
            Assert.IsTrue(versionMismatchCalled);
        }

        /// <summary>
        /// 测试：通过 ID 查询 NPC
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
        /// 测试：通过 NPC ID 获取检测区
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
        /// 测试：版本信息解析正确
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
            Assert.AreEqual(CdbDataProviderRegistry.ExpectedSchemaVersion, versionInfo.schemaVersion);
            Assert.AreEqual("UseNewDamageable=true", versionInfo.featureFlags);
        }

        /// <summary>
        /// 测试：NPC 数据完整性断言
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
}

