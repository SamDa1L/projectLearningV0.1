using NUnit.Framework;
using System.Collections;
using System.Linq;
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
            // 0.3 版本：加载 CastleDB MonsterSystem JSON 文件
            _castleDbAsset = Resources.Load<TextAsset>("Data/MonsterSystem");
            Assert.IsNotNull(_castleDbAsset, "无法加载 CastleDB MonsterSystem JSON 文件");

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
            Assert.AreEqual(CdbDataProviderRegistry.ExpectedSchemaVersion, _service.GetVersionInfo().schemaVersion);
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
            Assert.That(knight.maxHealth, Is.InRange(NpcRanges.MinHealth, NpcRanges.MaxHealth),
                $"Knight maxHealth out of range: {knight.maxHealth}");
            Assert.That(knight.moveSpeed, Is.InRange(NpcRanges.MinSpeed, NpcRanges.MaxSpeed),
                $"Knight moveSpeed out of range: {knight.moveSpeed}");
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
            Assert.That(flyingEye.maxHealth, Is.InRange(NpcRanges.MinHealth, NpcRanges.MaxHealth),
                $"FlyingEye maxHealth out of range: {flyingEye.maxHealth}");
            Assert.That(flyingEye.moveSpeed, Is.InRange(NpcRanges.MinSpeed, NpcRanges.MaxSpeed),
                $"FlyingEye moveSpeed out of range: {flyingEye.moveSpeed}");
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
            var knightChildIds = (knightZones ?? new System.Collections.Generic.List<DetectionZoneEntry>())
                .Select(z => z.childId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToList();

            var context = $"npcId=M_Knight\n" +
                          $"knightZones.Count={(knightZones != null ? knightZones.Count : 0)}\n" +
                          $"allZones.Count={(allZones != null ? allZones.Count : 0)}\n" +
                          $"knight.childIds=[{string.Join(", ", knightChildIds)}]\n" +
                          $"data=Assets/Resources/Data/MonsterSystem.cdb (sheet DetectionZone)";

            TestFailureHints.Require(
                knightZones != null && knightZones.Count > 0,
                "GetDetectionZonesByNpcId(\"M_Knight\") 返回空列表，说明 Knight 没有任何检测区配置。",
                "在 MonsterSystem.cdb 的 DetectionZone sheet 为 npcId=M_Knight 至少配置 1 条检测区（通常包含主攻击区）。",
                context);

            TestFailureHints.Require(
                knightChildIds.Contains("DZ_Attack"),
                "Knight 缺少基础主攻击检测区 childId='DZ_Attack'。",
                "在 MonsterSystem.cdb 的 DetectionZone sheet 为 npcId=M_Knight 配置 childId='DZ_Attack'（建议 role=0=PrimaryAttack）；如果你改了命名约定，请同步更新 Prefab/同步工具规则，并调整此测试的“基础主攻击区”判定规则。",
                context);
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
            Assert.AreEqual(CdbDataProviderRegistry.ExpectedSchemaVersion, versionInfo.schemaVersion);
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
