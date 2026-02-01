using System.Collections.Generic;
using UnityEngine;
using CastleDB.Runtime;

/// <summary>
/// AttackOverride 能力（0.5 Phase 6）。
/// 最小闭环：当 Attack Hook 触发时，通过生成配置化投射物来替代近战攻击。
/// - kind=AttackOverride
/// - projectileId -> AbilityCatalog.projectiles
/// - paramsJson 支持：
///   { "damageMultiplier": 1.5, "animTrigger":"rangedAttack" }
/// </summary>
public class AttackOverrideAbility : IPlayerAbility
{
    private const string DamageMultiplierKey = "damageMultiplier";
    private const string AnimTriggerKey = "animTrigger";

    private readonly PlayerController _playerController;
    private readonly Animator _animator;
    private readonly AbilityProjectileDefinition _projectileDef;
    private readonly AbilityOnHitSequenceDefinition _onHitSequence;
    private readonly float _cooldownSeconds;
    private readonly float _damageMultiplier;
    private readonly string _animTrigger;

    private float _nextReadyTime;
    private GameObject _cachedPrefab;
    private bool _loggedMissingPrefab;

    private const int DefaultProjectilePoolMaxSize = 16;
    private PrefabGameObjectPool _projectilePool;
    private GameObject _pooledPrefab;

    private const int DefaultVfxPoolMaxSize = 32;
    private VfxPoolService _vfxPool;

    public string AbilityId { get; }
    public int Priority { get; }
    public bool Enabled { get; set; }
    public float CooldownSeconds => _cooldownSeconds;
    public float CooldownRemaining => _cooldownSeconds > 0f ? Mathf.Max(0f, _nextReadyTime - Time.time) : 0f;

    public AttackOverrideAbility(
        PlayerController playerController,
        string abilityId,
        int priority,
        bool enabled,
        AbilityProjectileDefinition projectileDef,
        float cooldownSeconds,
        AbilityOnHitSequenceDefinition onHitSequence,
        string paramsJson)
    {
        _playerController = playerController;
        _animator = playerController != null ? playerController.GetComponent<Animator>() : null;
        _projectileDef = projectileDef;
        _onHitSequence = onHitSequence;
        _cooldownSeconds = Mathf.Max(0f, cooldownSeconds);

        ParseParams(paramsJson, out _damageMultiplier, out _animTrigger);

        AbilityId = abilityId ?? "";
        Priority = priority;
        Enabled = enabled;
    }

    private bool TryCast(AbilityInput input)
    {
        if (input.Phase != AbilityInputPhase.Started)
        {
            return false;
        }

        if (_playerController != null && !_playerController.IsAlive)
        {
            return true;
        }

        if (_cooldownSeconds > 0f && Time.time < _nextReadyTime)
        {
            return true;
        }

        if (!TrySpawnProjectile())
        {
            return false;
        }

        if (_cooldownSeconds > 0f)
        {
            _nextReadyTime = Time.time + _cooldownSeconds;
        }

        if (_animator != null && !string.IsNullOrWhiteSpace(_animTrigger))
        {
            _animator.SetTrigger(_animTrigger);
        }

        return true;
    }

    private bool TrySpawnProjectile()
    {
        if (_playerController == null)
        {
            Debug.LogError($"[AttackOverrideAbility] playerController is null (abilityId='{AbilityId}')");
            return false;
        }

        if (_projectileDef == null)
        {
            Debug.LogError($"[AttackOverrideAbility] projectileDef is null (abilityId='{AbilityId}')");
            return false;
        }

        string prefabPath = _projectileDef.prefabPath ?? "";
        if (string.IsNullOrWhiteSpace(prefabPath))
        {
            Debug.LogError($"[AttackOverrideAbility] projectile prefabPath is empty (abilityId='{AbilityId}', projectileId='{_projectileDef.id ?? ""}')");
            return false;
        }

        // 0.5：优先走动画事件的释放时机（与 ProjectileRangedAttackAbility 相同机制）
        if (_animator != null && TryQueueProjectileForAbilityRelease(prefabPath))
        {
            return true;
        }

        if (_cachedPrefab == null)
        {
            _cachedPrefab = ResourcesGameAssetProvider.Shared.Load<GameObject>(prefabPath);
                if (_cachedPrefab == null)
                {
                    if (!_loggedMissingPrefab)
                    {
                        Debug.LogError($"[AttackOverrideAbility] 资源加载失败: '{prefabPath}' (abilityId='{AbilityId}')");
                        _loggedMissingPrefab = true;
                    }
                    return false;
                }
            }

        SpawnProjectileInstance(_cachedPrefab);
        return true;
    }

