using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using CastleDB.Runtime;

namespace CastleDB.Editor.Providers
{
    public partial class MonsterDataProvider : CdbDataProviderBase
    {
        #region 初始化

        /// <summary>
        /// 解析 .cdb 数据并缓存
        /// 迁移自 CastleDbRepository.Load()
        /// </summary>
        protected override void OnInitialize(CastleDbRoot root, CdbModuleDescriptor descriptor)
        {
            _npcs.Clear();
            _detectionZones.Clear();
            _npcAbilities.Clear();
            _npcPassiveAbilityBindings.Clear();
            _npcPassiveAbilityConditions.Clear();
            _enemyAbilities.Clear();
            _enemyProjectiles.Clear();
            _enemyOnHitSequences.Clear();
            _npcPassiveAbilities.Clear();

            foreach (var sheet in root.sheets)
            {
                switch (sheet.name)
                {
                    case "NPC":
                        _npcs = ConvertLinesToNpcEntries(sheet.lines);
                        Debug.Log($"[MonsterDataProvider] 解析 NPC Sheet：{_npcs.Count} 条");
                        break;

                    case "DetectionZone":
                        _detectionZones = ConvertLinesToDetectionZoneEntries(sheet.lines);
                        Debug.Log($"[MonsterDataProvider] 解析 DetectionZone Sheet：{_detectionZones.Count} 条");
                        break;

                    case "NpcAbility":
                        _npcAbilities = ConvertLinesToNpcAbilityEntries(sheet.lines);
                        Debug.Log($"[MonsterDataProvider] Parsed NpcAbility Sheet: {_npcAbilities.Count} lines");
                        break;

                    case "NpcPassiveAbilityBinding":
                        _npcPassiveAbilityBindings = ConvertLinesToNpcPassiveAbilityBindingEntries(sheet.lines);
                        Debug.Log($"[MonsterDataProvider] Parsed NpcPassiveAbilityBinding Sheet: {_npcPassiveAbilityBindings.Count} lines");
                        break;

                    case "NpcPassiveAbilityCondition":
                        _npcPassiveAbilityConditions = ConvertLinesToNpcPassiveAbilityConditionEntries(sheet.lines);
                        Debug.Log($"[MonsterDataProvider] Parsed NpcPassiveAbilityCondition Sheet: {_npcPassiveAbilityConditions.Count} lines");
                        break;

                    case "EnemyAbility":
                        _enemyAbilities = ConvertLinesToAbilityEntries(sheet.lines);
                        Debug.Log($"[MonsterDataProvider] Parsed EnemyAbility Sheet: {_enemyAbilities.Count} lines");
                        break;

                    case "EnemyAbilityProjectile":
                        _enemyProjectiles = ConvertLinesToProjectileDefinitions(sheet.lines);
                        Debug.Log($"[MonsterDataProvider] Parsed EnemyAbilityProjectile Sheet: {_enemyProjectiles.Count} lines");
                        break;

                    case "EnemyAbilityOnHitSequence":
                        _enemyOnHitSequences = ConvertLinesToOnHitSequenceDefinitions(sheet.lines);
                        Debug.Log($"[MonsterDataProvider] Parsed EnemyAbilityOnHitSequence Sheet: {_enemyOnHitSequences.Count} sequences");
                        break;

                    case "NpcPassiveAbility":
                        _npcPassiveAbilities = ConvertLinesToBuffDefinitions(sheet.lines);
                        Debug.Log($"[MonsterDataProvider] Parsed NpcPassiveAbility Sheet: {_npcPassiveAbilities.Count} lines");
                        break;

                    case "Meta":
                        // Meta Sheet 由 Registry 处理，此处跳过
                        break;

                    default:
                        // 0.3 版本 Monster.cdb 只处理 NPC/DetectionZone/Meta
                        // 其他 Sheet（Player/Ability 等）由对应 Provider 处理
                        Debug.LogWarning($"[MonsterDataProvider] 忽略未知 Sheet：{sheet.name}");
                        break;
    }
}
        }

        #endregion
}
}
