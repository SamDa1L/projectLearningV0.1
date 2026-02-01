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
        #region 能力/投射物/OnHitSequence 解析（P2-4 拆分）

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
            string paramsJson = GetStringValue(dict, "paramsJson");
            CastleDbParamsJson.ParseAnimTriggerAndReleaseDelay(paramsJson, out string animTrigger, out float releaseDelay);

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
                paramsJson = paramsJson,

                // 2.2：导入阶段结构化高频字段（运行时不再反复解析 paramsJson）
                castParamsVersion = 1,
                animTrigger = animTrigger,
                releaseDelay = releaseDelay
            };
        }

        private List<NpcPassiveAbilityBindingEntry> ConvertLinesToNpcPassiveAbilityBindingEntries(List<object> lines)
        {
            var result = new List<NpcPassiveAbilityBindingEntry>();
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

                result.Add(new NpcPassiveAbilityBindingEntry
                {
                    bindingId = GetStringValue(dict, "bindingId"),
                    targetMode = GetIntValue(dict, "targetMode"),
                    applyMode = GetIntValue(dict, "applyMode")
                });
            }

            return result;
        }

        private List<NpcPassiveAbilityConditionEntry> ConvertLinesToNpcPassiveAbilityConditionEntries(List<object> lines)
        {
            var result = new List<NpcPassiveAbilityConditionEntry>();
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

                result.Add(new NpcPassiveAbilityConditionEntry
                {
                    bindingId = GetStringValue(dict, "bindingId"),
                    order = GetIntValue(dict, "order"),
                    conditionType = GetIntValue(dict, "conditionType"),
                    floatValue = GetFloatValue(dict, "floatValue"),
                    intValue = GetIntValue(dict, "intValue"),
                    role = GetIntValue(dict, "role"),
                    stringValue = GetStringValue(dict, "stringValue")
                });
            }

            return result;
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
                    modifiersJson = GetStringValue(dict, "modifiersJson"),
                    prefabPath = GetStringValue(dict, "prefabPath"),
                    prefabDuration = GetFloatValue(dict, "prefabDuration"),
                    onExpireVfxPath = GetStringValue(dict, "onExpireVfxPath"),
                    onExpireVfxDuration = GetFloatValue(dict, "onExpireVfxDuration"),
                    attachPointPath = GetStringValue(dict, "attachPointPath"),
                    followTarget = !dict.ContainsKey("followTarget") || GetBoolValue(dict, "followTarget")
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

        #endregion
}
}