    private bool TryQueueProjectileForAbilityRelease(string prefabPath)
    {
        if (_playerController == null)
        {
            return false;
        }

        GameObject prefab = _cachedPrefab;
        if (prefab == null)
        {
            prefab = ResourcesGameAssetProvider.Shared.Load<GameObject>(prefabPath);
            if (prefab == null)
            {
                if (!_loggedMissingPrefab)
                {
                    Debug.LogError($"[AttackOverrideAbility] 资源加载失败: '{prefabPath}' (abilityId='{AbilityId}')");
                    _loggedMissingPrefab = true;
                }

                return false;
            }

            _cachedPrefab = prefab;
        }

        return _playerController.QueueAbilityRelease(
            AbilityId,
            () =>
            {
                if (_playerController == null)
                {
                    return;
                }

                SpawnProjectileInstance(prefab);
            },
            expirySeconds: 1.5f);
    }

    private void SpawnProjectileInstance(GameObject prefab)
    {
        if (prefab == null || _playerController == null)
        {
            return;
        }

        Transform launchPoint = _playerController.AbilityFirePoint;
        Vector3 spawnPosition = launchPoint != null ? launchPoint.position : _playerController.transform.position;

        PrefabGameObjectPool pool = GetOrCreateProjectilePool(prefab);
        GameObject projectile = pool != null ? pool.Get(spawnPosition, prefab.transform.rotation) : null;
        if (projectile == null)
        {
            projectile = Object.Instantiate(prefab, spawnPosition, prefab.transform.rotation);
        }

        float dirSign = _playerController.IsFacingRight ? 1f : -1f;
        Vector3 scale = projectile.transform.localScale;
        scale.x = Mathf.Abs(scale.x) * dirSign;
        projectile.transform.localScale = scale;

        // 结构化路径：禁用旧 Projectile 脚本，启用 AbilityProjectileController 统一结算。
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

        IReadOnlyList<AbilityOnHitNode> nodes = _onHitSequence != null ? _onHitSequence.nodes : null;
        controller.SetRecycler(pool);
        controller.SetVfxPool(GetOrCreateVfxPool());
        controller.Initialize(_playerController.gameObject, AbilityId, CreateScaledDefinition(_projectileDef, _damageMultiplier), nodes);
    }

    private PrefabGameObjectPool GetOrCreateProjectilePool(GameObject prefab)
    {
        if (_playerController == null || prefab == null)
        {
            return null;
        }

        if (_projectilePool == null || _pooledPrefab != prefab)
        {
            _pooledPrefab = prefab;
            _projectilePool = new PrefabGameObjectPool(
                prefab,
                _playerController.transform,
                $"[Pool] PlayerProjectiles({AbilityId})",
                DefaultProjectilePoolMaxSize);
        }

        return _projectilePool;
    }

    private VfxPoolService GetOrCreateVfxPool()
    {
        if (_playerController == null)
        {
            return null;
        }

        if (_vfxPool == null)
        {
            _vfxPool = new VfxPoolService(_playerController.transform, DefaultVfxPoolMaxSize);
        }

        return _vfxPool;
    }

    private static AbilityProjectileDefinition CreateScaledDefinition(AbilityProjectileDefinition def, float multiplier)
    {
        if (def == null)
        {
            return null;
        }

        multiplier = Mathf.Max(0f, multiplier);
        int scaledDamage = Mathf.Max(0, Mathf.RoundToInt(def.baseDamage * multiplier));

        return new AbilityProjectileDefinition
        {
            id = def.id,
            prefabPath = def.prefabPath,
            speed = def.speed,
            lifetime = def.lifetime,
            baseDamage = scaledDamage,
            hitMask = def.hitMask,
            onHitVfxPath = def.onHitVfxPath,
            onHitVfxDuration = def.onHitVfxDuration,
            onExpireVfxPath = def.onExpireVfxPath,
            onExpireVfxDuration = def.onExpireVfxDuration,
            tags = def.tags
        };
    }

    private static void ParseParams(string paramsJson, out float damageMultiplier, out string animTrigger)
    {
        damageMultiplier = 1f;
        animTrigger = AnimationStrings.rangedAttackTrigger;

        if (string.IsNullOrWhiteSpace(paramsJson))
        {
            return;
        }

        Dictionary<string, object> obj = CastleDbJsonUtil.TryParseJsonObject(paramsJson);
        if (obj == null)
        {
            return;
        }

        if (TryReadFloat(obj, DamageMultiplierKey, out float mult) && mult >= 0f)
        {
            damageMultiplier = mult;
        }

        if (obj.TryGetValue(AnimTriggerKey, out var t) && t != null)
        {
            string s = t.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(s))
            {
                animTrigger = s;
            }
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

        switch (raw)
        {
            case float f:
                value = f;
                return true;
            case double d:
                value = (float)d;
                return true;
            case int i:
                value = i;
                return true;
            case long l:
                value = l;
                return true;
            case string s:
                return float.TryParse(s, out value);
        }

        return false;
    }

    public bool OnMove(AbilityInput input) => TryCast(input);
    public bool OnRun(AbilityInput input) => TryCast(input);
    public bool OnJump(AbilityInput input) => TryCast(input);
    public bool OnAttack(AbilityInput input) => TryCast(input);
    public bool OnRangedAttack(AbilityInput input) => TryCast(input);
}
