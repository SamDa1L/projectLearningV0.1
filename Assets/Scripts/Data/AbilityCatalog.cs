using System.Collections.Generic;
using UnityEngine;
using CastleDB.Runtime;

/// <summary>
/// 能力类型（0.5）
/// 与 PlayerAbility.cdb/Ability.kind 枚举顺序保持一致。
/// </summary>
public enum AbilityKind
{
    BuiltinDefault = 0,
    Projectile = 1,
    StatModifier = 2,
    Buff = 3,

    // 0.5 Phase 6：扩展主动技能类型（保持追加，避免破坏既有枚举值）
    Dash = 4,
    Summon = 5,
    AttackOverride = 6
}

/// <summary>
/// 投射物定义（0.5）
/// 对应 PlayerAbility.cdb/AbilityProjectile。
/// </summary>
[System.Serializable]
public class AbilityProjectileDefinition
{
    public string id;
    public string prefabPath;
    public float speed;
    public float lifetime;
    public int baseDamage;
    public string hitMask;
    public string onHitVfxPath;
    public float onHitVfxDuration;
    public string onExpireVfxPath;
    public float onExpireVfxDuration;
    public string tags;
}

public enum AbilitySummonSpawnRule
{
    ReplaceOldest = 0,
    Reject = 1
}

/// <summary>
/// 召唤物定义（0.5）
/// 对应 PlayerAbility.cdb/AbilitySummon。
/// </summary>
[System.Serializable]
public class AbilitySummonDefinition
{
    public string id;
    public string prefabPath;
    /// <summary>
    /// 召唤物持续时间（秒）：
    /// - > 0：时间到销毁
    /// - = 0：不启用“时间销毁”
    /// - = -1：无时间限制（由 isDead 控制；当 isDead=false 时视为错误配置，仅提示不阻塞）
    /// </summary>
    public float lifetime;

    /// <summary>
    /// 是否启用“死亡销毁”：
    /// - true：当 Damageable.Health <= 0 触发死亡事件时销毁
    /// - 可与 lifetime 组合，实现“死亡或时间到任一触发即销毁”的逻辑
    /// </summary>
    public bool isDead;

    /// <summary>
    /// 阵营覆写（可选）：
    /// - None：不覆写（使用怪物默认阵营/预制体默认阵营）
    /// - Enemy/Friend/Neutral：强制覆写召唤物阵营
    /// </summary>
    public FactionId factionOverride = FactionId.None;
    public int maxCount = 1;
    public AbilitySummonSpawnRule spawnRule;
    public string tags;
}

public enum AbilityOnHitNodeType
{
    ApplyStatus = 0,
    PlayVfx = 1,
    TriggerAOE = 2,
    TriggerSummon = 3,
    SpawnProjectile = 4
}

/// <summary>
/// 命中序列节点（0.5）
/// 对应 PlayerAbility.cdb/AbilityOnHitSequence 的一行。
/// </summary>
[System.Serializable]
public class AbilityOnHitNode
{
    public int order;
    public AbilityOnHitNodeType nodeType;

    public string statusId;
    public string aoeId;
    public string summonId;
    public string waitMode;

    public string paramsJson;
}

/// <summary>
/// 命中序列（0.5）
/// 以 sequenceId 分组并按 order 排序。
/// </summary>
[System.Serializable]
public class AbilityOnHitSequenceDefinition
{
    public string sequenceId;
    public List<AbilityOnHitNode> nodes = new List<AbilityOnHitNode>();
}

/// <summary>
/// 被动/BUFF 定义（0.5 预留）
/// 对应 PlayerAbility.cdb/AbilityBuff。
/// </summary>
[System.Serializable]
public class AbilityBuffDefinition
{
    public string id;
    public float duration;
    public StatusStackRule stackRule;
    public int maxStacks = 1;
    public string uniqueKey;
    public string modifiersJson;
    public string prefabPath;
    public float prefabDuration;
    public string onExpireVfxPath;
    public float onExpireVfxDuration;
    public string attachPointPath;
    public bool followTarget = true;
}

