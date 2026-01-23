using System.Collections.Generic;
using CastleDB.Runtime;
using UnityEngine;

public class NpcAbilityController : MonoBehaviour
{
    private const string AbilityCatalogResourcePath = "Config/EnemyAbilityCatalog";
    private const float DefaultAbilityReleaseExpirySeconds = 1.5f;
    private const string MoveSpeedMultiplierKey = "moveSpeedMultiplier";
    private const string AttackMultiplierKey = "attackMultiplier";

    [Header("Optional Overrides")]
    [SerializeField] private AbilityCatalog abilityCatalogOverride;
    [SerializeField] private Transform firePointOverride;

    private EnemyAgentBase _agent;
    private Animator _animator;

    private AbilityCatalog _catalog;
    private Dictionary<string, AbilityCatalogEntry> _abilitiesById;
    private readonly Dictionary<string, float> _nextReadyTimeByBindingId = new Dictionary<string, float>();
    private readonly Dictionary<string, GameObject> _prefabCache = new Dictionary<string, GameObject>();

    [Header("Projectile Pool (2.3)")]
    [SerializeField] private bool useProjectilePool = true;

    [Min(0)]
    [SerializeField] private int projectilePoolMaxSize = 16;

    private readonly Dictionary<string, PrefabGameObjectPool> _projectilePoolsByPrefabPath = new Dictionary<string, PrefabGameObjectPool>();

    [Header("VFX Pool (2.3)")]
    [SerializeField] private bool useVfxPool = true;

    [Min(0)]
    [SerializeField] private int vfxPoolMaxSize = 32;

    private VfxPoolService _vfxPool;

    private Transform _cachedFirePoint;
    private bool _searchedFirePoint;

    private PendingCast _pendingCast;
    private bool _hasPendingCast;
    private bool _loggedMissingCatalog;
    private bool _loggedMissingAgent;

    private EnemyTuningProfile _cachedPassiveProfile;
    private Dictionary<string, NpcPassiveAbilityBindingEntry> _passiveBindingsByBindingId;
    private Dictionary<string, List<NpcPassiveAbilityConditionEntry>> _passiveConditionsByBindingId;

    private readonly Dictionary<string, bool> _lastConditionTrueByBindingId = new Dictionary<string, bool>();
    private readonly Dictionary<string, ActiveBuffState> _activeBuffsByBindingId = new Dictionary<string, ActiveBuffState>();
    private readonly List<string> _tmpBuffKeys = new List<string>();

    private enum PendingCastKind
    {
        Projectile = 0,
        Buff = 1
    }

    private class ActiveBuffState
    {
        public string bindingId;
        public string sourceId;
        public AbilityBuffDefinition def;
        public Transform targetRoot;
        public StatModifierLayer stats;
        public float expiresAtTime;
        public GameObject loopVfx;
    }

    private struct PendingCast
    {
        public PendingCastKind kind;
        public string bindingId;
        public string abilityId;

        public AbilityProjectileDefinition projectile;
        public IReadOnlyList<AbilityOnHitNode> onHitNodes;

        public AbilityBuffDefinition buff;
        public Transform buffTargetRoot;
        public string buffSourceId;

        public float fallbackReleaseAtTime;
        public float expiresAtTime;
        public float directionSign;
    }

    private void Awake()
    {
        _agent = GetComponent<EnemyAgentBase>();
        _animator = GetComponent<Animator>();
    }

    private void Update()
    {
        TickActiveBuffs(Time.time);
    }

    private void OnDisable()
    {
        _hasPendingCast = false;
        _pendingCast = default;
        ClearAllActiveBuffs(spawnExpireVfx: false);
    }

    /// <summary>
    /// 仅 Tick“待释放的施法请求”（如果存在），与 DetectionZone 的 role/targets 无关。
    /// 返回 true 表示当前仍处于“施法等待释放”阶段，调用方应跳过近战逻辑。
    /// </summary>
    public bool TickPendingCast()
    {
        if (!_hasPendingCast)
        {
            return false;
        }

        float now = Time.time;

        // 兜底：如果没有配置 AnimationEvent（或动画没走到事件帧），允许按 releaseDelay 走延迟发射
        if (_pendingCast.fallbackReleaseAtTime > 0f && now >= _pendingCast.fallbackReleaseAtTime)
        {
            ReleasePendingCast();
            return true;
        }

        // 超时保护：避免因为“动画事件未触发”导致 NPC 永久卡在 pending 状态
        if (_pendingCast.expiresAtTime > 0f && now > _pendingCast.expiresAtTime)
        {
            PendingCastKind kind = _pendingCast.kind;
            string abilityId = _pendingCast.abilityId ?? "";
            _hasPendingCast = false;
            _pendingCast = default;

            string expectedEvent = kind == PendingCastKind.Buff ? "OnBuffRelease" : "OnAbilityRelease";
            Debug.LogWarning(
                $"[NpcAbilityController] 施法等待超时，可能缺少 AnimationEvent: {expectedEvent}（abilityId='{abilityId}'）",
                this);
            return false;
        }

        return true;
    }

