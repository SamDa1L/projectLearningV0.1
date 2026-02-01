using System.Collections.Generic;
using CastleDB.Runtime;
using UnityEngine;

public partial class NpcAbilityController : MonoBehaviour
{

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
}
