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
        #region 数据校验（P2-4 拆分）
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

                // 校验 castTrigger (0.5 Phase 3) (optional)
                if (!string.IsNullOrWhiteSpace(npc.castTrigger)
                    && !System.Text.RegularExpressions.Regex.IsMatch(npc.castTrigger, @"^[a-zA-Z0-9_]+$"))
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

                // 校验 faction：数据枚举顺序为 0=null，1=enemy，2=friend，3=Neutral。
                // 约定：MonsterSystem/NPC 的 faction 不允许为 null（=0），否则阻断导入。
                if (npc.faction < 0 || npc.faction > 3)
                {
                    errors.Add($"NPC '{npc.id}' 的 faction 超出范围（0..3）：{npc.faction}");
                }
                else if (npc.faction == 0)
                {
                    errors.Add($"NPC '{npc.id}' 的 faction 不能为 null（必须为 enemy/friend/Neutral）");
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

                if (ability.kind < 0 || ability.kind > (int)AbilityKind.AttackOverride)
                {
                    errors.Add($"MonsterSystem/EnemyAbility '{ability.id}' kind out of range (0-{(int)AbilityKind.AttackOverride}): {ability.kind}");
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
            foreach (var buff in _npcPassiveAbilities)
            {
                if (buff == null)
                {
                    errors.Add("MonsterSystem/NpcPassiveAbility contains null entry");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(buff.id))
                {
                    errors.Add("MonsterSystem/NpcPassiveAbility id is empty");
                    continue;
                }

                if (!buffIdSet.Add(buff.id))
                {
                    errors.Add($"MonsterSystem/NpcPassiveAbility id duplicated: '{buff.id}'");
                }

                if (!string.IsNullOrWhiteSpace(buff.modifiersJson)
                    && CastleDbJsonUtil.TryParseJsonObject(buff.modifiersJson) == null)
                {
                    errors.Add($"MonsterSystem/NpcPassiveAbility '{buff.id}' modifiersJson must be a JSON object");
                }

                if (!string.IsNullOrWhiteSpace(buff.prefabPath) && buff.prefabDuration < 0f)
                {
                    errors.Add($"MonsterSystem/NpcPassiveAbility '{buff.id}' prefabDuration must be >= 0 when prefabPath is set (current={buff.prefabDuration})");
                }

                if (!string.IsNullOrWhiteSpace(buff.onExpireVfxPath) && buff.onExpireVfxDuration < 0f)
                {
                    errors.Add($"MonsterSystem/NpcPassiveAbility '{buff.id}' onExpireVfxDuration must be >= 0 when onExpireVfxPath is set (current={buff.onExpireVfxDuration})");
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

                AbilityKind kind = ability.kind >= 0 && ability.kind <= (int)AbilityKind.AttackOverride
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

                if (kind == AbilityKind.AttackOverride)
                {
                    if (string.IsNullOrWhiteSpace(ability.projectileId))
                    {
                        errors.Add($"MonsterSystem/EnemyAbility '{ability.id}' kind=AttackOverride but projectileId is empty");
                    }
                    else if (!projectileIdSet.Contains(ability.projectileId))
                    {
                        errors.Add($"MonsterSystem/EnemyAbility '{ability.id}' kind=AttackOverride references missing projectileId='{ability.projectileId}'");
                    }
                }

                if (kind == AbilityKind.Buff || kind == AbilityKind.StatModifier)
                {
                    if (string.IsNullOrWhiteSpace(ability.buffId))
                    {
                        errors.Add($"MonsterSystem/EnemyAbility '{ability.id}' kind={kind} but buffId is empty");
                    }
                    else if (!buffIdSet.Contains(ability.buffId))
                    {
                        errors.Add($"MonsterSystem/EnemyAbility '{ability.id}' references missing buffId='{ability.buffId}'");
                    }
                }

                if (kind == AbilityKind.Dash)
                {
                    if (string.IsNullOrWhiteSpace(ability.paramsJson))
                    {
                        errors.Add($"MonsterSystem/EnemyAbility '{ability.id}' kind=Dash but paramsJson is empty (needs distance/speed)");
                    }
                }

                if (kind == AbilityKind.Summon)
                {
                    if (string.IsNullOrWhiteSpace(ability.paramsJson))
                    {
                        errors.Add($"MonsterSystem/EnemyAbility '{ability.id}' kind=Summon but paramsJson is empty (needs prefabPath)");
                    }
                }

                if (!string.IsNullOrWhiteSpace(ability.onHitSequenceId)
                    && !onHitSequenceIdSet.Contains(ability.onHitSequenceId))
                {
                    errors.Add($"MonsterSystem/EnemyAbility '{ability.id}' references missing onHitSequenceId='{ability.onHitSequenceId}'");
                }
            }

            // ===== NpcAbility validation (0.5 Phase 3) =====
            var enemyAbilityById = new Dictionary<string, AbilityEntry>();
            foreach (var ability in _enemyAbilities)
            {
                if (ability == null || string.IsNullOrWhiteSpace(ability.id))
                {
                    continue;
                }

                if (!enemyAbilityById.ContainsKey(ability.id))
                {
                    enemyAbilityById.Add(ability.id, ability);
                }
            }

            var npcAbilityIdSet = new HashSet<string>();
            var npcAbilityById = new Dictionary<string, NpcAbilityEntry>();
            var npcAbilityKindByBindingId = new Dictionary<string, AbilityKind>();
            var npcAbilitiesRequiringPassiveBinding = new HashSet<string>();
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
                else
                {
                    npcAbilityById[binding.id] = binding;
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
                else if (enemyAbilityById.TryGetValue(binding.abilityId, out var enemyAbility) && enemyAbility != null)
                {
                    AbilityKind kind = enemyAbility.kind >= 0 && enemyAbility.kind <= (int)AbilityKind.AttackOverride
                        ? (AbilityKind)enemyAbility.kind
                        : AbilityKind.BuiltinDefault;
                    npcAbilityKindByBindingId[binding.id] = kind;

                    if (kind == AbilityKind.Buff || kind == AbilityKind.StatModifier)
                    {
                        npcAbilitiesRequiringPassiveBinding.Add(binding.id);
                    }
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


            }

            // ===== NpcPassiveAbility validation (0.5 Phase 5) =====
            var passiveBindingIdSet = new HashSet<string>();
            var passiveBindingByBindingId = new Dictionary<string, NpcPassiveAbilityBindingEntry>();
            foreach (var passiveBinding in _npcPassiveAbilityBindings)
            {
                if (passiveBinding == null)
                {
                    errors.Add("NpcPassiveAbilityBinding contains null entry");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(passiveBinding.bindingId))
                {
                    errors.Add("NpcPassiveAbilityBinding bindingId is empty");
                    continue;
                }

                if (!npcAbilityIdSet.Contains(passiveBinding.bindingId))
                {
                    errors.Add($"NpcPassiveAbilityBinding bindingId '{passiveBinding.bindingId}' not found in NpcAbility");
                    continue;
                }

                if (!passiveBindingIdSet.Add(passiveBinding.bindingId))
                {
                    errors.Add($"NpcPassiveAbilityBinding bindingId duplicated: '{passiveBinding.bindingId}'");
                    continue;
                }

                passiveBindingByBindingId[passiveBinding.bindingId] = passiveBinding;

                if (!npcAbilityKindByBindingId.TryGetValue(passiveBinding.bindingId, out var npcKind)
                    || (npcKind != AbilityKind.Buff && npcKind != AbilityKind.StatModifier))
                {
                    errors.Add($"NpcPassiveAbilityBinding '{passiveBinding.bindingId}' is configured, but referenced EnemyAbility.kind is not Buff/StatModifier");
                }

                if (passiveBinding.targetMode < 0 || passiveBinding.targetMode > (int)NpcPassiveAbilityTargetMode.CurrentTarget)
                {
                    errors.Add(
                        $"NpcPassiveAbilityBinding '{passiveBinding.bindingId}' targetMode out of range (0-{(int)NpcPassiveAbilityTargetMode.CurrentTarget}): {passiveBinding.targetMode}");
                }

                if (passiveBinding.applyMode < 0 || passiveBinding.applyMode > (int)NpcPassiveAbilityApplyMode.OnEnter)
                {
                    errors.Add(
                        $"NpcPassiveAbilityBinding '{passiveBinding.bindingId}' applyMode out of range (0-{(int)NpcPassiveAbilityApplyMode.OnEnter}): {passiveBinding.applyMode}");
                }
            }

            foreach (string bindingId in npcAbilitiesRequiringPassiveBinding)
            {
                if (!passiveBindingIdSet.Contains(bindingId))
                {
                    AbilityKind kind = npcAbilityKindByBindingId.TryGetValue(bindingId, out var k) ? k : AbilityKind.BuiltinDefault;
                    errors.Add(
                        $"NpcAbility '{bindingId}' references EnemyAbility.kind={kind}, but missing NpcPassiveAbilityBinding (bindingId='{bindingId}')");
                }
            }

            var hasTargetConditionByBindingId = new HashSet<string>();
            var hasSecondaryTargetConditionByBindingId = new HashSet<string>(); // 阶段10：用于 SecondaryAttack【按需必填】判定
            var orderSetByBindingId = new Dictionary<string, HashSet<int>>();
            foreach (var cond in _npcPassiveAbilityConditions)
            {
                if (cond == null)
                {
                    errors.Add("NpcPassiveAbilityCondition contains null entry");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(cond.bindingId))
                {
                    errors.Add("NpcPassiveAbilityCondition bindingId is empty");
                    continue;
                }

                if (!npcAbilityIdSet.Contains(cond.bindingId))
                {
                    errors.Add($"NpcPassiveAbilityCondition bindingId '{cond.bindingId}' not found in NpcAbility");
                    continue;
                }

                if (cond.order < 0)
                {
                    errors.Add($"NpcPassiveAbilityCondition '{cond.bindingId}' order must be >= 0 (current={cond.order})");
                }

                if (!orderSetByBindingId.TryGetValue(cond.bindingId, out var orderSet))
                {
                    orderSet = new HashSet<int>();
                    orderSetByBindingId.Add(cond.bindingId, orderSet);
                }

                if (!orderSet.Add(cond.order))
                {
                    errors.Add($"NpcPassiveAbilityCondition '{cond.bindingId}' duplicated order={cond.order}");
                }

                if (cond.conditionType < 0 || cond.conditionType > (int)NpcPassiveAbilityConditionType.HasTargetInRole)
                {
                    errors.Add(
                        $"NpcPassiveAbilityCondition '{cond.bindingId}' conditionType out of range (0-{(int)NpcPassiveAbilityConditionType.HasTargetInRole}): {cond.conditionType}");
                    continue;
                }

                switch ((NpcPassiveAbilityConditionType)cond.conditionType)
                {
                    case NpcPassiveAbilityConditionType.SelfHpBelowPercent:
                    case NpcPassiveAbilityConditionType.SelfHpAbovePercent:
                        if (!(cond.floatValue > 0f && cond.floatValue < 1f))
                        {
                            errors.Add(
                                $"NpcPassiveAbilityCondition '{cond.bindingId}' {((NpcPassiveAbilityConditionType)cond.conditionType)} floatValue must be in (0,1) (current={cond.floatValue})");
                        }
                        break;

                    case NpcPassiveAbilityConditionType.HasTargetInRole:
                        if (cond.role < 0 || cond.role > 5)
                        {
                            errors.Add(
                                $"NpcPassiveAbilityCondition '{cond.bindingId}' HasTargetInRole role out of range (0-5): {cond.role}");
                        }
                        hasTargetConditionByBindingId.Add(cond.bindingId);

                        // 阶段10：SecondaryAttack 仅在【被需求使用】时才作为必填检测区
                        if (cond.role == 1)
                        {
                            hasSecondaryTargetConditionByBindingId.Add(cond.bindingId);
                        }
                        break;
                }
            }

            foreach (var kvp in passiveBindingByBindingId)
            {
                string bindingId = kvp.Key;
                var passiveBinding = kvp.Value;
                if (passiveBinding == null || string.IsNullOrWhiteSpace(bindingId))
                {
                    continue;
                }

                if (passiveBinding.targetMode != (int)NpcPassiveAbilityTargetMode.CurrentTarget)
                {
                    continue;
                }

                if (!npcAbilityById.TryGetValue(bindingId, out var npcAbility) || npcAbility == null)
                {
                    continue;
                }

                // triggerRole=Custom 时 runtime 无法从 DetectionZone 获取目标，必须配置 HasTargetInRole 来提供目标 hint。
                if (npcAbility.triggerRole == 2 && !hasTargetConditionByBindingId.Contains(bindingId))
                {
                    errors.Add(
                        $"NpcPassiveAbilityBinding '{bindingId}' targetMode=CurrentTarget but NpcAbility.triggerRole=Custom and no HasTargetInRole condition configured");
                }
            }


            // ===== SecondaryAttack 检测区校验（0.5 阶段10） =====
            // 目标：SecondaryAttack 改为【按需必填】——只有当配置确实依赖 SecondaryAttack 时才报错。
            foreach (var kvp in npcAbilityById)
            {
                var binding = kvp.Value;
                if (binding == null || !binding.enabled)
                {
                    continue;
                }

                bool requiresSecondaryZone = false;
                string reason = null;

                // 1) 投射物/施法：NpcAbility(triggerRole=SecondaryAttack) 且 EnemyAbility.kind=Projectile
                if (binding.triggerRole == 1
                    && npcAbilityKindByBindingId.TryGetValue(binding.id, out var kind)
                    && kind == AbilityKind.Projectile)
                {
                    requiresSecondaryZone = true;
                    reason = "NpcAbility(triggerRole=SecondaryAttack) + EnemyAbility.kind=Projectile";
                }

                // 2) 被动能力条件：HasTargetInRole(role=SecondaryAttack)
                if (!requiresSecondaryZone && hasSecondaryTargetConditionByBindingId.Contains(binding.id))
                {
                    requiresSecondaryZone = true;
                    reason = "NpcPassiveAbilityCondition.HasTargetInRole(role=SecondaryAttack)";
                }

                // 3) 被动能力目标为 CurrentTarget 且无 HasTargetInRole 条件时，会回退到 triggerRole 找目标
                if (!requiresSecondaryZone
                    && passiveBindingByBindingId.TryGetValue(binding.id, out var passiveBinding)
                    && passiveBinding != null
                    && passiveBinding.targetMode == (int)NpcPassiveAbilityTargetMode.CurrentTarget
                    && binding.triggerRole == 1
                    && !hasTargetConditionByBindingId.Contains(binding.id))
                {
                    requiresSecondaryZone = true;
                    reason = "Passive targetMode=CurrentTarget fallback to triggerRole=SecondaryAttack (no HasTargetInRole)";
                }

                if (!requiresSecondaryZone)
                {
                    continue;
                }

                bool hasSecondaryZone = _detectionZones.Any(z => z != null && z.npcId == binding.npcId && z.role == 1);
                if (!hasSecondaryZone)
                {
                    errors.Add(
                        $"NpcAbility '{binding.id}' requires SecondaryAttack DetectionZone ({reason}), but DetectionZone missing role=SecondaryAttack for npcId='{binding.npcId}' (suggest childId='DZ_Ability')");
                }
            }

            return errors;
        }

        #endregion
}
}