    /// <summary>
    /// AnimationEvent 入口：释放当前排队的投射物施法（如果存在）。
    /// 命名需与 PlayerController.OnAbilityRelease() 保持一致，方便复用同一套动画事件。
    /// </summary>
    public void OnAbilityRelease()
    {
        if (!_hasPendingCast || _pendingCast.kind != PendingCastKind.Projectile)
        {
            return;
        }

        ReleasePendingCast();
    }

    /// <summary>
    /// AnimationEvent 入口：释放当前排队的 Buff/StatModifier（如果存在）。
    /// 与 OnAbilityRelease 分开，便于在 AnimatorEvent 下拉列表中明确区分 projectile/buff。
    /// </summary>
    public void OnBuffRelease()
    {
        if (!_hasPendingCast || _pendingCast.kind != PendingCastKind.Buff)
        {
            return;
        }

        ReleasePendingCast();
    }

    /// <summary>
    /// Tick 所有 Buff/StatModifier 能力（0.5 Phase 5）。
    /// - 负责条件评估、WhileTrue 回滚、OnEnter 触发、持续时间到期清理
    /// - 返回 true 表示本帧已启动一次 Buff 施法（EnemyAgentBase 应跳过近战/投射物逻辑）
    /// </summary>
    public bool TickPassiveAbilities(float deltaTime)
    {
        if (_agent == null)
        {
            if (!_loggedMissingAgent)
            {
                Debug.LogWarning($"[NpcAbilityController] EnemyAgentBase not found on '{gameObject.name}', controller disabled.", this);
                _loggedMissingAgent = true;
            }
            return false;
        }

        EnemyTuningProfile profile = _agent.TuningProfile;
        if (profile == null)
        {
            ClearAllActiveBuffs(spawnExpireVfx: false);
            return false;
        }

        EnsureCatalogLoaded();
        if (_catalog == null || _abilitiesById == null)
        {
            return false;
        }

        EnsurePassiveCaches(profile);

        float now = Time.time;
        TickActiveBuffs(now);

        if (_hasPendingCast)
        {
            return false;
        }

        if (!_agent.IsAlive())
        {
            return false;
        }

        if (profile.npcAbilities == null || profile.npcAbilities.Count == 0)
        {
            return false;
        }

        NpcAbilityEntry bestBinding = null;
        AbilityCatalogEntry bestAbility = null;
        AbilityBuffDefinition bestBuff = null;
        NpcPassiveAbilityBindingEntry bestPassiveBinding = null;
        Transform bestTargetRoot = null;
        int bestPriority = int.MinValue;

        for (int i = 0; i < profile.npcAbilities.Count; i++)
        {
            var binding = profile.npcAbilities[i];
            if (binding == null || !binding.enabled)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(binding.id) || string.IsNullOrWhiteSpace(binding.abilityId))
            {
                continue;
            }

            if (_passiveBindingsByBindingId == null
                || !_passiveBindingsByBindingId.TryGetValue(binding.id, out var passiveBinding)
                || passiveBinding == null)
            {
                continue;
            }

            if (!_abilitiesById.TryGetValue(binding.abilityId, out var ability) || ability == null)
            {
                continue;
            }

            if (ability.kind != AbilityKind.StatModifier && ability.kind != AbilityKind.Buff)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(ability.buffId) || !_catalog.TryGetBuff(ability.buffId, out var buffDef) || buffDef == null)
            {
                continue;
            }

            bool conditionsTrue = EvaluatePassiveConditions(binding.id, out Transform conditionTargetHint);

            bool shouldTryCast = false;
            var applyMode = (NpcPassiveAbilityApplyMode)Mathf.Clamp(passiveBinding.applyMode, 0, (int)NpcPassiveAbilityApplyMode.OnEnter);
            if (applyMode == NpcPassiveAbilityApplyMode.OnEnter)
            {
                bool prev = _lastConditionTrueByBindingId.TryGetValue(binding.id, out bool prevValue) && prevValue;
                _lastConditionTrueByBindingId[binding.id] = conditionsTrue;

                if (!prev && conditionsTrue)
                {
                    shouldTryCast = !_activeBuffsByBindingId.ContainsKey(binding.id);
                }
            }
            else
            {
                _lastConditionTrueByBindingId[binding.id] = conditionsTrue;

                if (!conditionsTrue)
                {
                    if (_activeBuffsByBindingId.ContainsKey(binding.id))
                    {
                        RemoveActiveBuff(binding.id, spawnExpireVfx: true);
                    }
                    continue;
                }

                if (!_activeBuffsByBindingId.ContainsKey(binding.id))
                {
                    shouldTryCast = true;
                }
            }

            if (!shouldTryCast)
            {
                continue;
            }

            if (binding.priority < bestPriority)
            {
                continue;
            }

            if (IsOnCooldown(binding.id, now))
            {
                continue;
            }

            Transform targetRoot = ResolveBuffTargetRoot(binding, passiveBinding, conditionTargetHint);
            if (targetRoot == null)
            {
                continue;
            }

