using System.Collections.Generic;
using CastleDB.Runtime;
using UnityEngine;

public class NpcAbilityController : MonoBehaviour
{
    private const string AbilityCatalogResourcePath = "Config/EnemyAbilityCatalog";
    private const float DefaultAbilityReleaseExpirySeconds = 1.5f;

    [Header("Optional Overrides")]
    [SerializeField] private AbilityCatalog abilityCatalogOverride;
    [SerializeField] private Transform firePointOverride;

    private EnemyAgentBase _agent;
    private Animator _animator;

    private AbilityCatalog _catalog;
    private Dictionary<string, AbilityCatalogEntry> _abilitiesById;
    private readonly Dictionary<string, float> _nextReadyTimeByBindingId = new Dictionary<string, float>();
    private readonly Dictionary<string, GameObject> _prefabCache = new Dictionary<string, GameObject>();

    private Transform _cachedFirePoint;
    private bool _searchedFirePoint;

    private PendingCast _pendingCast;
    private bool _hasPendingCast;
    private bool _loggedMissingCatalog;
    private bool _loggedMissingAgent;

    private struct PendingCast
    {
        public string abilityId;
        public AbilityProjectileDefinition projectile;
        public IReadOnlyList<AbilityOnHitNode> onHitNodes;
        public float fallbackReleaseAtTime;
        public float expiresAtTime;
        public float directionSign;
    }

    private void Awake()
    {
        _agent = GetComponent<EnemyAgentBase>();
        _animator = GetComponent<Animator>();
    }

    private void OnDisable()
    {
        _hasPendingCast = false;
        _pendingCast = default;
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
            string abilityId = _pendingCast.abilityId ?? "";
            _hasPendingCast = false;
            _pendingCast = default;

            Debug.LogWarning(
                $"[NpcAbilityController] 施法等待超时，可能缺少 AnimationEvent: OnAbilityRelease（abilityId='{abilityId}'）",
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
        if (!_hasPendingCast)
        {
            return;
        }

        ReleasePendingCast();
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

        string paramsJson = !string.IsNullOrWhiteSpace(bestBinding.paramsJson)
            ? bestBinding.paramsJson
            : bestAbility.paramsJson;

        string fallbackTrigger = !string.IsNullOrWhiteSpace(profile.castTrigger)
            ? profile.castTrigger
            : profile.animationTrigger;

        string animTrigger = ResolveAnimTrigger(paramsJson, fallback: fallbackTrigger);
        float releaseDelay = ResolveReleaseDelay(paramsJson, fallbackSeconds: 0f);

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
        // AbilityCatalogEntry.cooldown 仅用于玩家能力，这里不参与 NPC 施法冷却计算。
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

    private static string ResolveAnimTrigger(string paramsJson, string fallback)
    {
        if (string.IsNullOrWhiteSpace(paramsJson))
        {
            return fallback ?? "";
        }

        var obj = CastleDbJsonUtil.TryParseJsonObject(paramsJson);
        if (obj == null)
        {
            return fallback ?? "";
        }

        if (obj.TryGetValue("animTrigger", out var value) && value != null)
        {
            string trigger = value.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(trigger))
            {
                return trigger;
            }
        }

        return fallback ?? "";
    }

    private static float ResolveReleaseDelay(string paramsJson, float fallbackSeconds)
    {
        if (string.IsNullOrWhiteSpace(paramsJson))
        {
            return fallbackSeconds;
        }

        var obj = CastleDbJsonUtil.TryParseJsonObject(paramsJson);
        if (obj == null)
        {
            return fallbackSeconds;
        }

        if (!obj.TryGetValue("releaseDelay", out var value) || value == null)
        {
            return fallbackSeconds;
        }

        if (value is float f)
        {
            return Mathf.Max(0f, f);
        }

        if (value is double d)
        {
            return Mathf.Max(0f, (float)d);
        }

        if (value is int i)
        {
            return Mathf.Max(0f, i);
        }

        if (float.TryParse(value.ToString(), out float parsed))
        {
            return Mathf.Max(0f, parsed);
        }

        return fallbackSeconds;
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

        if (cast.projectile == null || string.IsNullOrWhiteSpace(cast.abilityId))
        {
            return;
        }

        SpawnProjectile(cast.abilityId, cast.projectile, cast.onHitNodes, cast.directionSign);
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

        GameObject projectile = Instantiate(prefab, spawnPosition, prefab.transform.rotation);

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

        controller.Initialize(gameObject, abilityId, projectileDef, onHitNodes);
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

        prefab = Resources.Load<GameObject>(prefabPath);
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
            : Resources.Load<AbilityCatalog>(AbilityCatalogResourcePath);
        if (_catalog == null)
        {
            if (!_loggedMissingCatalog)
            {
                Debug.LogError($"[NpcAbilityController] Resources.Load failed: '{AbilityCatalogResourcePath}'.", this);
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
