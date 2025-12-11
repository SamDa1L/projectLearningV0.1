using NUnit.Framework;
using System.Collections;
using UnityEngine;
using UnityEngine.TestTools;
using CastleDB.Runtime;

namespace CastleDB.Tests.PlayMode
{
    /// <summary>
    /// CastleDB 集成测试
    /// 在 PlayMode 中测试从文件加载和运行时使用
    /// </summary>
    public class CastleDbIntegrationTest
    {
        private CastleDbService _service;
        private TextAsset _castleDbAsset;

        [SetUp]
        public void Setup()
        {
            // 加载 CastleDB JSON 文件
            _castleDbAsset = Resources.Load<TextAsset>("Data/CastleDbDemo/MonsterSystem.cdb");
            Assert.IsNotNull(_castleDbAsset, "无法加载 CastleDB JSON 文件");

            // 初始化服务
            _service = new CastleDbService();
        }

        [TearDown]
        public void Teardown()
        {
            _service = null;
        }

        /// <summary>
        /// 测试：从 TextAsset 加载 CastleDB 数据
        /// </summary>
        [UnityTest]
        public IEnumerator TestLoadFromTextAsset()
        {
            // Arrange
            var source = new CastleDbJsonSource(_castleDbAsset);

            // Act
            _service.Initialize(source);
            yield return null;

            // Assert
            Assert.IsNotNull(_service.GetVersionInfo());
            Assert.AreEqual("0.2", _service.GetVersionInfo().schemaVersion);
            Debug.Log($"[CastleDbIntegrationTest] 成功加载 CastleDB，NPC 数量：{_service.GetAllNpcs().Count}");
        }

        /// <summary>
        /// 测试：查询 Knight NPC 数据
        /// </summary>
        [UnityTest]
        public IEnumerator TestQueryKnightData()
        {
            // Arrange
            var source = new CastleDbJsonSource(_castleDbAsset);
            _service.Initialize(source);
            yield return null;

            // Act
            var knight = _service.GetNpcById("M_Knight");

            // Assert
            Assert.IsNotNull(knight);
            Assert.AreEqual("Knight", knight.displayName);
            Assert.AreEqual(100, knight.maxHealth);
            Assert.AreEqual(3, knight.moveSpeed);
            Debug.Log($"[CastleDbIntegrationTest] Knight 数据：{knight}");
        }

        /// <summary>
        /// 测试：查询 FlyingEye NPC 数据
        /// </summary>
        [UnityTest]
        public IEnumerator TestQueryFlyingEyeData()
        {
            // Arrange
            var source = new CastleDbJsonSource(_castleDbAsset);
            _service.Initialize(source);
            yield return null;

            // Act
            var flyingEye = _service.GetNpcById("M_FlyingEye");

            // Assert
            Assert.IsNotNull(flyingEye);
            Assert.AreEqual("FlyingEye", flyingEye.displayName);
            Assert.AreEqual(50, flyingEye.maxHealth);
            Assert.AreEqual(4, flyingEye.moveSpeed);
            Debug.Log($"[CastleDbIntegrationTest] FlyingEye 数据：{flyingEye}");
        }

        /// <summary>
        /// 测试：查询检测区数据
        /// </summary>
        [UnityTest]
        public IEnumerator TestQueryDetectionZones()
        {
            // Arrange
            var source = new CastleDbJsonSource(_castleDbAsset);
            _service.Initialize(source);
            yield return null;

            // Act
            var knightZones = _service.GetDetectionZonesByNpcId("M_Knight");
            var allZones = _service.GetAllDetectionZones();

            // Assert
            Assert.AreEqual(1, knightZones.Count);
            Assert.AreEqual(2, allZones.Count);
            Assert.AreEqual("HitboxDecetion", knightZones[0].childId);
            Debug.Log($"[CastleDbIntegrationTest] Knight 检测区：{knightZones[0]}");
        }

        /// <summary>
        /// 测试：数据变更事件
        /// </summary>
        [UnityTest]
        public IEnumerator TestDataChangedEvent()
        {
            // Arrange
            var source = new CastleDbJsonSource(_castleDbAsset);
            bool eventCalled = false;
            _service.OnDataChanged += () => eventCalled = true;

            // Act
            _service.Initialize(source);
            yield return null;
            _service.Refresh();
            yield return null;

            // Assert
            Assert.IsTrue(eventCalled);
            Debug.Log("[CastleDbIntegrationTest] 数据变更事件已触发");
        }

        /// <summary>
        /// 测试：版本信息完整性
        /// </summary>
        [UnityTest]
        public IEnumerator TestVersionInfoIntegrity()
        {
            // Arrange
            var source = new CastleDbJsonSource(_castleDbAsset);
            _service.Initialize(source);
            yield return null;

            // Act
            var versionInfo = _service.GetVersionInfo();

            // Assert
            Assert.IsNotNull(versionInfo);
            Assert.AreEqual("0.2", versionInfo.schemaVersion);
            Assert.AreEqual("UseNewDamageable=true", versionInfo.featureFlags);
            Assert.IsNotNull(versionInfo.loadTime);
            Debug.Log($"[CastleDbIntegrationTest] 版本信息：{versionInfo}");
        }

        /// <summary>
        /// 测试：所有 NPC 数据完整性
        /// </summary>
        [UnityTest]
        public IEnumerator TestAllNpcsDataIntegrity()
        {
            // Arrange
            var source = new CastleDbJsonSource(_castleDbAsset);
            _service.Initialize(source);
            yield return null;

            // Act
            var npcs = _service.GetAllNpcs();

            // Assert
            Assert.Greater(npcs.Count, 0);
            foreach (var npc in npcs)
            {
                Assert.IsNotEmpty(npc.id);
                Assert.IsNotEmpty(npc.displayName);
                Assert.IsNotEmpty(npc.prefabName);
                Assert.IsNotEmpty(npc.animationTrigger);
                Assert.Greater(npc.maxHealth, 0);
                Assert.Greater(npc.moveSpeed, 0);
                Debug.Log($"[CastleDbIntegrationTest] NPC 验证通过：{npc.id}");
            }
        }
    }
}