/// <summary>
/// 能力目录条目（阶段 3B）
/// 运行时可执行的能力配置
/// </summary>
[System.Serializable]
public class AbilityCatalogEntry
{
    /// <summary>能力 ID（对应 CastleDB Ability.id，用于 Registry/Factory 映射）</summary>
    public string id;

    /// <summary>Hook 类型</summary>
    public AbilityHookType hookType;

    /// <summary>优先级（数值越大越先执行）</summary>
    public int priority;

    /// <summary>是否启用</summary>
    public bool enabled;

    /// <summary>能力类型（0.5：从 paramsJson.kind 升级为结构化字段）</summary>
    public AbilityKind kind;

    /// <summary>投射物定义 ID（kind=Projectile 时使用）</summary>
    public string projectileId;

    /// <summary>召唤物定义 ID（kind=Summon 时使用）</summary>
    public string summonId;

    /// <summary>Buff 定义 ID（kind=Buff/StatModifier 时使用）</summary>
    public string buffId;

    /// <summary>入口冷却（秒）。只属于 Ability（不属于子表）</summary>
    public float cooldown;

    /// <summary>命中序列 ID（可空）</summary>
    public string onHitSequenceId;

    /// <summary>参数 JSON（0.5：收敛为 cast/targeting 等可变配置；不再放 kind/基础数值）</summary>
    public string paramsJson;

    public override string ToString()
    {
        return $"AbilityCatalogEntry[id={id}, hookType={hookType}, priority={priority}, enabled={enabled}, kind={kind}]";
    }
}

/// <summary>
/// 能力目录（阶段 3B）
///
/// 从 CastleDB Ability Sheet 导入的能力配置资产
/// 运行时根据此资产构建能力系统
///
/// 设计约束：
/// - 此资产由 Tools/CastleDB/Import All 生成/覆盖，禁止手动编辑
/// - OnValidate 检测手动编辑并提示
/// </summary>
[CreateAssetMenu(fileName = "AbilityCatalog", menuName = "CastleDB/AbilityCatalog")]
public class AbilityCatalog : ScriptableObject
{
    /// <summary>
    /// 能力条目列表
    /// </summary>
    [SerializeField]
    public List<AbilityCatalogEntry> entries = new List<AbilityCatalogEntry>();

    [Header("0.5 扩展：子表定义（导入产物，禁止手改）")]
    [SerializeField]
    public List<AbilityProjectileDefinition> projectiles = new List<AbilityProjectileDefinition>();

    [SerializeField]
    public List<AbilitySummonDefinition> summons = new List<AbilitySummonDefinition>();

    [SerializeField]
    public List<AbilityOnHitSequenceDefinition> onHitSequences = new List<AbilityOnHitSequenceDefinition>();

    [SerializeField]
    public List<AbilityBuffDefinition> buffs = new List<AbilityBuffDefinition>();

    [System.NonSerialized]
    private Dictionary<string, AbilityProjectileDefinition> _projectilesById;

    [System.NonSerialized]
    private Dictionary<string, AbilitySummonDefinition> _summonsById;

    [System.NonSerialized]
    private Dictionary<string, AbilityOnHitSequenceDefinition> _onHitSequencesById;

    [System.NonSerialized]
    private Dictionary<string, AbilityBuffDefinition> _buffsById;

    [System.NonSerialized]
    private bool _isValid;

    public bool IsValid => _isValid;

    private void OnEnable()
    {
        RebuildCaches();
    }

    public bool TryGetProjectile(string projectileId, out AbilityProjectileDefinition def)
    {
        def = null;

        if (_projectilesById == null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(projectileId))
        {
            return false;
        }

        return _projectilesById.TryGetValue(projectileId, out def);
    }

    public bool TryGetSummon(string summonId, out AbilitySummonDefinition def)
    {
        def = null;

        if (_summonsById == null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(summonId))
        {
            return false;
        }

        return _summonsById.TryGetValue(summonId, out def);
    }

    public bool TryGetOnHitSequence(string sequenceId, out AbilityOnHitSequenceDefinition seq)
    {
        seq = null;

        if (_onHitSequencesById == null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(sequenceId))
        {
            return false;
        }

        return _onHitSequencesById.TryGetValue(sequenceId, out seq);
    }

