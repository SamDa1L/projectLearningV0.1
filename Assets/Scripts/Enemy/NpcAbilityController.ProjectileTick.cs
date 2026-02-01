using System.Collections.Generic;
using CastleDB.Runtime;
using UnityEngine;

public partial class NpcAbilityController : MonoBehaviour
{

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