            if ((NpcPassiveAbilityTargetMode)Mathf.Clamp(passiveBinding.targetMode, 0, (int)NpcPassiveAbilityTargetMode.CurrentTarget)
                == NpcPassiveAbilityTargetMode.CurrentTarget)
            {
                float distanceToTarget = Vector2.Distance(transform.position, targetRoot.position);
                if (!IsInRange(profile, binding, distanceToTarget))
                {
                    continue;
                }
            }

            bestBinding = binding;
            bestAbility = ability;
            bestBuff = buffDef;
            bestPassiveBinding = passiveBinding;
            bestTargetRoot = targetRoot;
            bestPriority = binding.priority;
        }

        if (bestBinding == null || bestAbility == null || bestBuff == null || bestPassiveBinding == null || bestTargetRoot == null)
        {
            return false;
        }

        QueueBuffCast(profile, bestBinding, bestAbility, bestBuff, bestTargetRoot, now);
        return true;
    }

    /// <summary>
    /// Tick 指定 DetectionZone role 下的 NPC 投射物技能。
    /// 返回 true 表示该 role 已由本控制器接管（调用方应跳过旧近战逻辑）。
    /// </summary>
    public bool Tick(DetectionZoneBinding.Role role, float deltaTime)
    {
        if (_agent == null)
        {
            if (!_loggedMissingAgent)
            {
                Debug.LogWarning($"[NpcAbilityController] EnemyAgentBase not found on '{gameObject.name}', controller disabled.", this);
                _loggedMissingAgent = true;
            }
            return false;
        }

        EnemyTuningProfile profile = _agent.TuningProfile;
        if (profile == null || profile.npcAbilities == null || profile.npcAbilities.Count == 0)
        {
            return false;
        }

        bool hasEnabledBindingsForRole = false;
        for (int i = 0; i < profile.npcAbilities.Count; i++)
        {
            var binding = profile.npcAbilities[i];
            if (binding == null || !binding.enabled)
            {
                continue;
            }

            if (MapTriggerRole(binding.triggerRole) == role)
            {
                hasEnabledBindingsForRole = true;
                break;
            }
        }

        if (!hasEnabledBindingsForRole)
        {
            return false;
        }

        if (_hasPendingCast)
        {
            // 统一走 TickPendingCast：包含 AnimationEvent 兜底延迟与超时保护
            if (TickPendingCast())
            {
                return true;
            }
        }

        if (!_agent.IsAlive())
        {
            return true;
        }

        List<Collider2D> targets = _agent.GetDetectedTargetsForRole(role);
        if (targets == null || targets.Count == 0)
        {
            return true;
        }

        Transform target = ResolveBestTarget(targets);
        if (target == null)
        {
            return true;
        }

        TryCastBestForRole(profile, role, target);
        return true;
    }

    private void TryCastBestForRole(EnemyTuningProfile profile, DetectionZoneBinding.Role role, Transform target)
    {
        EnsureCatalogLoaded();
        if (_catalog == null || _abilitiesById == null)
        {
            return;
        }

        float now = Time.time;

        NpcAbilityEntry bestBinding = null;
        AbilityCatalogEntry bestAbility = null;
        AbilityProjectileDefinition bestProjectile = null;
        IReadOnlyList<AbilityOnHitNode> bestOnHitNodes = null;
        int bestPriority = int.MinValue;

        float distanceToTarget = Vector2.Distance(transform.position, target.position);

        for (int i = 0; i < profile.npcAbilities.Count; i++)
        {
            var binding = profile.npcAbilities[i];
            if (binding == null || !binding.enabled)
            {
                continue;
            }

            if (MapTriggerRole(binding.triggerRole) != role)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(binding.id))
            {
                continue;
            }

            if (binding.priority < bestPriority)
            {
                continue;
            }

            if (IsOnCooldown(binding.id, now))
            {
                continue;
            }

            if (!_abilitiesById.TryGetValue(binding.abilityId ?? "", out var ability) || ability == null)
            {
                continue;
            }

            if (ability.kind != AbilityKind.Projectile || string.IsNullOrWhiteSpace(ability.projectileId))
            {
                continue;
            }

            if (!_catalog.TryGetProjectile(ability.projectileId, out var projectileDef) || projectileDef == null)
            {
                continue;
            }

            if (!IsInRange(profile, binding, distanceToTarget))
            {
                continue;
            }

            AbilityOnHitSequenceDefinition onHitSeq = null;
            if (!string.IsNullOrWhiteSpace(ability.onHitSequenceId))
            {
                _catalog.TryGetOnHitSequence(ability.onHitSequenceId, out onHitSeq);
            }

            bestBinding = binding;
            bestAbility = ability;
            bestProjectile = projectileDef;
            bestOnHitNodes = onHitSeq != null ? onHitSeq.nodes : null;
            bestPriority = binding.priority;
        }

        if (bestBinding == null || bestAbility == null || bestProjectile == null)
        {
            return;
        }

        float cooldown = ResolveCooldownSeconds(bestBinding, bestAbility);
        if (cooldown > 0f)
        {
            _nextReadyTimeByBindingId[bestBinding.id] = now + cooldown;
        }

        ResolveCastParams(profile, bestBinding, bestAbility, out string animTrigger, out float releaseDelay);

        if (_animator != null && !string.IsNullOrWhiteSpace(animTrigger))
        {
            _animator.SetTrigger(animTrigger);
        }

        float dirSign = target.position.x >= transform.position.x ? 1f : -1f;

        // 0.5 阶段3优化：NPC 施法改为“动画事件驱动释放”
        // - 施法时只排队，不 Instantiate
        // - 由动画 clip 的 AnimationEvent（functionName=OnAbilityRelease）触发真正发射
        // - releaseDelay 仅作为“没有事件/动画没触发事件帧”的兜底延迟
        if (_animator != null && !string.IsNullOrWhiteSpace(animTrigger))
        {
            _hasPendingCast = true;
            float expirySeconds = Mathf.Max(DefaultAbilityReleaseExpirySeconds, releaseDelay + 0.25f);
            _pendingCast = new PendingCast
            {
                kind = PendingCastKind.Projectile,
                bindingId = bestBinding.id ?? "",
                abilityId = bestBinding.abilityId ?? "",
                projectile = bestProjectile,
                onHitNodes = bestOnHitNodes,
                fallbackReleaseAtTime = releaseDelay > 0f ? now + releaseDelay : 0f,
                expiresAtTime = now + expirySeconds,
                directionSign = dirSign
            };
            return;
        }

        // 兼容：没有 Animator/Trigger 的情况下，维持旧行为（立即或延迟发射）
        if (releaseDelay > 0f)
        {
            _hasPendingCast = true;
            _pendingCast = new PendingCast
            {
                kind = PendingCastKind.Projectile,
                bindingId = bestBinding.id ?? "",
                abilityId = bestBinding.abilityId ?? "",
                projectile = bestProjectile,
                onHitNodes = bestOnHitNodes,
                fallbackReleaseAtTime = now + releaseDelay,
                expiresAtTime = now + Mathf.Max(DefaultAbilityReleaseExpirySeconds, releaseDelay + 0.25f),
                directionSign = dirSign
            };
            return;
        }

        SpawnProjectile(bestBinding.abilityId ?? "", bestProjectile, bestOnHitNodes, dirSign);
    }

    private bool IsOnCooldown(string bindingId, float now)
    {
        if (_nextReadyTimeByBindingId.TryGetValue(bindingId, out float nextReady) && nextReady > 0f)
        {
            return now < nextReady;
        }

        return false;
    }

    private static float ResolveCooldownSeconds(NpcAbilityEntry binding, AbilityCatalogEntry ability)
    {
        if (binding == null)
        {
            return 0f;
        }

        // 约定：怪物/NPC 的技能冷却只由 NpcAbilityEntry.cooldownOverride 配置。
        // 注意：AbilityCatalogEntry.cooldown 仅用于玩家能力；这里不参与 NPC 施法冷却计算。
        return Mathf.Max(0f, binding.cooldownOverride);
    }

    private static bool IsInRange(EnemyTuningProfile profile, NpcAbilityEntry binding, float distanceToTarget)
    {
        if (profile == null || binding == null)
        {
            return false;
        }

        float minRange = binding.minRange > 0f ? binding.minRange : 0f;
        float maxRange = binding.maxRange > 0f ? binding.maxRange : Mathf.Max(0f, profile.attackRange);

        if (minRange > 0f && distanceToTarget < minRange)
        {
            return false;
        }

        if (maxRange > 0f && distanceToTarget > maxRange)
        {
            return false;
        }

        return true;
    }

    private static void ResolveCastParams(
        EnemyTuningProfile profile,
        NpcAbilityEntry binding,
        AbilityCatalogEntry ability,
        out string animTrigger,
        out float releaseDelaySeconds)
    {
        // 注意：这里的优先级必须与旧逻辑一致：
        // - 如果 binding.paramsJson 非空，则只读取 binding（即使字段缺失也不回退到 ability.paramsJson）
        // - 如果 binding.paramsJson 为空，再读取 ability 的结构化字段/缓存解析结果
        // - animTrigger 缺失/为空时回退到 NPC Profile 的 castTrigger / animationTrigger
        // - releaseDelay 缺失/非法时视为 0

        string fallbackTrigger = "";
        if (profile != null)
        {
            fallbackTrigger = !string.IsNullOrWhiteSpace(profile.castTrigger)
                ? profile.castTrigger
                : (profile.animationTrigger ?? "");
        }

        animTrigger = fallbackTrigger ?? "";
        releaseDelaySeconds = 0f;

        if (binding != null && !string.IsNullOrWhiteSpace(binding.paramsJson))
        {
            binding.GetCastParams(out string bindingTrigger, out float bindingDelay);
            if (!string.IsNullOrWhiteSpace(bindingTrigger))
            {
                animTrigger = bindingTrigger;
            }

            releaseDelaySeconds = Mathf.Max(0f, bindingDelay);
            return;
        }

        if (ability != null)
        {
            ability.GetCastParams(out string abilityTrigger, out float abilityDelay);
            if (!string.IsNullOrWhiteSpace(abilityTrigger))
            {
                animTrigger = abilityTrigger;
            }

            releaseDelaySeconds = Mathf.Max(0f, abilityDelay);
        }
    }

    private void ApplyPendingBuff(PendingCast cast)
    {
        if (cast.buff == null || cast.buffTargetRoot == null || string.IsNullOrWhiteSpace(cast.bindingId))
        {
            return;
        }

        string sourceId = !string.IsNullOrWhiteSpace(cast.buffSourceId) ? cast.buffSourceId : BuildBuffSourceId(cast.bindingId);
        ApplyBuff(cast.bindingId, sourceId, cast.buff, cast.buffTargetRoot);
    }

    private void QueueBuffCast(
        EnemyTuningProfile profile,
        NpcAbilityEntry binding,
        AbilityCatalogEntry ability,
        AbilityBuffDefinition buffDef,
        Transform targetRoot,
        float now)
    {
        if (profile == null || binding == null || ability == null || buffDef == null || targetRoot == null)
        {
            return;
        }

        float cooldown = ResolveCooldownSeconds(binding, ability);
        if (cooldown > 0f)
        {
            _nextReadyTimeByBindingId[binding.id] = now + cooldown;
        }

        ResolveCastParams(profile, binding, ability, out string animTrigger, out float releaseDelay);

        if (_animator != null && !string.IsNullOrWhiteSpace(animTrigger))
        {
            _animator.SetTrigger(animTrigger);
        }

        string sourceId = BuildBuffSourceId(binding.id);

        if (_animator != null && !string.IsNullOrWhiteSpace(animTrigger))
        {
            _hasPendingCast = true;
            float expirySeconds = Mathf.Max(DefaultAbilityReleaseExpirySeconds, releaseDelay + 0.25f);
            _pendingCast = new PendingCast
            {
                kind = PendingCastKind.Buff,
                bindingId = binding.id ?? "",
                abilityId = binding.abilityId ?? "",
                buff = buffDef,
                buffTargetRoot = targetRoot,
                buffSourceId = sourceId,
                fallbackReleaseAtTime = releaseDelay > 0f ? now + releaseDelay : 0f,
                expiresAtTime = now + expirySeconds,
                directionSign = 1f
            };
            return;
        }

        if (releaseDelay > 0f)
        {
            _hasPendingCast = true;
            _pendingCast = new PendingCast
            {
                kind = PendingCastKind.Buff,
                bindingId = binding.id ?? "",
                abilityId = binding.abilityId ?? "",
                buff = buffDef,
                buffTargetRoot = targetRoot,
                buffSourceId = sourceId,
                fallbackReleaseAtTime = now + releaseDelay,
                expiresAtTime = now + Mathf.Max(DefaultAbilityReleaseExpirySeconds, releaseDelay + 0.25f),
                directionSign = 1f
            };
            return;
        }

        ApplyBuff(binding.id, sourceId, buffDef, targetRoot);
    }

    private void EnsurePassiveCaches(EnemyTuningProfile profile)
    {
        if (profile == _cachedPassiveProfile && _passiveBindingsByBindingId != null && _passiveConditionsByBindingId != null)
        {
            return;
        }

        _cachedPassiveProfile = profile;
        _passiveBindingsByBindingId = new Dictionary<string, NpcPassiveAbilityBindingEntry>();
        _passiveConditionsByBindingId = new Dictionary<string, List<NpcPassiveAbilityConditionEntry>>();
        _lastConditionTrueByBindingId.Clear();
        ClearAllActiveBuffs(spawnExpireVfx: false);

        if (profile != null && profile.npcPassiveAbilityBindings != null)
        {
            for (int i = 0; i < profile.npcPassiveAbilityBindings.Count; i++)
            {
                var entry = profile.npcPassiveAbilityBindings[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.bindingId))
                {
                    continue;
                }

                if (!_passiveBindingsByBindingId.ContainsKey(entry.bindingId))
                {
                    _passiveBindingsByBindingId.Add(entry.bindingId, entry);
                }
            }
        }

        if (profile != null && profile.npcPassiveAbilityConditions != null)
        {
            for (int i = 0; i < profile.npcPassiveAbilityConditions.Count; i++)
            {
                var entry = profile.npcPassiveAbilityConditions[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.bindingId))
                {
                    continue;
                }

                if (!_passiveConditionsByBindingId.TryGetValue(entry.bindingId, out var list))
                {
                    list = new List<NpcPassiveAbilityConditionEntry>();
                    _passiveConditionsByBindingId.Add(entry.bindingId, list);
                }

                list.Add(entry);
            }

            foreach (var kvp in _passiveConditionsByBindingId)
            {
                kvp.Value.Sort((a, b) => a.order.CompareTo(b.order));
            }
        }
    }

    private bool EvaluatePassiveConditions(string bindingId, out Transform targetHint)
    {
        targetHint = null;

        if (_passiveConditionsByBindingId == null
            || string.IsNullOrWhiteSpace(bindingId)
            || !_passiveConditionsByBindingId.TryGetValue(bindingId, out var conditions)
            || conditions == null
            || conditions.Count == 0)
        {
            return true;
        }

        Damageable selfDamageable = GetComponent<Damageable>();

        for (int i = 0; i < conditions.Count; i++)
        {
            var cond = conditions[i];
            if (cond == null)
            {
                continue;
            }

            if (cond.conditionType < 0 || cond.conditionType > (int)NpcPassiveAbilityConditionType.HasTargetInRole)
            {
                return false;
            }

            switch ((NpcPassiveAbilityConditionType)cond.conditionType)
            {
                case NpcPassiveAbilityConditionType.SelfHpBelowPercent:
                {
                    if (selfDamageable == null || selfDamageable.MaxHealth <= 0f)
                    {
                        return false;
                    }

                    float threshold = Mathf.Clamp01(cond.floatValue);
                    float pct = selfDamageable.Health / selfDamageable.MaxHealth;
                    if (!(pct < threshold))
                    {
                        return false;
                    }
                    break;
                }
                case NpcPassiveAbilityConditionType.SelfHpAbovePercent:
                {
                    if (selfDamageable == null || selfDamageable.MaxHealth <= 0f)
                    {
                        return false;
                    }

                    float threshold = Mathf.Clamp01(cond.floatValue);
                    float pct = selfDamageable.Health / selfDamageable.MaxHealth;
                    if (!(pct > threshold))
                    {
                        return false;
                    }
                    break;
                }
                case NpcPassiveAbilityConditionType.HasTargetInRole:
                {
                    var role = (DetectionZoneBinding.Role)cond.role;
                    List<Collider2D> targets = _agent != null ? _agent.GetDetectedTargetsForRole(role) : null;

                    int minCount = cond.intValue > 0 ? cond.intValue : 1;
                    if (targets == null || targets.Count < minCount)
                    {
                        return false;
                    }

                    if (targetHint == null)
                    {
                        targetHint = ResolveBestTarget(targets);
                    }
                    break;
                }
            }
        }

        return true;
    }

    private Transform ResolveBuffTargetRoot(NpcAbilityEntry binding, NpcPassiveAbilityBindingEntry passiveBinding, Transform conditionTargetHint)
    {
        if (binding == null || passiveBinding == null)
        {
            return null;
        }

        var targetMode = (NpcPassiveAbilityTargetMode)Mathf.Clamp(passiveBinding.targetMode, 0, (int)NpcPassiveAbilityTargetMode.CurrentTarget);
        if (targetMode == NpcPassiveAbilityTargetMode.Self)
        {
            return transform;
        }

        Transform candidate = conditionTargetHint;

        DetectionZoneBinding.Role triggerRole = MapTriggerRole(binding.triggerRole);
        if (candidate == null && triggerRole != DetectionZoneBinding.Role.Custom)
        {
            List<Collider2D> targets = _agent != null ? _agent.GetDetectedTargetsForRole(triggerRole) : null;
            candidate = ResolveBestTarget(targets);
        }

        return ResolveTargetRoot(candidate);
    }

    private static Transform ResolveTargetRoot(Transform candidate)
    {
        if (candidate == null)
        {
            return null;
        }

        PlayerController player = candidate.GetComponentInParent<PlayerController>();
        if (player != null)
        {
            return player.transform;
        }

        EnemyAgentBase enemy = candidate.GetComponentInParent<EnemyAgentBase>();
        if (enemy != null)
        {
            return enemy.transform;
        }

        return candidate;
    }

    private void TickActiveBuffs(float now)
    {
        if (_activeBuffsByBindingId.Count == 0)
        {
            return;
        }

        _tmpBuffKeys.Clear();
        foreach (var kvp in _activeBuffsByBindingId)
        {
            _tmpBuffKeys.Add(kvp.Key);
        }

        for (int i = 0; i < _tmpBuffKeys.Count; i++)
        {
            string bindingId = _tmpBuffKeys[i];
            if (string.IsNullOrWhiteSpace(bindingId))
            {
                continue;
            }

            if (!_activeBuffsByBindingId.TryGetValue(bindingId, out var state) || state == null)
            {
                _activeBuffsByBindingId.Remove(bindingId);
                continue;
            }

            if (state.targetRoot == null)
            {
                RemoveActiveBuff(bindingId, spawnExpireVfx: false);
                continue;
            }

            Damageable targetDamageable = state.targetRoot.GetComponent<Damageable>();
            if (targetDamageable != null && !targetDamageable.IsAlive)
            {
                RemoveActiveBuff(bindingId, spawnExpireVfx: true);
                continue;
            }

            if (state.expiresAtTime > 0f && now >= state.expiresAtTime)
            {
                RemoveActiveBuff(bindingId, spawnExpireVfx: true);
            }
        }

        _tmpBuffKeys.Clear();
    }

    private void ApplyBuff(string bindingId, string sourceId, AbilityBuffDefinition def, Transform targetRoot)
    {
        if (string.IsNullOrWhiteSpace(bindingId) || string.IsNullOrWhiteSpace(sourceId) || def == null || targetRoot == null)
        {
            return;
        }

        if (_activeBuffsByBindingId.ContainsKey(bindingId))
        {
            RemoveActiveBuff(bindingId, spawnExpireVfx: false);
        }

        StatModifierLayer stats = targetRoot.GetComponent<StatModifierLayer>();
        if (stats == null)
        {
            stats = targetRoot.gameObject.AddComponent<StatModifierLayer>();
        }

        ParseStatModifiers(def.modifiersJson, bindingId, out float moveSpeedMultiplier, out float attackMultiplier);

        stats.SetMoveSpeedMultiplier(sourceId, moveSpeedMultiplier);
        stats.SetAttackMultiplier(sourceId, attackMultiplier);

        GameObject loopVfx = AbilityBuffVfx.SpawnLoop(def, targetRoot);

        float expiresAtTime = def.duration > 0f ? Time.time + def.duration : 0f;

        _activeBuffsByBindingId[bindingId] = new ActiveBuffState
        {
            bindingId = bindingId,
            sourceId = sourceId,
            def = def,
            targetRoot = targetRoot,
            stats = stats,
            expiresAtTime = expiresAtTime,
            loopVfx = loopVfx
        };
    }

    private void RemoveActiveBuff(string bindingId, bool spawnExpireVfx)
    {
        if (string.IsNullOrWhiteSpace(bindingId))
        {
            return;
        }

        if (!_activeBuffsByBindingId.TryGetValue(bindingId, out var state) || state == null)
        {
            _activeBuffsByBindingId.Remove(bindingId);
            return;
        }

        Vector3 expirePos = state.targetRoot != null ? state.targetRoot.position : transform.position;
        if (state.loopVfx != null)
        {
            expirePos = state.loopVfx.transform.position;
        }

        if (state.stats != null && !string.IsNullOrWhiteSpace(state.sourceId))
        {
            state.stats.ClearMoveSpeedMultiplier(state.sourceId);
            state.stats.ClearAttackMultiplier(state.sourceId);
        }

        AbilityBuffVfx.DestroyLoop(state.loopVfx);

        if (spawnExpireVfx && state.def != null)
        {
            AbilityBuffVfx.SpawnExpire(state.def, expirePos);
        }

        _activeBuffsByBindingId.Remove(bindingId);
    }

    private void ClearAllActiveBuffs(bool spawnExpireVfx)
    {
        if (_activeBuffsByBindingId.Count == 0)
        {
            return;
        }

        _tmpBuffKeys.Clear();
        foreach (var kvp in _activeBuffsByBindingId)
        {
            _tmpBuffKeys.Add(kvp.Key);
        }

        for (int i = 0; i < _tmpBuffKeys.Count; i++)
        {
            RemoveActiveBuff(_tmpBuffKeys[i], spawnExpireVfx);
        }

        _tmpBuffKeys.Clear();
    }

    private static void ParseStatModifiers(string modifiersJson, string contextId, out float moveSpeedMultiplier, out float attackMultiplier)
    {
        moveSpeedMultiplier = 1f;
        attackMultiplier = 1f;

        if (string.IsNullOrWhiteSpace(modifiersJson))
        {
            return;
        }

        Dictionary<string, object> obj = CastleDbJsonUtil.TryParseJsonObject(modifiersJson);
        if (obj == null)
        {
            Debug.LogError($"[NpcAbilityController] modifiersJson must be a JSON object (bindingId='{contextId}')");
            return;
        }

        if (TryReadFloat(obj, MoveSpeedMultiplierKey, out float rawMoveSpeedMultiplier))
        {
            moveSpeedMultiplier = Mathf.Max(0f, rawMoveSpeedMultiplier);
        }

        if (TryReadFloat(obj, AttackMultiplierKey, out float rawAttackMultiplier))
        {
            attackMultiplier = Mathf.Max(0f, rawAttackMultiplier);
        }
    }

    private static bool TryReadFloat(Dictionary<string, object> obj, string key, out float value)
    {
        value = 0f;

        if (obj == null || string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        if (!obj.TryGetValue(key, out object raw) || raw == null)
        {
            return false;
        }

        if (raw is float f)
        {
            value = f;
            return true;
        }

        if (raw is double d)
        {
            value = (float)d;
            return true;
        }

        if (raw is int i)
        {
            value = i;
            return true;
        }

        if (raw is long l)
        {
            value = l;
            return true;
        }

        return float.TryParse(raw.ToString(), out value);
    }

    private string BuildBuffSourceId(string bindingId)
    {
        int instanceId = gameObject.GetInstanceID();
        if (string.IsNullOrWhiteSpace(bindingId))
        {
            return instanceId.ToString();
        }

        return $"{bindingId}@{instanceId}";
    }

    private void ReleasePendingCast()
    {
        if (!_hasPendingCast)
        {
            return;
        }

        var cast = _pendingCast;
        _hasPendingCast = false;
        _pendingCast = default;

        switch (cast.kind)
        {
            case PendingCastKind.Projectile:
                if (cast.projectile == null || string.IsNullOrWhiteSpace(cast.abilityId))
                {
                    return;
                }

                SpawnProjectile(cast.abilityId, cast.projectile, cast.onHitNodes, cast.directionSign);
                return;

            case PendingCastKind.Buff:
                ApplyPendingBuff(cast);
                return;
        }
    }

    private void SpawnProjectile(
        string abilityId,
        AbilityProjectileDefinition projectileDef,
        IReadOnlyList<AbilityOnHitNode> onHitNodes,
        float directionSign)
    {
        if (projectileDef == null || string.IsNullOrWhiteSpace(projectileDef.prefabPath))
        {
            return;
        }

        GameObject prefab = ResolvePrefab(projectileDef.prefabPath);
        if (prefab == null)
        {
            return;
        }

        Transform spawnPoint = ResolveFirePoint();
        Vector3 spawnPosition = spawnPoint != null ? spawnPoint.position : transform.position;

        GameObject projectile = SpawnProjectileInstance(
            projectileDef.prefabPath,
            prefab,
            spawnPosition,
            prefab.transform.rotation,
            out PrefabGameObjectPool pool);

        Vector3 scale = projectile.transform.localScale;
        scale.x = Mathf.Abs(scale.x) * (directionSign >= 0f ? 1f : -1f);
        projectile.transform.localScale = scale;

        var legacy = projectile.GetComponent<Projectile>();
        if (legacy != null)
        {
            legacy.enabled = false;
        }

        var controller = projectile.GetComponent<AbilityProjectileController>();
        if (controller == null)
        {
            controller = projectile.AddComponent<AbilityProjectileController>();
        }

        controller.SetRecycler(pool);
        controller.SetVfxPool(GetOrCreateVfxPool());
        controller.Initialize(gameObject, abilityId, projectileDef, onHitNodes);
    }

    private GameObject SpawnProjectileInstance(
        string prefabPath,
        GameObject prefab,
        Vector3 position,
        Quaternion rotation,
        out PrefabGameObjectPool pool)
    {
        pool = null;

        if (!useProjectilePool || projectilePoolMaxSize <= 0 || prefab == null || string.IsNullOrWhiteSpace(prefabPath))
        {
            return Instantiate(prefab, position, rotation);
        }

        if (!_projectilePoolsByPrefabPath.TryGetValue(prefabPath, out pool) || pool == null)
        {
            pool = new PrefabGameObjectPool(prefab, transform, $"[Pool] {name}.Projectiles", projectilePoolMaxSize);
            _projectilePoolsByPrefabPath[prefabPath] = pool;
        }

        return pool.Get(position, rotation);
    }

    private VfxPoolService GetOrCreateVfxPool()
    {
        if (!useVfxPool || vfxPoolMaxSize <= 0)
        {
            return null;
        }

        if (_vfxPool == null)
        {
            _vfxPool = new VfxPoolService(transform, vfxPoolMaxSize);
        }

        return _vfxPool;
    }

    private GameObject ResolvePrefab(string prefabPath)
    {
        if (string.IsNullOrWhiteSpace(prefabPath))
        {
            return null;
        }

        if (_prefabCache.TryGetValue(prefabPath, out var prefab) && prefab != null)
        {
            return prefab;
        }

        prefab = ResourcesGameAssetProvider.Shared.Load<GameObject>(prefabPath);
        if (prefab != null)
        {
            _prefabCache[prefabPath] = prefab;
        }

        return prefab;
    }

    private Transform ResolveFirePoint()
    {
        if (firePointOverride != null)
        {
            return firePointOverride;
        }

        if (!_searchedFirePoint)
        {
            _searchedFirePoint = true;
            _cachedFirePoint = transform.Find("FirePoint");
        }

        if (_cachedFirePoint != null)
        {
            return _cachedFirePoint;
        }

        return transform;
    }

    private Transform ResolveBestTarget(List<Collider2D> colliders)
    {
        if (colliders == null || colliders.Count == 0)
        {
            return null;
        }

        Transform best = null;
        float bestDistSq = float.MaxValue;
        Vector3 origin = transform.position;

        for (int i = 0; i < colliders.Count; i++)
        {
            var col = colliders[i];
            if (col == null || col.transform == null)
            {
                continue;
            }

            float distSq = (col.transform.position - origin).sqrMagnitude;
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                best = col.transform;
            }
        }

        return best;
    }

    private void EnsureCatalogLoaded()
    {
        if (_catalog != null && _abilitiesById != null)
        {
            return;
        }

        _catalog = abilityCatalogOverride != null
            ? abilityCatalogOverride
            : ResourcesGameAssetProvider.Shared.AbilityCatalog;
        if (_catalog == null)
        {
            if (!_loggedMissingCatalog)
            {
                Debug.LogError($"[NpcAbilityController] 资源加载失败：'{AbilityCatalogResourcePath}'。", this);
                _loggedMissingCatalog = true;
            }
            _abilitiesById = null;
            return;
        }

        _abilitiesById = new Dictionary<string, AbilityCatalogEntry>();
        if (_catalog.entries != null)
        {
            foreach (var entry in _catalog.entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.id))
                {
                    continue;
                }

                if (!_abilitiesById.ContainsKey(entry.id))
                {
                    _abilitiesById.Add(entry.id, entry);
                }
            }
        }
    }

    private static DetectionZoneBinding.Role MapTriggerRole(int triggerRole)
    {
        return triggerRole switch
        {
            0 => DetectionZoneBinding.Role.PrimaryAttack,
            1 => DetectionZoneBinding.Role.SecondaryAttack,
            2 => DetectionZoneBinding.Role.Custom,
            _ => DetectionZoneBinding.Role.Custom
        };
    }
}