    public bool TryGetBuff(string buffId, out AbilityBuffDefinition def)
    {
        def = null;

        if (_buffsById == null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(buffId))
        {
            return false;
        }

        return _buffsById.TryGetValue(buffId, out def);
    }

    /// <summary>
    /// 从 CastleDB DTO 应用数据（Import 阶段调用）
    /// </summary>
    /// <param name="abilityEntries">CastleDB Ability Sheet 的所有条目</param>
    public void ApplyFromCastleDb(
        List<AbilityEntry> abilityEntries,
        List<AbilityProjectileDefinition> projectileDefinitions,
        List<AbilitySummonDefinition> summonDefinitions,
        List<AbilityOnHitSequenceDefinition> onHitSequenceDefinitions,
        List<AbilityBuffDefinition> buffDefinitions)
    {
        if (abilityEntries == null)
        {
            Debug.LogError("[AbilityCatalog] ApplyFromCastleDb: abilityEntries is null");
            return;
        }

        // 清空现有条目
        entries.Clear();

        // 转换 DTO 到运行时格式
        foreach (var dto in abilityEntries)
        {
            AbilityKind kind = AbilityKind.BuiltinDefault;
            if (dto.kind >= 0 && dto.kind <= (int)AbilityKind.AttackOverride)
            {
                kind = (AbilityKind)dto.kind;
            }

            var entry = new AbilityCatalogEntry
            {
                id = dto.id,
                hookType = (AbilityHookType)dto.hookType,
                priority = dto.priority,
                enabled = dto.enabled,
                kind = kind,
                projectileId = dto.projectileId ?? "",
                summonId = dto.summonId ?? "",
                buffId = dto.buffId ?? "",
                cooldown = dto.cooldown,
                onHitSequenceId = dto.onHitSequenceId ?? "",
                paramsJson = dto.paramsJson ?? ""
            };

            entries.Add(entry);
        }

        projectiles = projectileDefinitions ?? new List<AbilityProjectileDefinition>();
        summons = summonDefinitions ?? new List<AbilitySummonDefinition>();
        onHitSequences = onHitSequenceDefinitions ?? new List<AbilityOnHitSequenceDefinition>();
        buffs = buffDefinitions ?? new List<AbilityBuffDefinition>();

        RebuildCaches();

        Debug.Log($"[AbilityCatalog] Applied {entries.Count} ability entries from CastleDB");
    }

