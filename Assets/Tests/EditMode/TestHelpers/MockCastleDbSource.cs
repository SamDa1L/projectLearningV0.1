using System.Collections.Generic;
using CastleDB.Runtime;

namespace CastleDB.Tests.EditMode.TestHelpers
{
    /// <summary>
    /// Mock CastleDbSource 用于测试
    /// 提供两种使用方式：
    /// 1. 通过构造函数传入 CastleDbRoot
    /// 2. 使用 SetupValidData() 或 SetupVersionMismatch() 方法构建数据
    /// </summary>
    public class MockCastleDbSource : ICastleDbSource
    {
        private CastleDbRoot _root;

        /// <summary>
        /// 构造函数：传入预先构建的 CastleDbRoot
        /// </summary>
        public MockCastleDbSource(CastleDbRoot root)
        {
            _root = root;
        }

        /// <summary>
        /// 默认构造函数：配合 SetupXxx() 方法使用
        /// </summary>
        public MockCastleDbSource()
        {
            _root = new CastleDbRoot();
        }

        /// <summary>
        /// 设置有效的测试数据
        /// 包含 NPC、DetectionZone 和 Meta Sheet
        /// </summary>
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

        /// <summary>
        /// 设置版本不匹配的测试数据
        /// 用于测试版本校验逻辑
        /// </summary>
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

        /// <summary>
        /// ICastleDbSource 接口实现：读取 CastleDB JSON 数据
        /// </summary>
        public CastleDbRoot ReadCastleDbJson()
        {
            return _root;
        }

        /// <summary>
        /// ICastleDbSource 接口实现：获取数据源描述
        /// </summary>
        public string GetSourceDescription()
        {
            return "MockCastleDbSource";
        }
    }
}
