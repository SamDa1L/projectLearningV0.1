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
    public class MonsterDataProvider : CdbDataProviderBase
    {
        /// <summary>
        /// Provider ID，对应 Meta Sheet 中的 providerId
        /// </summary>
        public override string ExpectedProviderId => "Monster";

        // ===== 缓存数据 =====
        private List<NpcEntry> _npcs = new List<NpcEntry>();
        private List<DetectionZoneEntry> _detectionZones = new List<DetectionZoneEntry>();
        private List<NpcAbilityEntry> _npcAbilities = new List<NpcAbilityEntry>();
        private List<AbilityEntry> _enemyAbilities = new List<AbilityEntry>();
        private List<AbilityProjectileDefinition> _enemyProjectiles = new List<AbilityProjectileDefinition>();
        private List<AbilityOnHitSequenceDefinition> _enemyOnHitSequences = new List<AbilityOnHitSequenceDefinition>();
        private List<AbilityBuffDefinition> _enemyBuffs = new List<AbilityBuffDefinition>();

        private const string ENEMY_ABILITY_CATALOG_PATH = "Assets/Resources/Config/EnemyAbilityCatalog.asset";

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
            _enemyAbilities.Clear();
            _enemyProjectiles.Clear();
            _enemyOnHitSequences.Clear();
            _enemyBuffs.Clear();

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

                    case "EnemyAbilityBuff":
                        _enemyBuffs = ConvertLinesToBuffDefinitions(sheet.lines);
                        Debug.Log($"[MonsterDataProvider] Parsed EnemyAbilityBuff Sheet: {_enemyBuffs.Count} lines");
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

        #region NPC 解析（迁移自 CastleDbRepository）

        /// <summary>
        /// 将通用 lines 转换为 NpcEntry 列表
        /// 迁移自 CastleDbService.cs:318-331
        /// </summary>
        private List<NpcEntry> ConvertLinesToNpcEntries(List<object> lines)
        {
            var result = new List<NpcEntry>();
            if (lines == null) return result;

            foreach (var line in lines)
            {
                if (line is Dictionary<string, object> dict)
                {
                    result.Add(ConvertDictToNpcEntry(dict));
                }
            }
            return result;
        }

        /// <summary>
        /// 将字典转换为 NpcEntry
        /// 迁移自 CastleDbService.cs:336-357
        /// </summary>
        private NpcEntry ConvertDictToNpcEntry(Dictionary<string, object> dict)
        {
            return new NpcEntry
            {
                id = GetStringValue(dict, "id"),
                displayName = GetStringValue(dict, "displayName"),
                prefabName = GetStringValue(dict, "prefabName"),
                animationTrigger = GetStringValue(dict, "animationTrigger"),
                castTrigger = GetStringValue(dict, "castTrigger"),
                maxHealth = GetFloatValue(dict, "maxHealth"),
                attackDamage = GetFloatValue(dict, "attackDamage"),
                moveSpeed = GetFloatValue(dict, "moveSpeed"),
                attackRange = GetFloatValue(dict, "attackRange"),
                attackCooldown = GetFloatValue(dict, "attackCooldown"),
                invincibleDuration = GetFloatValue(dict, "invincibleDuration"),
                knockbackMultiplier = GetFloatValue(dict, "knockbackMultiplier"),
                enableDeathAnimation = GetBoolValue(dict, "enableDeathAnimation"),
                useLegacyLogicFallback = GetBoolValue(dict, "useLegacyLogicFallback"),
                perceptionRadius = GetFloatValue(dict, "perceptionRadius"),
                attackZonePriority = GetIntValue(dict, "attackZonePriority"),
                abilityZonePriority = GetIntValue(dict, "abilityZonePriority"),
                // 怪物命中玩家时的击退缩放系数，默认值为 1（保持 Prefab 原始击退）
                knockbackToPlayer = GetFloatValueWithDefault(dict, "knockbackToPlayer", 1f)
            };
        }

        #endregion

        #region DetectionZone 解析（迁移自 CastleDbRepository）

        /// <summary>
        /// 将通用 lines 转换为 DetectionZoneEntry 列表
        /// 迁移自 CastleDbService.cs:445-464
        /// </summary>
        private List<DetectionZoneEntry> ConvertLinesToDetectionZoneEntries(List<object> lines)
        {
            var result = new List<DetectionZoneEntry>();
            if (lines == null) return result;

            foreach (var line in lines)
            {
                if (line is Dictionary<string, object> dict)
                {
                    result.Add(new DetectionZoneEntry
                    {
                        id = GetStringValue(dict, "id"),
                        npcId = GetStringValue(dict, "npcId"),
                        role = GetIntValue(dict, "role"),
                        childId = GetStringValue(dict, "childId")
                    });
                }
            }
            return result;
        }

        #endregion

        #region 数据校验（迁移自 CastleDbImporter）

        /// <summary>
        /// 校验 NPC 和 DetectionZone 数据完整性
        /// 迁移自 CastleDbImporter.cs:168-204
        /// </summary>
        private List<NpcAbilityEntry> ConvertLinesToNpcAbilityEntries(List<object> lines)
        {
            var result = new List<NpcAbilityEntry>();
            if (lines == null) return result;

            foreach (var line in lines)
            {
                if (line is Dictionary<string, object> dict)
                {
                    result.Add(ConvertDictToNpcAbilityEntry(dict));
                }
            }

            return result;
        }

        private NpcAbilityEntry ConvertDictToNpcAbilityEntry(Dictionary<string, object> dict)
        {
            return new NpcAbilityEntry
            {
                id = GetStringValue(dict, "id"),
                npcId = GetStringValue(dict, "npcId"),
                abilityId = GetStringValue(dict, "abilityId"),
                enabled = GetBoolValue(dict, "enabled"),
                priority = GetIntValue(dict, "priority"),
                cooldownOverride = GetFloatValue(dict, "cooldownOverride"),
                triggerRole = GetIntValue(dict, "triggerRole"),
                minRange = GetFloatValue(dict, "minRange"),
                maxRange = GetFloatValue(dict, "maxRange"),
                paramsJson = GetStringValue(dict, "paramsJson")
            };
        }

        private List<AbilityEntry> ConvertLinesToAbilityEntries(List<object> lines)
        {
            var result = new List<AbilityEntry>();
            if (lines == null)
            {
                return result;
            }

            foreach (var line in lines)
            {
                if (line is not Dictionary<string, object> dict)
                {
                    continue;
                }

                result.Add(new AbilityEntry
                {
                    id = GetStringValue(dict, "id"),
                    hookType = GetIntValue(dict, "hookType"),
                    priority = GetIntValue(dict, "priority"),
                    enabled = GetBoolValue(dict, "enabled"),
                    kind = GetIntValue(dict, "kind"),
                    projectileId = GetStringValue(dict, "projectileId"),
                    buffId = GetStringValue(dict, "buffId"),
                    cooldown = GetFloatValue(dict, "cooldown"),
                    onHitSequenceId = GetStringValue(dict, "onHitSequenceId"),
                    paramsJson = GetStringValue(dict, "paramsJson")
                });
            }

            return result;
        }

        private List<AbilityProjectileDefinition> ConvertLinesToProjectileDefinitions(List<object> lines)
        {
            var result = new List<AbilityProjectileDefinition>();
            if (lines == null)
            {
                return result;
            }

            foreach (var line in lines)
            {
                if (line is not Dictionary<string, object> dict)
                {
                    continue;
                }

                result.Add(new AbilityProjectileDefinition
                {
                    id = GetStringValue(dict, "id"),
                    prefabPath = GetStringValue(dict, "prefabPath"),
                    speed = GetFloatValue(dict, "speed"),
                    lifetime = GetFloatValue(dict, "lifetime"),
                    baseDamage = GetIntValue(dict, "baseDamage"),
                    hitMask = GetStringValue(dict, "hitMask"),
                    onHitVfxPath = GetStringValue(dict, "onHitVfxPath"),
                    onHitVfxDuration = GetFloatValue(dict, "onHitVfxDuration"),
                    onExpireVfxPath = GetStringValue(dict, "onExpireVfxPath"),
                    onExpireVfxDuration = GetFloatValue(dict, "onExpireVfxDuration"),
                    tags = GetStringValue(dict, "tags")
                });
            }

            return result;
        }

        private List<AbilityBuffDefinition> ConvertLinesToBuffDefinitions(List<object> lines)
        {
            var result = new List<AbilityBuffDefinition>();
            if (lines == null)
            {
                return result;
            }

            foreach (var line in lines)
            {
                if (line is not Dictionary<string, object> dict)
                {
                    continue;
                }

                result.Add(new AbilityBuffDefinition
                {
                    id = GetStringValue(dict, "id"),
                    duration = GetFloatValue(dict, "duration"),
                    stackRule = (StatusStackRule)GetIntValue(dict, "stackRule"),
                    maxStacks = Mathf.Max(1, GetIntValue(dict, "maxStacks")),
                    uniqueKey = GetStringValue(dict, "uniqueKey"),
                    modifiersJson = GetStringValue(dict, "modifiersJson")
                });
            }

            return result;
        }

        private List<AbilityOnHitSequenceDefinition> ConvertLinesToOnHitSequenceDefinitions(List<object> lines)
        {
            var result = new List<AbilityOnHitSequenceDefinition>();
            if (lines == null)
            {
                return result;
            }

            var group = new Dictionary<string, List<AbilityOnHitNode>>();
            foreach (var line in lines)
            {
                if (line is not Dictionary<string, object> dict)
                {
                    continue;
                }

                string seqId = GetStringValue(dict, "sequenceId");
                if (string.IsNullOrWhiteSpace(seqId))
                {
                    continue;
                }

                if (!group.TryGetValue(seqId, out var nodes))
                {
                    nodes = new List<AbilityOnHitNode>();
                    group[seqId] = nodes;
                }

                nodes.Add(new AbilityOnHitNode
                {
                    order = GetIntValue(dict, "order"),
                    nodeType = (AbilityOnHitNodeType)GetIntValue(dict, "nodeType"),
                    statusId = GetStringValue(dict, "statusId"),
                    aoeId = GetStringValue(dict, "aoeId"),
                    summonId = GetStringValue(dict, "summonId"),
                    waitMode = GetStringValue(dict, "waitMode"),
                    paramsJson = GetStringValue(dict, "paramsJson")
                });
            }

            foreach (var kvp in group)
            {
                var seq = new AbilityOnHitSequenceDefinition
                {
                    sequenceId = kvp.Key,
                    nodes = kvp.Value?.OrderBy(n => n.order).ToList()
                };
                result.Add(seq);
            }

            return result;
        }

        protected override List<string> OnValidate(CdbModuleDescriptor descriptor)
        {
            var errors = new List<string>();

            // ===== NPC 校验 =====
            foreach (var npc in _npcs)
            {
                // 校验 id
                if (string.IsNullOrWhiteSpace(npc.id))
                {
                    errors.Add($"NPC 的 id 为空");
                    continue;
                }

                // 校验 maxHealth
                if (npc.maxHealth <= 0)
                {
                    errors.Add($"NPC '{npc.id}' 的 maxHealth <= 0 ({npc.maxHealth})");
                }

                // 校验 moveSpeed
                if (npc.moveSpeed <= 0)
                {
                    errors.Add($"NPC '{npc.id}' 的 moveSpeed <= 0 ({npc.moveSpeed})");
                }

                // 校验 animationTrigger
                if (string.IsNullOrWhiteSpace(npc.animationTrigger))
                {
                    errors.Add($"NPC '{npc.id}' 的 animationTrigger 为空");
                }
                else if (!System.Text.RegularExpressions.Regex.IsMatch(npc.animationTrigger, @"^[a-zA-Z0-9_]+$"))
                {
                    errors.Add($"NPC '{npc.id}' 的 animationTrigger '{npc.animationTrigger}' 包含非法字符");
                }

                // 校验 castTrigger (0.5 Phase 3)
                if (string.IsNullOrWhiteSpace(npc.castTrigger))
                {
                    errors.Add($"NPC '{npc.id}' 的 castTrigger 为空");
                }
                else if (!System.Text.RegularExpressions.Regex.IsMatch(npc.castTrigger, @"^[a-zA-Z0-9_]+$"))
                {
                    errors.Add($"NPC '{npc.id}' 的 castTrigger '{npc.castTrigger}' 包含非法字符");
                }

                // 校验 attackCooldown
                if (npc.attackCooldown < 0)
                {
                    errors.Add($"NPC '{npc.id}' 的 attackCooldown < 0 ({npc.attackCooldown})");
                }

                // 校验 attackDamage
                if (npc.attackDamage < 0)
                {
                    errors.Add($"NPC '{npc.id}' 的 attackDamage < 0 ({npc.attackDamage})");
                }

                if (npc.attackZonePriority < 0)
                {
                    errors.Add($"NPC '{npc.id}' 的 attackZonePriority < 0 ({npc.attackZonePriority})");
                }

                if (npc.abilityZonePriority < 0)
                {
                    errors.Add($"NPC '{npc.id}' 的 abilityZonePriority < 0 ({npc.abilityZonePriority})");
                }
            }

            // ===== NPC ID 唯一性校验 =====
            var npcIdSet = new HashSet<string>();
            foreach (var npc in _npcs)
            {
                if (!string.IsNullOrEmpty(npc.id))
                {
                    if (npcIdSet.Contains(npc.id))
                    {
                        errors.Add($"NPC id '{npc.id}' 重复");
                    }
                    npcIdSet.Add(npc.id);
                }
            }

            // ===== DetectionZone 校验 =====
            foreach (var zone in _detectionZones)
            {
                // 校验 npcId 是否存在
                if (string.IsNullOrWhiteSpace(zone.npcId))
                {
                    errors.Add($"DetectionZone '{zone.id}' 的 npcId 为空");
                }
                else if (!npcIdSet.Contains(zone.npcId))
                {
                    errors.Add($"DetectionZone '{zone.id}' 的 npcId '{zone.npcId}' 在 NPC Sheet 中不存在");
                }

                // 校验 childId
                if (string.IsNullOrWhiteSpace(zone.childId))
                {
                    errors.Add($"DetectionZone '{zone.id}' 的 childId 为空");
                }

                // 校验 role 范围
                if (zone.role < 0 || zone.role > 5)
                {
                    errors.Add($"DetectionZone '{zone.id}' 的 role {zone.role} 超出范围 (0-5)");
                }
            }

            // ===== EnemyAbility validation (0.5 Phase 3) =====
            var enemyAbilityIdSet = new HashSet<string>();
            foreach (var ability in _enemyAbilities)
            {
                if (ability == null)
                {
                    errors.Add("MonsterSystem/EnemyAbility contains null entry");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(ability.id))
                {
                    errors.Add("MonsterSystem/EnemyAbility id is empty");
                    continue;
                }

                if (!enemyAbilityIdSet.Add(ability.id))
                {
                    errors.Add($"MonsterSystem/EnemyAbility id duplicated: '{ability.id}'");
                }

                if (!string.IsNullOrWhiteSpace(ability.paramsJson)
                    && CastleDbJsonUtil.TryParseJsonObject(ability.paramsJson) == null)
                {
                    errors.Add($"MonsterSystem/EnemyAbility '{ability.id}' paramsJson must be a JSON object");
                }

                if (ability.kind < 0 || ability.kind > (int)AbilityKind.Buff)
                {
                    errors.Add($"MonsterSystem/EnemyAbility '{ability.id}' kind out of range (0-{(int)AbilityKind.Buff}): {ability.kind}");
                }
                else
                {
                    string kindName = ((AbilityKind)ability.kind).ToString();
                    if (!AbilityRegistry.IsKindRegistered(kindName))
                    {
                        errors.Add($"MonsterSystem/EnemyAbility '{ability.id}' kind '{kindName}' not registered in AbilityRegistry");
                    }
                }
            }

            var projectileIdSet = new HashSet<string>();
            foreach (var proj in _enemyProjectiles)
            {
                if (proj == null)
                {
                    errors.Add("MonsterSystem/EnemyAbilityProjectile contains null entry");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(proj.id))
                {
                    errors.Add("MonsterSystem/EnemyAbilityProjectile id is empty");
                    continue;
                }

                if (!projectileIdSet.Add(proj.id))
                {
                    errors.Add($"MonsterSystem/EnemyAbilityProjectile id duplicated: '{proj.id}'");
                }

                if (string.IsNullOrWhiteSpace(proj.prefabPath))
                {
                    errors.Add($"MonsterSystem/EnemyAbilityProjectile '{proj.id}' prefabPath is empty");
                }

                if (proj.speed <= 0f)
                {
                    errors.Add($"MonsterSystem/EnemyAbilityProjectile '{proj.id}' speed must be > 0 (current={proj.speed})");
                }

                if (proj.lifetime < 0f)
                {
                    errors.Add($"MonsterSystem/EnemyAbilityProjectile '{proj.id}' lifetime must be >= 0 (current={proj.lifetime})");
                }

                if (proj.baseDamage < 0)
                {
                    errors.Add($"MonsterSystem/EnemyAbilityProjectile '{proj.id}' baseDamage must be >= 0 (current={proj.baseDamage})");
                }

                if (proj.onHitVfxDuration < 0f)
                {
                    errors.Add($"MonsterSystem/EnemyAbilityProjectile '{proj.id}' onHitVfxDuration must be >= 0 (current={proj.onHitVfxDuration})");
                }

                if (proj.onExpireVfxDuration < 0f)
                {
                    errors.Add($"MonsterSystem/EnemyAbilityProjectile '{proj.id}' onExpireVfxDuration must be >= 0 (current={proj.onExpireVfxDuration})");
                }

                if (!string.IsNullOrWhiteSpace(proj.onExpireVfxPath) && proj.onExpireVfxDuration <= 0f)
                {
                    errors.Add(
                        $"MonsterSystem/EnemyAbilityProjectile '{proj.id}' onExpireVfxDuration must be > 0 when onExpireVfxPath is set (current={proj.onExpireVfxDuration})");
                }
            }

            var buffIdSet = new HashSet<string>();
            foreach (var buff in _enemyBuffs)
            {
                if (buff == null)
                {
                    errors.Add("MonsterSystem/EnemyAbilityBuff contains null entry");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(buff.id))
                {
                    errors.Add("MonsterSystem/EnemyAbilityBuff id is empty");
                    continue;
                }

                if (!buffIdSet.Add(buff.id))
                {
                    errors.Add($"MonsterSystem/EnemyAbilityBuff id duplicated: '{buff.id}'");
                }

                if (!string.IsNullOrWhiteSpace(buff.modifiersJson)
                    && CastleDbJsonUtil.TryParseJsonObject(buff.modifiersJson) == null)
                {
                    errors.Add($"MonsterSystem/EnemyAbilityBuff '{buff.id}' modifiersJson must be a JSON object");
                }
            }

            var onHitSequenceIdSet = new HashSet<string>();
            var statusCatalog = AssetDatabase.LoadAssetAtPath<StatusCatalog>("Assets/Resources/Config/StatusCatalog.asset");
            foreach (var seq in _enemyOnHitSequences)
            {
                if (seq == null)
                {
                    errors.Add("MonsterSystem/EnemyAbilityOnHitSequence contains null sequence");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(seq.sequenceId))
                {
                    errors.Add("MonsterSystem/EnemyAbilityOnHitSequence sequenceId is empty");
                    continue;
                }

                if (!onHitSequenceIdSet.Add(seq.sequenceId))
                {
                    errors.Add($"MonsterSystem/EnemyAbilityOnHitSequence sequenceId duplicated: '{seq.sequenceId}'");
                }

                if (seq.nodes == null || seq.nodes.Count == 0)
                {
                    continue;
                }

                var orderSet = new HashSet<int>();
                foreach (var node in seq.nodes)
                {
                    if (node == null)
                    {
                        errors.Add($"MonsterSystem/EnemyAbilityOnHitSequence '{seq.sequenceId}' contains null node");
                        continue;
                    }

                    if (node.order <= 0)
                    {
                        errors.Add($"MonsterSystem/EnemyAbilityOnHitSequence '{seq.sequenceId}' contains invalid order={node.order}");
                        continue;
                    }

                    if (!orderSet.Add(node.order))
                    {
                        errors.Add($"MonsterSystem/EnemyAbilityOnHitSequence '{seq.sequenceId}' contains duplicated order={node.order}");
                    }

                    if (node.nodeType < AbilityOnHitNodeType.ApplyStatus || node.nodeType > AbilityOnHitNodeType.SpawnProjectile)
                    {
                        errors.Add($"MonsterSystem/EnemyAbilityOnHitSequence '{seq.sequenceId}' contains unsupported nodeType={node.nodeType}");
                        continue;
                    }

                    if (node.nodeType == AbilityOnHitNodeType.ApplyStatus)
                    {
                        if (string.IsNullOrWhiteSpace(node.statusId))
                        {
                            errors.Add($"MonsterSystem/EnemyAbilityOnHitSequence '{seq.sequenceId}' ApplyStatus node statusId is empty (order={node.order})");
                        }
                        else if (statusCatalog == null || !statusCatalog.IsValid)
                        {
                            errors.Add($"MonsterSystem/EnemyAbilityOnHitSequence '{seq.sequenceId}' references statusId='{node.statusId}', but StatusCatalog missing/invalid. Import Status.cdb first.");
                        }
                        else if (!statusCatalog.TryGetStatus(node.statusId, out _))
                        {
                            errors.Add($"MonsterSystem/EnemyAbilityOnHitSequence '{seq.sequenceId}' references missing statusId='{node.statusId}' (order={node.order})");
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(node.paramsJson)
                        && CastleDbJsonUtil.TryParseJsonObject(node.paramsJson) == null)
                    {
                        errors.Add($"MonsterSystem/EnemyAbilityOnHitSequence '{seq.sequenceId}' node.paramsJson must be a JSON object (order={node.order})");
                    }
                }
            }

            foreach (var ability in _enemyAbilities)
            {
                if (ability == null)
                {
                    continue;
                }

                AbilityKind kind = ability.kind >= 0 && ability.kind <= (int)AbilityKind.Buff
                    ? (AbilityKind)ability.kind
                    : AbilityKind.BuiltinDefault;

                if (kind == AbilityKind.Projectile)
                {
                    if (string.IsNullOrWhiteSpace(ability.projectileId))
                    {
                        errors.Add($"MonsterSystem/EnemyAbility '{ability.id}' kind=Projectile but projectileId is empty");
                    }
                    else if (!projectileIdSet.Contains(ability.projectileId))
                    {
                        errors.Add($"MonsterSystem/EnemyAbility '{ability.id}' references missing projectileId='{ability.projectileId}'");
                    }
                }

                if ((kind == AbilityKind.Buff || kind == AbilityKind.StatModifier)
                    && !string.IsNullOrWhiteSpace(ability.buffId)
                    && !buffIdSet.Contains(ability.buffId))
                {
                    errors.Add($"MonsterSystem/EnemyAbility '{ability.id}' references missing buffId='{ability.buffId}'");
                }

                if (!string.IsNullOrWhiteSpace(ability.onHitSequenceId)
                    && !onHitSequenceIdSet.Contains(ability.onHitSequenceId))
                {
                    errors.Add($"MonsterSystem/EnemyAbility '{ability.id}' references missing onHitSequenceId='{ability.onHitSequenceId}'");
                }
            }

            // ===== NpcAbility validation (0.5 Phase 3) =====
            var npcAbilityIdSet = new HashSet<string>();
            foreach (var binding in _npcAbilities)
            {
                if (binding == null)
                {
                    errors.Add("NpcAbility contains null entry");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(binding.id))
                {
                    errors.Add("NpcAbility id is empty");
                    continue;
                }

                if (!npcAbilityIdSet.Add(binding.id))
                {
                    errors.Add($"NpcAbility id '{binding.id}' duplicated");
                }

                if (string.IsNullOrWhiteSpace(binding.npcId))
                {
                    errors.Add($"NpcAbility '{binding.id}' npcId is empty");
                }
                else if (!npcIdSet.Contains(binding.npcId))
                {
                    errors.Add($"NpcAbility '{binding.id}' npcId '{binding.npcId}' not found in NPC sheet");
                }

                if (string.IsNullOrWhiteSpace(binding.abilityId))
                {
                    errors.Add($"NpcAbility '{binding.id}' abilityId is empty");
                }
                else if (!enemyAbilityIdSet.Contains(binding.abilityId))
                {
                    errors.Add($"NpcAbility '{binding.id}' abilityId '{binding.abilityId}' not found in MonsterSystem/EnemyAbility");
                }

                if (binding.triggerRole < 0 || binding.triggerRole > 2)
                {
                    errors.Add($"NpcAbility '{binding.id}' triggerRole {binding.triggerRole} out of range (0-2)");
                }

                if (binding.minRange < 0f)
                {
                    errors.Add($"NpcAbility '{binding.id}' minRange < 0 ({binding.minRange})");
                }

                if (binding.maxRange < 0f)
                {
                    errors.Add($"NpcAbility '{binding.id}' maxRange < 0 ({binding.maxRange})");
                }

                if (binding.minRange > 0f && binding.maxRange > 0f && binding.minRange > binding.maxRange)
                {
                    errors.Add($"NpcAbility '{binding.id}' minRange > maxRange ({binding.minRange} > {binding.maxRange})");
                }

                if (!string.IsNullOrWhiteSpace(binding.paramsJson)
                    && CastleDbJsonUtil.TryParseJsonObject(binding.paramsJson) == null)
                {
                    errors.Add($"NpcAbility '{binding.id}' paramsJson must be a JSON object");
                }

                if (binding.enabled && binding.triggerRole == 1)
                {
                    bool hasSecondaryZone = _detectionZones.Any(z => z != null && z.npcId == binding.npcId && z.role == 1);
                    if (!hasSecondaryZone)
                    {
                        errors.Add($"NpcAbility '{binding.id}' uses triggerRole=SecondaryAttack, but DetectionZone missing role=SecondaryAttack for npcId='{binding.npcId}' (suggest childId='DZ_Ability')");
                    }
                }
            }

            return errors;
        }

        #endregion

        #region 导入（Editor Only）

        /// <summary>
        /// 导入 NPC 数据生成 EnemyTuningProfile 资产
        /// 迁移自 CastleDbImporter.cs:492-547
        /// </summary>
        protected override CdbImportResult OnImport(CdbModuleDescriptor descriptor)
        {
            var builder = new CdbImportResultBuilder(ExpectedProviderId);

            const string PROFILE_OUTPUT_DIR = "Assets/Resources/Profiles";

            // 确保输出目录存在
            if (!System.IO.Directory.Exists(PROFILE_OUTPUT_DIR))
            {
                System.IO.Directory.CreateDirectory(PROFILE_OUTPUT_DIR);
                builder.AddInfo($"创建目录：{PROFILE_OUTPUT_DIR}");
            }

            foreach (var npc in _npcs)
            {
                try
                {
                    // 生成 Profile 文件路径（使用 npc.id 作为稳定主键）
                    string profilePath = $"{PROFILE_OUTPUT_DIR}/Profile_{npc.id}.asset";

                    // 查找或创建 Profile
                    EnemyTuningProfile profile = AssetDatabase.LoadAssetAtPath<EnemyTuningProfile>(profilePath);

                    // 记录旧的 animationTrigger（用于变更日志）
                    string oldTrigger = profile != null ? profile.animationTrigger : null;
                    string oldCastTrigger = profile != null ? profile.castTrigger : null;

                    bool isNew = profile == null;
                    if (isNew)
                    {
                        // 创建新 Profile
                        profile = ScriptableObject.CreateInstance<EnemyTuningProfile>();
                        profile.profileName = npc.displayName;
                        AssetDatabase.CreateAsset(profile, profilePath);
                        builder.Created(profilePath);
                    }
                    else
                    {
                        builder.Updated(profilePath);
                    }

                    // 应用 CastleDB 数据
                    var npcAbilities = _npcAbilities
                        .Where(binding => binding != null && binding.npcId == npc.id)
                        .OrderByDescending(binding => binding.priority)
                        .ThenBy(binding => binding.id)
                        .ToList();

                    profile.ApplyFromCastleDb(npc, npcAbilities);

                    // animationTrigger 变更记录
                    if (!string.IsNullOrEmpty(oldTrigger) && oldTrigger != npc.animationTrigger)
                    {
                        builder.AddWarning($"AnimationTrigger 变更 - NPC '{npc.id}': '{oldTrigger}' → '{npc.animationTrigger}'");
                    }

                    // castTrigger 变更记录
                    if (!string.IsNullOrEmpty(oldCastTrigger) && oldCastTrigger != npc.castTrigger)
                    {
                        builder.AddWarning($"CastTrigger 变更 - NPC '{npc.id}': '{oldCastTrigger}' → '{npc.castTrigger}'");
                    }

                    // 标记为 dirty（由 ImportCoordinator 统一保存）
                    EditorUtility.SetDirty(profile);
                }
                catch (Exception ex)
                {
                    builder.AddError($"创建/更新 Profile 失败 ({npc.id}): {ex.Message}");
                }
            }

            // 记录 DetectionZone 信息（不生成资产，仅记录日志）
            if (_detectionZones.Count > 0)
            {
                builder.AddInfo($"DetectionZone 数据：{_detectionZones.Count} 条（将在 Sync NPC Prefabs 时应用）");
            }

            try
            {
                string catalogDir = Path.GetDirectoryName(ENEMY_ABILITY_CATALOG_PATH);
                if (!string.IsNullOrEmpty(catalogDir) && !Directory.Exists(catalogDir))
                {
                    Directory.CreateDirectory(catalogDir);
                    builder.AddInfo($"Created dir: {catalogDir}");
                }

                AbilityCatalog catalog = AssetDatabase.LoadAssetAtPath<AbilityCatalog>(ENEMY_ABILITY_CATALOG_PATH);
                bool isNew = catalog == null;
                if (isNew)
                {
                    catalog = ScriptableObject.CreateInstance<AbilityCatalog>();
                    AssetDatabase.CreateAsset(catalog, ENEMY_ABILITY_CATALOG_PATH);
                    builder.Created(ENEMY_ABILITY_CATALOG_PATH);
                }
                else
                {
                    builder.Updated(ENEMY_ABILITY_CATALOG_PATH);
                }

                catalog.ApplyFromCastleDb(_enemyAbilities, _enemyProjectiles, _enemyOnHitSequences, _enemyBuffs);
                builder.AddInfo($"EnemyAbilityCatalog: {_enemyAbilities.Count} abilities");
                builder.AddInfo($"EnemyAbilityProjectiles: {_enemyProjectiles.Count} entries");
                builder.AddInfo($"EnemyAbilityOnHitSequences: {_enemyOnHitSequences.Count} sequences");
                builder.AddInfo($"EnemyAbilityBuffs: {_enemyBuffs.Count} entries");

                EditorUtility.SetDirty(catalog);
            }
            catch (Exception ex)
            {
                builder.AddError($"Create/Update EnemyAbilityCatalog failed: {ex.Message}");
            }

            return builder.Build();
        }

        #endregion

        #region 数据查询

        /// <summary>
        /// 获取指定类型的所有条目
        /// </summary>
        public override List<T> GetAllEntries<T>()
        {
            if (typeof(T) == typeof(NpcEntry))
            {
                return _npcs.Cast<T>().ToList();
            }

            if (typeof(T) == typeof(DetectionZoneEntry))
            {
                return _detectionZones.Cast<T>().ToList();
            }

            if (typeof(T) == typeof(NpcAbilityEntry))
            {
                return _npcAbilities.Cast<T>().ToList();
            }

            if (typeof(T) == typeof(AbilityEntry))
            {
                return _enemyAbilities.Cast<T>().ToList();
            }

            return new List<T>();
        }

        /// <summary>
        /// 按 ID 获取指定类型的条目
        /// </summary>
        public override T GetEntryById<T>(string id)
        {
            if (typeof(T) == typeof(NpcEntry))
            {
                return _npcs.FirstOrDefault(n => n.id == id) as T;
            }

            if (typeof(T) == typeof(DetectionZoneEntry))
            {
                return _detectionZones.FirstOrDefault(z => z.id == id) as T;
            }

            if (typeof(T) == typeof(NpcAbilityEntry))
            {
                return _npcAbilities.FirstOrDefault(a => a.id == id) as T;
            }

            if (typeof(T) == typeof(AbilityEntry))
            {
                return _enemyAbilities.FirstOrDefault(a => a.id == id) as T;
            }

            return null;
        }

        /// <summary>
        /// 按 NPC ID 获取检测区列表
        /// </summary>
        public List<DetectionZoneEntry> GetDetectionZonesByNpcId(string npcId)
        {
            return _detectionZones.Where(z => z.npcId == npcId).ToList();
        }

        /// <summary>
        /// 获取所有 NPC
        /// </summary>
        public List<NpcEntry> GetAllNpcs()
        {
            return new List<NpcEntry>(_npcs);
        }

        /// <summary>
        /// 获取所有检测区
        /// </summary>
        public List<DetectionZoneEntry> GetAllDetectionZones()
        {
            return new List<DetectionZoneEntry>(_detectionZones);
        }

        #endregion

        #region 重置

        /// <summary>
        /// 清除缓存数据
        /// </summary>
        protected override void OnReset()
        {
            _npcs.Clear();
            _detectionZones.Clear();
            _npcAbilities.Clear();
            _enemyAbilities.Clear();
            _enemyProjectiles.Clear();
            _enemyOnHitSequences.Clear();
            _enemyBuffs.Clear();
        }

        #endregion

        #region 辅助方法（迁移自 CastleDbRepository）

        private string GetStringValue(Dictionary<string, object> dict, string key)
        {
            if (dict.TryGetValue(key, out var value))
            {
                return value?.ToString() ?? "";
            }
            return "";
        }

        private float GetFloatValue(Dictionary<string, object> dict, string key)
        {
            if (dict.TryGetValue(key, out var value))
            {
                if (value is float f) return f;
                if (value is double d) return (float)d;
                if (value is int i) return i;
                if (float.TryParse(value?.ToString(), out var result)) return result;
            }
            return 0f;
        }

        private float GetFloatValueWithDefault(Dictionary<string, object> dict, string key, float defaultValue)
        {
            if (dict.TryGetValue(key, out var value))
            {
                if (value is float f) return f;
                if (value is double d) return (float)d;
                if (value is int i) return i;
                if (float.TryParse(value?.ToString(), out var result)) return result;
            }
            return defaultValue;
        }

        private int GetIntValue(Dictionary<string, object> dict, string key)
        {
            if (dict.TryGetValue(key, out var value))
            {
                if (value is int i) return i;
                if (value is long l) return (int)l;
                if (int.TryParse(value?.ToString(), out var result)) return result;
            }
            return 0;
        }

        private bool GetBoolValue(Dictionary<string, object> dict, string key)
        {
            if (dict.TryGetValue(key, out var value))
            {
                if (value is bool b) return b;
                if (bool.TryParse(value?.ToString(), out var result)) return result;
            }
            return false;
        }

        #endregion
    }
}