    private void RebuildCaches()
    {
        _isValid = false;

        _projectilesById = new Dictionary<string, AbilityProjectileDefinition>();
        _summonsById = new Dictionary<string, AbilitySummonDefinition>();
        _onHitSequencesById = new Dictionary<string, AbilityOnHitSequenceDefinition>();
        _buffsById = new Dictionary<string, AbilityBuffDefinition>();

        if (projectiles != null)
        {
            foreach (var proj in projectiles)
            {
                if (proj == null)
                {
                    Debug.LogError("[AbilityCatalog] Found null projectile definition, resource is corrupted!", this);
                    _projectilesById = null;
                    _summonsById = null;
                    _onHitSequencesById = null;
                    _buffsById = null;
                    return;
                }

                if (string.IsNullOrWhiteSpace(proj.id))
                {
                    Debug.LogError("[AbilityCatalog] Found projectile with empty id, resource is corrupted!", this);
                    _projectilesById = null;
                    _summonsById = null;
                    _onHitSequencesById = null;
                    _buffsById = null;
                    return;
                }

                if (_projectilesById.ContainsKey(proj.id))
                {
                    Debug.LogError($"[AbilityCatalog] Duplicate projectile id detected: '{proj.id}', resource is corrupted!", this);
                    _projectilesById = null;
                    _summonsById = null;
                    _onHitSequencesById = null;
                    _buffsById = null;
                    return;
                }

                _projectilesById[proj.id] = proj;
            }
        }

        if (summons != null)
        {
            foreach (var summon in summons)
            {
                if (summon == null)
                {
                    Debug.LogError("[AbilityCatalog] Found null summon definition, resource is corrupted!", this);
                    _projectilesById = null;
                    _summonsById = null;
                    _onHitSequencesById = null;
                    _buffsById = null;
                    return;
                }

                if (string.IsNullOrWhiteSpace(summon.id))
                {
                    Debug.LogError("[AbilityCatalog] Found summon with empty id, resource is corrupted!", this);
                    _projectilesById = null;
                    _summonsById = null;
                    _onHitSequencesById = null;
                    _buffsById = null;
                    return;
                }

                if (_summonsById.ContainsKey(summon.id))
                {
                    Debug.LogError($"[AbilityCatalog] Duplicate summon id detected: '{summon.id}', resource is corrupted!", this);
                    _projectilesById = null;
                    _summonsById = null;
                    _onHitSequencesById = null;
                    _buffsById = null;
                    return;
                }

                summon.maxCount = Mathf.Max(1, summon.maxCount);
                _summonsById[summon.id] = summon;
            }
        }

        if (onHitSequences != null)
        {
            foreach (var seq in onHitSequences)
            {
                if (seq == null)
                {
                    Debug.LogError("[AbilityCatalog] Found null onHitSequence, resource is corrupted!", this);
                    _projectilesById = null;
                    _summonsById = null;
                    _onHitSequencesById = null;
                    _buffsById = null;
                    return;
                }

                if (string.IsNullOrWhiteSpace(seq.sequenceId))
                {
                    Debug.LogError("[AbilityCatalog] Found onHitSequence with empty sequenceId, resource is corrupted!", this);
                    _projectilesById = null;
                    _summonsById = null;
                    _onHitSequencesById = null;
                    _buffsById = null;
                    return;
                }

                if (_onHitSequencesById.ContainsKey(seq.sequenceId))
                {
                    Debug.LogError($"[AbilityCatalog] Duplicate onHitSequence id detected: '{seq.sequenceId}', resource is corrupted!", this);
                    _projectilesById = null;
                    _summonsById = null;
                    _onHitSequencesById = null;
                    _buffsById = null;
                    return;
                }

                _onHitSequencesById[seq.sequenceId] = seq;
            }
        }

        if (buffs != null)
        {
            foreach (var buff in buffs)
            {
                if (buff == null)
                {
                    Debug.LogError("[AbilityCatalog] Found null buff definition, resource is corrupted!", this);
                    _projectilesById = null;
                    _summonsById = null;
                    _onHitSequencesById = null;
                    _buffsById = null;
                    return;
                }

                if (string.IsNullOrWhiteSpace(buff.id))
                {
                    Debug.LogError("[AbilityCatalog] Found buff with empty id, resource is corrupted!", this);
                    _projectilesById = null;
                    _summonsById = null;
                    _onHitSequencesById = null;
                    _buffsById = null;
                    return;
                }

                if (_buffsById.ContainsKey(buff.id))
                {
                    Debug.LogError($"[AbilityCatalog] Duplicate buff id detected: '{buff.id}', resource is corrupted!", this);
                    _projectilesById = null;
                    _summonsById = null;
                    _onHitSequencesById = null;
                    _buffsById = null;
                    return;
                }

                buff.maxStacks = Mathf.Max(1, buff.maxStacks);
                _buffsById[buff.id] = buff;
            }
        }

        _isValid = true;
    }

    private void OnValidate()
    {
        // 警告：此资产应由 Import All 生成，不应手动编辑
        // 0.2 在编辑器和运行时都输出警告，不强制回退（避免 Inspector 编辑时频繁弹窗）
#if UNITY_EDITOR
        Debug.LogWarning("[AbilityCatalog] 此资产由 Tools/CastleDB/Import All 生成，请勿手动编辑。" +
            "如需修改能力配置，请在 CastleDB 中编辑 Ability Sheet 并重新导入。", this);
#else
        Debug.LogWarning("[AbilityCatalog] 此资产由 Tools/CastleDB/Import All 生成，请勿手动编辑。" +
            "如需修改能力配置，请在 CastleDB 中编辑 Ability Sheet 并重新导入。");
#endif
    }
}
