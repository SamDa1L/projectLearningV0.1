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
    /// PlayerAbility 数据提供者（0.3 版本 Phase 4）
    /// 处理 PlayerAbility.cdb 中的 Ability Sheet
    ///
    /// 职责：
    /// - 解析 Ability Sheet 数据
    /// - 校验 Ability 数据完整性（含 id 唯一性、paramsJson.kind、priority 等）
    /// - 导入时生成 AbilityCatalog 资产
    ///
    /// 依赖：
    /// - Player Provider（dependencies="Player"，但当前实现不需要运行时查询）
    ///
    /// 迁移来源：
    /// - Ability 解析：CastleDbService.cs:363-383
    /// - 校验逻辑：CastleDbImporter.cs:613-696
    /// - AbilityCatalog 生成：CastleDbImporter.cs:954-1031
    /// </summary>
    public class AbilityDataProvider : CdbDataProviderBase
    {
        /// <summary>
        /// Provider ID，对应 Meta Sheet 中的 providerId
        /// </summary>
        public override string ExpectedProviderId => "PlayerAbility";

        // ===== 缓存数据 =====
        private List<AbilityEntry> _abilities = new List<AbilityEntry>();
        private List<AbilityProjectileDefinition> _projectiles = new List<AbilityProjectileDefinition>();
        private List<AbilitySummonDefinition> _summons = new List<AbilitySummonDefinition>();
        private List<AbilityOnHitSequenceDefinition> _onHitSequences = new List<AbilityOnHitSequenceDefinition>();
        private List<AbilityBuffDefinition> _buffs = new List<AbilityBuffDefinition>();

        // ===== 常量 =====
        private const string ABILITY_CATALOG_PATH = "Assets/Resources/Config/AbilityCatalog.asset";

        #region 初始化

        /// <summary>
        /// 解析 .cdb 数据并缓存
        /// </summary>
        protected override void OnInitialize(CastleDbRoot root, CdbModuleDescriptor descriptor)
        {
            _abilities.Clear();
            _projectiles.Clear();
            _summons.Clear();
            _onHitSequences.Clear();
            _buffs.Clear();

            foreach (var sheet in root.sheets)
            {
                switch (sheet.name)
                {
                    case "Ability":
                        _abilities = ConvertLinesToAbilityEntries(sheet.lines);
                        Debug.Log($"[AbilityDataProvider] 解析 Ability Sheet：{_abilities.Count} 条");
                        break;

                    case "AbilityProjectile":
                        _projectiles = ConvertLinesToProjectileDefinitions(sheet.lines);
                        Debug.Log($"[AbilityDataProvider] 解析 AbilityProjectile Sheet：{_projectiles.Count} 条");
                        break;

                    case "AbilitySummon":
                        _summons = ConvertLinesToSummonDefinitions(sheet.lines);
                        Debug.Log($"[AbilityDataProvider] Parsed AbilitySummon Sheet: {_summons.Count}");
                        break;

                    case "AbilityOnHitSequence":
                        _onHitSequences = ConvertLinesToOnHitSequenceDefinitions(sheet.lines);
                        Debug.Log($"[AbilityDataProvider] 解析 AbilityOnHitSequence Sheet：{_onHitSequences.Count} 个序列");
                        break;

                    case "AbilityBuff":
                        _buffs = ConvertLinesToBuffDefinitions(sheet.lines);
                        Debug.Log($"[AbilityDataProvider] 解析 AbilityBuff Sheet：{_buffs.Count} 条");
                        break;

                    case "Meta":
                        // Meta Sheet 由 Registry 处理，此处跳过
                        break;

                    default:
                        Debug.LogWarning($"[AbilityDataProvider] 忽略未知 Sheet：{sheet.name}");
                        break;
                }
            }
        }

        #endregion

        #region Ability 解析

        /// <summary>
        /// 将通用 lines 转换为 AbilityEntry 列表
        /// 迁移自 CastleDbService.cs:363-383
        /// </summary>
        private List<AbilityEntry> ConvertLinesToAbilityEntries(List<object> lines)
        {
            var result = new List<AbilityEntry>();
            if (lines == null) return result;

            foreach (var line in lines)
            {
                if (line is Dictionary<string, object> dict)
                {
                    result.Add(new AbilityEntry
                    {
                        id = GetStringValue(dict, "id"),
                        hookType = GetIntValue(dict, "hookType"),
                        priority = GetIntValue(dict, "priority"),
                        enabled = GetBoolValue(dict, "enabled"),
                        kind = GetIntValue(dict, "kind"),
                        projectileId = GetStringValue(dict, "projectileId"),
                        summonId = GetStringValue(dict, "summonId"),
                        buffId = GetStringValue(dict, "buffId"),
                        cooldown = GetFloatValue(dict, "cooldown"),
                        onHitSequenceId = GetStringValue(dict, "onHitSequenceId"),
                        paramsJson = GetStringValue(dict, "paramsJson")
                    });
                }
            }
            return result;
        }

        private List<AbilityProjectileDefinition> ConvertLinesToProjectileDefinitions(List<object> lines)
        {
            var result = new List<AbilityProjectileDefinition>();
            if (lines == null) return result;

            foreach (var line in lines)
            {
                if (line is Dictionary<string, object> dict)
                {
                    float baseDamage = GetFloatValue(dict, "baseDamage");
                    result.Add(new AbilityProjectileDefinition
                    {
                        id = GetStringValue(dict, "id"),
                        prefabPath = GetStringValue(dict, "prefabPath"),
                        speed = GetFloatValue(dict, "speed"),
                        lifetime = GetFloatValue(dict, "lifetime"),
                        baseDamage = Mathf.Max(0, Mathf.RoundToInt(baseDamage)),
                        hitMask = GetStringValue(dict, "hitMask"),
                        onHitVfxPath = GetStringValue(dict, "onHitVfxPath"),
                        onHitVfxDuration = Mathf.Max(0f, GetFloatValue(dict, "onHitVfxDuration")),
                        onExpireVfxPath = GetStringValue(dict, "onExpireVfxPath"),
                        onExpireVfxDuration = Mathf.Max(0f, GetFloatValue(dict, "onExpireVfxDuration")),
                        tags = GetStringValue(dict, "tags")
                    });
                }
            }

            return result;
        }

        private List<AbilitySummonDefinition> ConvertLinesToSummonDefinitions(List<object> lines)
        {
            var result = new List<AbilitySummonDefinition>();
            if (lines == null) return result;

            foreach (var line in lines)
            {
                if (line is Dictionary<string, object> dict)
                {
                    int spawnRuleRaw = GetIntValue(dict, "spawnRule");
                    var spawnRule = spawnRuleRaw >= 0 && spawnRuleRaw <= (int)AbilitySummonSpawnRule.Reject
                        ? (AbilitySummonSpawnRule)spawnRuleRaw
                        : AbilitySummonSpawnRule.ReplaceOldest;

                    // factionOverride 使用数据枚举：0=null，1=enemy，2=friend，3=Neutral
                    int factionOverrideRaw = GetIntValueOrDefault(dict, "factionOverride", 0);
                    FactionId factionOverride = FactionUtility.FromCastleDbFaction(factionOverrideRaw);

                    result.Add(new AbilitySummonDefinition
                    {
                        id = GetStringValue(dict, "id"),
                        prefabPath = GetStringValue(dict, "prefabPath"),
                        lifetime = GetFloatValue(dict, "lifetime"),
                        isDead = GetBoolValue(dict, "isDead"),
                        factionOverride = factionOverride,
                        maxCount = GetIntValue(dict, "maxCount"),
                        spawnRule = spawnRule,
                        tags = GetStringValue(dict, "tags")
                    });
                }
            }

            return result;
        }

        private List<AbilityOnHitSequenceDefinition> ConvertLinesToOnHitSequenceDefinitions(List<object> lines)
        {
            var result = new List<AbilityOnHitSequenceDefinition>();
            if (lines == null || lines.Count == 0) return result;

            var bySequenceId = new Dictionary<string, List<AbilityOnHitNode>>();

            foreach (var line in lines)
            {
                if (!(line is Dictionary<string, object> dict))
                {
                    continue;
                }

                string sequenceId = GetStringValue(dict, "sequenceId");
                int order = GetIntValue(dict, "order");
                int nodeTypeRaw = GetIntValue(dict, "nodeType");

                if (!bySequenceId.TryGetValue(sequenceId, out var list))
                {
                    list = new List<AbilityOnHitNode>();
                    bySequenceId[sequenceId] = list;
                }

                list.Add(new AbilityOnHitNode
                {
                    order = order,
                    nodeType = (AbilityOnHitNodeType)nodeTypeRaw,
                    statusId = GetStringValue(dict, "statusId"),
                    aoeId = GetStringValue(dict, "aoeId"),
                    summonId = GetStringValue(dict, "summonId"),
                    waitMode = GetStringValue(dict, "waitMode"),
                    paramsJson = GetStringValue(dict, "paramsJson")
                });
            }

            foreach (var kvp in bySequenceId)
            {
                result.Add(new AbilityOnHitSequenceDefinition
                {
                    sequenceId = kvp.Key,
                    nodes = kvp.Value.OrderBy(n => n.order).ToList()
                });
            }

            return result;
        }

        private List<AbilityBuffDefinition> ConvertLinesToBuffDefinitions(List<object> lines)
        {
            var result = new List<AbilityBuffDefinition>();
            if (lines == null) return result;

            foreach (var line in lines)
            {
                if (line is Dictionary<string, object> dict)
                {
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
            }

            return result;
        }

        #endregion

        #region 数据校验

        /// <summary>
        /// 校验 Ability 数据完整性
        /// 迁移自 CastleDbImporter.cs:613-696
        /// </summary>
        protected override List<string> OnValidate(CdbModuleDescriptor descriptor)
        {
            var errors = new List<string>();

            // ===== 1. Ability.id 基本校验：不能为空 + 唯一 =====
            var idSet = new HashSet<string>();
            foreach (var ability in _abilities)
            {
                if (string.IsNullOrWhiteSpace(ability.id))
                {
                    errors.Add("Ability 存在空 id（id 不能为空）");
                    continue;
                }

                if (!idSet.Add(ability.id))
                {
                    errors.Add($"Ability id 重复: '{ability.id}'");
                }
            }

            // ===== 2. paramsJson 格式校验（0.5：不再要求 kind 字段） =====
            foreach (var ability in _abilities)
            {
                if (string.IsNullOrWhiteSpace(ability.paramsJson))
                {
                    continue; // 可为空
                }

                var obj = CastleDbJsonUtil.TryParseJsonObject(ability.paramsJson);
                if (obj == null)
                {
                    errors.Add($"Ability '{ability.id}' 的 paramsJson 必须是 JSON 对象 ({{...}})");
                }
            }

            // ===== 3. kind 校验（0.5：结构化字段） =====
            foreach (var ability in _abilities)
            {
                if (ability.kind < 0 || ability.kind > (int)AbilityKind.AttackOverride)
                {
                    errors.Add($"Ability '{ability.id}' 的 kind 超出范围 (0-{(int)AbilityKind.AttackOverride}): {ability.kind}");
                    continue;
                }

                string kindName = ((AbilityKind)ability.kind).ToString();
                if (!AbilityRegistry.IsKindRegistered(kindName))
                {
                    errors.Add($"Ability '{ability.id}' 的 kind '{kindName}' 未在 AbilityRegistry 中注册");
                }
            }

            // ===== 4. hookType 范围校验 =====
            foreach (var ability in _abilities)
            {
                if (ability.hookType < 0 || ability.hookType > 4)
                {
                    errors.Add($"Ability '{ability.id}' 的 hookType {ability.hookType} 超出范围 (0-4)");
                }
            }

            // ===== 5. priority 唯一性校验（同一 hookType 内 priority 必须唯一） =====
            var hookTypeGroups = _abilities.GroupBy(a => a.hookType);
            foreach (var group in hookTypeGroups)
            {
                var priorityCheck = new HashSet<int>();
                foreach (var ability in group)
                {
                    if (!priorityCheck.Add(ability.priority))
                    {
                        errors.Add($"hookType {group.Key} 内存在重复的 priority: {ability.priority}");
                    }
                }
            }

            // ===== 6. AbilityProjectile 子表校验 =====
            var projectileIdSet = new HashSet<string>();
            foreach (var proj in _projectiles)
            {
                if (proj == null)
                {
                    errors.Add("AbilityProjectile 存在空行（解析失败）");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(proj.id))
                {
                    errors.Add("AbilityProjectile 存在空 id（id 不能为空）");
                    continue;
                }

                if (!projectileIdSet.Add(proj.id))
                {
                    errors.Add($"AbilityProjectile id 重复: '{proj.id}'");
                }

                if (string.IsNullOrWhiteSpace(proj.prefabPath))
                {
                    errors.Add($"AbilityProjectile '{proj.id}' 的 prefabPath 不能为空");
                }

                if (proj.speed <= 0f)
                {
                    errors.Add($"AbilityProjectile '{proj.id}' 的 speed 必须 > 0 (当前={proj.speed})");
                }

                if (proj.lifetime < 0f)
                {
                    errors.Add($"AbilityProjectile '{proj.id}' 的 lifetime 不能为负数 (当前={proj.lifetime})");
                }

                if (!string.IsNullOrWhiteSpace(proj.hitMask))
                {
                    string[] tokens = proj.hitMask.Split(new[] { ',', ';', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string token in tokens)
                    {
                        string trimmed = token?.Trim();
                        if (string.IsNullOrWhiteSpace(trimmed))
                        {
                            continue;
                        }

                        if (LayerMask.NameToLayer(trimmed) < 0)
                        {
                            errors.Add($"AbilityProjectile '{proj.id}' 的 hitMask 包含未知 Layer: '{trimmed}'");
                        }
                    }
                }
            }

            // ===== 7. AbilityBuff 子表校验（0.5 预留） =====
            var summonIdSet = new HashSet<string>();
            foreach (var summon in _summons)
            {
                if (summon == null)
                {
                    errors.Add("AbilitySummon contains null entry (parse failed)");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(summon.id))
                {
                    errors.Add("AbilitySummon contains empty id");
                    continue;
                }

                if (!summonIdSet.Add(summon.id))
                {
                    errors.Add($"AbilitySummon id duplicate: '{summon.id}'");
                }

                if (string.IsNullOrWhiteSpace(summon.prefabPath))
                {
                    errors.Add($"AbilitySummon '{summon.id}' prefabPath is empty");
                }

                // lifetime 规则：
                // - >0：时间销毁
                // - =0：不按时间销毁
                // - =-1：无时间限制（仅当 isDead=true 时有效；否则仅提示不阻塞）
                if (summon.lifetime < -1f)
                {
                    errors.Add($"AbilitySummon '{summon.id}' lifetime 不能小于 -1（current={summon.lifetime}）");
                }
                else if (summon.lifetime < 0f && !Mathf.Approximately(summon.lifetime, -1f))
                {
                    errors.Add($"AbilitySummon '{summon.id}' lifetime 仅支持 -1 / 0 / 正数（current={summon.lifetime}）");
                }
                else if (Mathf.Approximately(summon.lifetime, -1f) && !summon.isDead)
                {
                    Debug.LogWarning($"[AbilityDataProvider] AbilitySummon '{summon.id}' 配置错误：isDead=false 但 lifetime=-1（仅提示，不阻塞导入）");
                }

                // 校验 factionOverride：允许 null（None）/enemy/friend/Neutral；若出现未知值，视为错误（通常只会在手改 .cdb 时出现）
                if (summon.factionOverride != FactionId.None
                    && summon.factionOverride != FactionId.Enemy
                    && summon.factionOverride != FactionId.Friend
                    && summon.factionOverride != FactionId.Neutral)
                {
                    errors.Add($"AbilitySummon '{summon.id}' factionOverride 非法：{summon.factionOverride}");
                }

                if (summon.maxCount <= 0)
                {
                    errors.Add($"AbilitySummon '{summon.id}' maxCount must be >= 1 (current={summon.maxCount})");
                }

                int rule = (int)summon.spawnRule;
                if (rule < (int)AbilitySummonSpawnRule.ReplaceOldest || rule > (int)AbilitySummonSpawnRule.Reject)
                {
                    errors.Add($"AbilitySummon '{summon.id}' spawnRule is not supported: {summon.spawnRule}");
                }
            }

            var buffIdSet = new HashSet<string>();
            foreach (var buff in _buffs)
            {
                if (buff == null)
                {
                    errors.Add("AbilityBuff 存在空行（解析失败）");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(buff.id))
                {
                    errors.Add("AbilityBuff 存在空 id（id 不能为空）");
                    continue;
                }

                if (!buffIdSet.Add(buff.id))
                {
                    errors.Add($"AbilityBuff id 重复: '{buff.id}'");
                }

                if (!string.IsNullOrWhiteSpace(buff.modifiersJson))
                {
                    var obj = CastleDbJsonUtil.TryParseJsonObject(buff.modifiersJson);
                    if (obj == null)
                    {
                        errors.Add($"AbilityBuff '{buff.id}' 的 modifiersJson 必须是 JSON 对象 ({{...}})");
                    }
                }

                if (!string.IsNullOrWhiteSpace(buff.prefabPath) && buff.prefabDuration < 0f)
                {
                    errors.Add($"AbilityBuff '{buff.id}' 的 prefabDuration 不能为负数 (current={buff.prefabDuration})");
                }

                if (!string.IsNullOrWhiteSpace(buff.onExpireVfxPath) && buff.onExpireVfxDuration < 0f)
                {
                    errors.Add($"AbilityBuff '{buff.id}' onExpireVfxDuration must be >= 0 when onExpireVfxPath is set (current={buff.onExpireVfxDuration})");
                }
            }

            // ===== 8. AbilityOnHitSequence 子表校验 =====
            var onHitSequenceIdSet = new HashSet<string>();
            var statusCatalog = AssetDatabase.LoadAssetAtPath<StatusCatalog>("Assets/Resources/Config/StatusCatalog.asset");
            foreach (var seq in _onHitSequences)
            {
                if (seq == null)
                {
                    errors.Add("AbilityOnHitSequence 存在空序列（解析失败）");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(seq.sequenceId))
                {
                    errors.Add("AbilityOnHitSequence 存在空 sequenceId（不能为空）");
                    continue;
                }

                if (!onHitSequenceIdSet.Add(seq.sequenceId))
                {
                    errors.Add($"AbilityOnHitSequence sequenceId 重复: '{seq.sequenceId}'");
                }

                if (seq.nodes == null || seq.nodes.Count == 0)
                {
                    Debug.LogWarning($"[AbilityDataProvider] onHitSequence '{seq.sequenceId}' 为空序列（无 nodes）");
                    continue;
                }

                var orderSet = new HashSet<int>();
                foreach (var node in seq.nodes)
                {
                    if (node == null)
                    {
                        errors.Add($"AbilityOnHitSequence '{seq.sequenceId}' 存在空 node");
                        continue;
                    }

                    if (node.order <= 0)
                    {
                        errors.Add($"AbilityOnHitSequence '{seq.sequenceId}' 存在非法 order={node.order}（必须 >=1）");
                        continue;
                    }

                    if (!orderSet.Add(node.order))
                    {
                        errors.Add($"AbilityOnHitSequence '{seq.sequenceId}' 存在重复 order={node.order}");
                    }

                    if (node.nodeType < AbilityOnHitNodeType.ApplyStatus || node.nodeType > AbilityOnHitNodeType.SpawnProjectile)
                    {
                        errors.Add($"AbilityOnHitSequence '{seq.sequenceId}' 存在未支持的 nodeType={node.nodeType}");
                        continue;
                    }

                    if (node.nodeType == AbilityOnHitNodeType.ApplyStatus)
                    {
                        if (string.IsNullOrWhiteSpace(node.statusId))
                        {
                            errors.Add($"AbilityOnHitSequence '{seq.sequenceId}' 的 ApplyStatus 节点 statusId 不能为空 (order={node.order})");
                        }
                        else if (statusCatalog == null || !statusCatalog.IsValid)
                        {
                            errors.Add($"AbilityOnHitSequence '{seq.sequenceId}' 引用了 statusId='{node.statusId}'，但 StatusCatalog 缺失或无效。请先 Import Status.cdb 生成 Resources/Config/StatusCatalog.asset");
                        }
                        else if (!statusCatalog.TryGetStatus(node.statusId, out _))
                        {
                            errors.Add($"AbilityOnHitSequence '{seq.sequenceId}' 引用不存在的 statusId='{node.statusId}' (order={node.order})");
                        }
                    }

                    if (node.nodeType == AbilityOnHitNodeType.TriggerSummon)
                    {
                        if (string.IsNullOrWhiteSpace(node.summonId))
                        {
                            errors.Add($"AbilityOnHitSequence '{seq.sequenceId}' TriggerSummon node summonId is empty (order={node.order})");
                        }
                        else if (!summonIdSet.Contains(node.summonId))
                        {
                            errors.Add($"AbilityOnHitSequence '{seq.sequenceId}' references missing summonId='{node.summonId}' (order={node.order})");
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(node.paramsJson))
                    {
                        var obj = CastleDbJsonUtil.TryParseJsonObject(node.paramsJson);
                        if (obj == null)
                        {
                            errors.Add($"AbilityOnHitSequence '{seq.sequenceId}' 的 node.paramsJson 必须是 JSON 对象 (order={node.order})");
                        }
                    }
                }
            }

            // ===== 9. Ability -> 子表引用校验 =====
            foreach (var ability in _abilities)
            {
                AbilityKind kind = ability.kind >= 0 && ability.kind <= (int)AbilityKind.AttackOverride
                    ? (AbilityKind)ability.kind
                    : AbilityKind.BuiltinDefault;

                if (kind == AbilityKind.Projectile)
                {
                    if (string.IsNullOrWhiteSpace(ability.projectileId))
                    {
                        errors.Add($"Ability '{ability.id}' kind=Projectile 但 projectileId 为空");
                    }
                    else if (!projectileIdSet.Contains(ability.projectileId))
                    {
                        errors.Add($"Ability '{ability.id}' 引用不存在的 projectileId='{ability.projectileId}'");
                    }
                }

                if (kind == AbilityKind.Buff || kind == AbilityKind.StatModifier)
                {
                    if (string.IsNullOrWhiteSpace(ability.buffId))
                    {
                        errors.Add($"Ability '{ability.id}' kind={kind} 但 buffId 为空");
                    }
                    else if (!buffIdSet.Contains(ability.buffId))
                    {
                        errors.Add($"Ability '{ability.id}' 引用不存在的 buffId='{ability.buffId}'");
                    }
                }

                if (kind == AbilityKind.AttackOverride)
                {
                    if (string.IsNullOrWhiteSpace(ability.projectileId))
                    {
                        errors.Add($"Ability '{ability.id}' kind=AttackOverride 但 projectileId 为空");
                    }
                    else if (!projectileIdSet.Contains(ability.projectileId))
                    {
                        errors.Add($"Ability '{ability.id}' kind=AttackOverride 引用不存在的 projectileId='{ability.projectileId}'");
                    }
                }

                if (kind == AbilityKind.Dash)
                {
                    if (string.IsNullOrWhiteSpace(ability.paramsJson))
                    {
                        errors.Add($"Ability '{ability.id}' kind=Dash 但 paramsJson 为空（需要 distance/speed）");
                    }
                    else
                    {
                        var obj = CastleDbJsonUtil.TryParseJsonObject(ability.paramsJson);
                        if (obj == null)
                        {
                            errors.Add($"Ability '{ability.id}' kind=Dash 的 paramsJson 必须是 JSON 对象 ({{...}})");
                        }
                        else
                        {
                            if (!obj.ContainsKey("distance") || GetFloatValue(obj, "distance") <= 0f)
                            {
                                errors.Add($"Ability '{ability.id}' kind=Dash 缺少或非法 distance（必须 > 0）");
                            }

                            if (!obj.ContainsKey("speed") || GetFloatValue(obj, "speed") <= 0f)
                            {
                                errors.Add($"Ability '{ability.id}' kind=Dash 缺少或非法 speed（必须 > 0）");
                            }

                            if (obj.ContainsKey("invincibleWindow") && GetFloatValue(obj, "invincibleWindow") < 0f)
                            {
                                errors.Add($"Ability '{ability.id}' kind=Dash invincibleWindow 不能为负数");
                            }
                        }
                    }
                }

                if (kind == AbilityKind.Summon)
                {
                    if (string.IsNullOrWhiteSpace(ability.summonId))
                    {
                        errors.Add($"Ability '{ability.id}' kind=Summon 但 summonId 为空");
                    }
                    else if (!summonIdSet.Contains(ability.summonId))
                    {
                        errors.Add($"Ability '{ability.id}' 引用不存在的 summonId='{ability.summonId}'");
                    }
                }

                if (!string.IsNullOrWhiteSpace(ability.onHitSequenceId) && !onHitSequenceIdSet.Contains(ability.onHitSequenceId))
                {
                    errors.Add($"Ability '{ability.id}' 引用不存在的 onHitSequenceId='{ability.onHitSequenceId}'");
                }
            }

            // ===== 10. 启用能力数量检查（每个 hookType 至少有一个启用的能力，否则 warning） =====
            var enabledByHookType = _abilities
                .Where(a => a.enabled)
                .GroupBy(a => a.hookType)
                .ToDictionary(g => g.Key, g => g.Count());

            for (int hookType = 0; hookType <= 4; hookType++)
            {
                if (!enabledByHookType.ContainsKey(hookType) || enabledByHookType[hookType] == 0)
                {
                    Debug.LogWarning($"[AbilityDataProvider] hookType {hookType} 没有任何启用的能力");
                }
            }

            return errors;
        }

        #endregion

        #region 导入（Editor Only）

        /// <summary>
        /// 导入 Ability 数据生成 AbilityCatalog 资产
        /// 迁移自 CastleDbImporter.cs:954-1031
        /// </summary>
        protected override CdbImportResult OnImport(CdbModuleDescriptor descriptor)
        {
            var builder = new CdbImportResultBuilder(ExpectedProviderId);

            try
            {
                // 确保输出目录存在
                string catalogDir = Path.GetDirectoryName(ABILITY_CATALOG_PATH);
                if (!string.IsNullOrEmpty(catalogDir) && !Directory.Exists(catalogDir))
                {
                    Directory.CreateDirectory(catalogDir);
                    builder.AddInfo($"创建目录：{catalogDir}");
                }

                // 查找或创建 AbilityCatalog
                AbilityCatalog catalog = AssetDatabase.LoadAssetAtPath<AbilityCatalog>(ABILITY_CATALOG_PATH);

                bool isNew = catalog == null;
                if (isNew)
                {
                    // 创建新 AbilityCatalog
                    catalog = ScriptableObject.CreateInstance<AbilityCatalog>();
                    AssetDatabase.CreateAsset(catalog, ABILITY_CATALOG_PATH);
                    builder.Created(ABILITY_CATALOG_PATH);
                }
                else
                {
                    builder.Updated(ABILITY_CATALOG_PATH);
                }

                // 应用 CastleDB 数据
                catalog.ApplyFromCastleDb(_abilities, _projectiles, _summons, _onHitSequences, _buffs);

                // 记录导入摘要
                builder.AddInfo($"AbilityCatalog: {_abilities.Count} 个能力配置");
                builder.AddInfo($"AbilityProjectiles: {_projectiles.Count} 条");
                builder.AddInfo($"AbilitySummons: {_summons.Count} 条");
                builder.AddInfo($"AbilityOnHitSequences: {_onHitSequences.Count} 个序列");
                builder.AddInfo($"AbilityBuffs: {_buffs.Count} 条");

                // 统计每个 hookType 的能力数量
                var hookTypeStats = new Dictionary<AbilityHookType, int>();
                foreach (AbilityHookType hookType in System.Enum.GetValues(typeof(AbilityHookType)))
                {
                    hookTypeStats[hookType] = 0;
                }

                foreach (var ability in _abilities)
                {
                    if (ability.enabled)
                    {
                        hookTypeStats[(AbilityHookType)ability.hookType]++;
                    }
                }

                builder.AddInfo($"  能力分布:");
                foreach (var kvp in hookTypeStats)
                {
                    builder.AddInfo($"    • {kvp.Key}: {kvp.Value} 个启用");
                }

                // 列出所有能力配置
                builder.AddInfo($"  能力详情:");
                foreach (var ability in _abilities)
                {
                    string enabledStr = ability.enabled ? "✓" : "✗";
                    string hookTypeName = ((AbilityHookType)ability.hookType).ToString();
                    builder.AddInfo($"    • [{enabledStr}] {ability.id}: {hookTypeName}, priority={ability.priority}");
                }

                // 标记为 dirty
                EditorUtility.SetDirty(catalog);
            }
            catch (Exception ex)
            {
                builder.AddError($"创建/更新 AbilityCatalog 失败: {ex.Message}");
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
            if (typeof(T) == typeof(AbilityEntry))
            {
                return _abilities.Cast<T>().ToList();
            }

            return new List<T>();
        }

        /// <summary>
        /// 按 ID 获取指定类型的条目
        /// </summary>
        public override T GetEntryById<T>(string id)
        {
            if (typeof(T) == typeof(AbilityEntry))
            {
                return _abilities.FirstOrDefault(a => a.id == id) as T;
            }

            return null;
        }

        /// <summary>
        /// 获取所有能力配置
        /// </summary>
        public List<AbilityEntry> GetAllAbilities()
        {
            return new List<AbilityEntry>(_abilities);
        }

        /// <summary>
        /// 按 hookType 获取能力列表
        /// </summary>
        public List<AbilityEntry> GetAbilitiesByHookType(int hookType)
        {
            return _abilities.Where(a => a.hookType == hookType).ToList();
        }

        #endregion

        #region 重置

        /// <summary>
        /// 清除缓存数据
        /// </summary>
        protected override void OnReset()
        {
            _abilities.Clear();
            _projectiles.Clear();
            _onHitSequences.Clear();
            _buffs.Clear();
        }

        #endregion

        #region 辅助方法

        private string GetStringValue(Dictionary<string, object> dict, string key)
        {
            if (dict.TryGetValue(key, out var value))
            {
                return value?.ToString() ?? "";
            }
            return "";
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

        private int GetIntValueOrDefault(Dictionary<string, object> dict, string key, int defaultValue)
        {
            if (dict == null || string.IsNullOrWhiteSpace(key))
            {
                return defaultValue;
            }

            if (!dict.TryGetValue(key, out var value) || value == null)
            {
                return defaultValue;
            }

            if (value is int i) return i;
            if (value is long l) return (int)l;
            return int.TryParse(value.ToString(), out var result) ? result : defaultValue;
        }

        private float GetFloatValue(Dictionary<string, object> dict, string key)
        {
            if (dict.TryGetValue(key, out var value))
            {
                if (value is float f) return f;
                if (value is double d) return (float)d;
                if (value is int i) return i;
                if (value is long l) return l;
                if (float.TryParse(value?.ToString(), out var result)) return result;
            }
            return 0f;
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
