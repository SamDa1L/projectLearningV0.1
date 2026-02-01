using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using CastleDB.Runtime;

namespace CastleDB.Editor.Providers
{
    /// <summary>
    /// Monster 数据提供者（0.3 版本 Phase 2）
    /// 处理 MonsterSystem.cdb 中的 NPC 和 DetectionZone Sheet
    ///
    /// 职责：
    /// - 解析 NPC Sheet 数据
    /// - 解析 DetectionZone Sheet 数据
    /// - 校验 NPC 和 DetectionZone 数据完整性
    /// - 导入时生成 EnemyTuningProfile 资产
    ///
    /// 迁移来源：
    /// - NPC 解析：CastleDbService.cs:318-356
    /// - Zone 解析：CastleDbService.cs:445-464
    /// - 校验逻辑：CastleDbImporter.cs:168-204
    /// - Profile 生成：CastleDbImporter.cs:492-547
    /// </summary>
    public partial class MonsterDataProvider : CdbDataProviderBase
    {
        /// <summary>
        /// Provider ID，对应 Meta Sheet 中的 providerId
        /// </summary>
        public override string ExpectedProviderId => "Monster";

        // ===== 缓存数据 =====
        private List<NpcEntry> _npcs = new List<NpcEntry>();
        private List<DetectionZoneEntry> _detectionZones = new List<DetectionZoneEntry>();
        private List<NpcAbilityEntry> _npcAbilities = new List<NpcAbilityEntry>();
        private List<NpcPassiveAbilityBindingEntry> _npcPassiveAbilityBindings = new List<NpcPassiveAbilityBindingEntry>();
        private List<NpcPassiveAbilityConditionEntry> _npcPassiveAbilityConditions = new List<NpcPassiveAbilityConditionEntry>();
        private List<AbilityEntry> _enemyAbilities = new List<AbilityEntry>();
        private List<AbilityProjectileDefinition> _enemyProjectiles = new List<AbilityProjectileDefinition>();
        private List<AbilityOnHitSequenceDefinition> _enemyOnHitSequences = new List<AbilityOnHitSequenceDefinition>();
        private List<AbilityBuffDefinition> _npcPassiveAbilities = new List<AbilityBuffDefinition>();

        private const string ENEMY_ABILITY_CATALOG_PATH = "Assets/Resources/Config/EnemyAbilityCatalog.asset";

    }
}
