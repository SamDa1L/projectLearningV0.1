using System.Collections.Generic;
using CastleDB.Runtime;
using UnityEngine;

public class NpcAbilityController : MonoBehaviour
{
    private const string AbilityCatalogResourcePath = "Config/EnemyAbilityCatalog";

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
        public float releaseAtTime;
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
    /// Ticks only the pending cast timer/release (if any), independent from DetectionZone role/targets.
    /// Returns true if there is (or was) a pending cast, so the caller can skip melee for this frame.
    /// </summary>
    public bool TickPendingCast()
    {
        if (!_hasPendingCast)
        {
            return false;
        }

        if (_pendingCast.releaseAtTime > 0f && Time.time >= _pendingCast.releaseAtTime)
        {
            ReleasePendingCast();
        }

        return true;
    }

    /// <summary>
    /// AnimationEvent entry: releases the currently queued projectile cast (if any).
    /// Keep the name aligned with PlayerController.OnAbilityRelease().
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
    /// Tick NPC projectile abilities for a specific DetectionZone role.
    /// Returns true if this controller is responsible for the role (so the caller should skip legacy melee attack).
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
            if (_pendingCast.releaseAtTime > 0f && Time.time >= _pendingCast.releaseAtTime)
            {
                ReleasePendingCast();
            }

            return true;
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

        if (releaseDelay > 0f)
        {
            _hasPendingCast = true;
            _pendingCast = new PendingCast
            {
                abilityId = bestBinding.abilityId ?? "",
                projectile = bestProjectile,
                onHitNodes = bestOnHitNodes,
                releaseAtTime = now + releaseDelay,
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
        if (binding == null || ability == null)
        {
            return 0f;
        }

        if (binding.cooldownOverride > 0f)
        {
            return binding.cooldownOverride;
        }

        return Mathf.Max(0f, ability.cooldown);
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
