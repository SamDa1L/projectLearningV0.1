using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Projectile 能力（0.5 Phase 2）
/// - 结构化路径：从 AbilityCatalogEntry.projectileId → AbilityCatalog.projectiles 加载定义并实例化
/// - 兼容路径：从 paramsJson.projectile.prefabPath（Resources 路径）加载并实例化（旧数据/旧产物）
/// - 响应 RangedAttack Hook（F 键默认绑定）
/// </summary>
public class ProjectileRangedAttackAbility : IPlayerAbility
{
    private readonly PlayerController playerController;
    private readonly Animator animator;
    private readonly string prefabResourcesPath;
    private readonly AbilityProjectileDefinition projectileDef;
    private readonly AbilityOnHitSequenceDefinition onHitSequence;
    private readonly float cooldownSeconds;

    private float nextReadyTime;

    private GameObject cachedPrefab;
    private bool loggedMissingPrefab;

    private const int DefaultProjectilePoolMaxSize = 16;
    private PrefabGameObjectPool _projectilePool;
    private GameObject _pooledPrefab;

    private const int DefaultVfxPoolMaxSize = 32;
    private VfxPoolService _vfxPool;

    public string AbilityId { get; private set; }
    public int Priority { get; private set; }
    public bool Enabled { get; set; }
    public float CooldownSeconds => cooldownSeconds;
    public float CooldownRemaining => cooldownSeconds > 0f ? Mathf.Max(0f, nextReadyTime - Time.time) : 0f;

    /// <summary>
    /// 兼容构造：仅使用 prefabPath（旧数据/旧产物）
    /// </summary>
    public ProjectileRangedAttackAbility(PlayerController playerController, string abilityId, int priority, bool enabled, string prefabResourcesPath)
    {
        this.playerController = playerController;
        this.animator = playerController != null ? playerController.GetComponent<Animator>() : null;
        this.prefabResourcesPath = prefabResourcesPath ?? "";
        this.projectileDef = null;
        this.onHitSequence = null;
        this.cooldownSeconds = 0f;

        AbilityId = abilityId;
        Priority = priority;
        Enabled = enabled;
    }

    /// <summary>
    /// 0.5 构造：使用结构化投射物定义（支持速度/生命周期/VFX/OnHitSequence）
    /// </summary>
    public ProjectileRangedAttackAbility(
        PlayerController playerController,
        string abilityId,
        int priority,
        bool enabled,
        AbilityProjectileDefinition projectileDef,
        float cooldownSeconds,
        AbilityOnHitSequenceDefinition onHitSequence)
    {
        this.playerController = playerController;
        this.animator = playerController != null ? playerController.GetComponent<Animator>() : null;
        this.projectileDef = projectileDef;
        this.onHitSequence = onHitSequence;
        this.cooldownSeconds = Mathf.Max(0f, cooldownSeconds);
        this.prefabResourcesPath = projectileDef != null ? projectileDef.prefabPath ?? "" : "";

        AbilityId = abilityId;
        Priority = priority;
        Enabled = enabled;
    }

    public bool OnRangedAttack(AbilityInput input)
    {
        if (input.Phase != AbilityInputPhase.Started)
        {
            return false;
        }

        if (cooldownSeconds > 0f && Time.time < nextReadyTime)
        {
            return true; // 消费输入（避免回退能力误触发）
        }

        if (!TrySpawnProjectile())
        {
            return false;
        }

        if (cooldownSeconds > 0f)
        {
            nextReadyTime = Time.time + cooldownSeconds;
        }

        if (animator != null)
        {
            animator.SetTrigger(AnimationStrings.rangedAttackTrigger);
        }
        else
        {
            Debug.LogWarning($"[ProjectileRangedAttackAbility] Animator not found (abilityId='{AbilityId}')");
        }

        return true;
    }

    private bool TrySpawnProjectile()
    {
        if (playerController == null)
        {
            Debug.LogError($"[ProjectileRangedAttackAbility] playerController is null (abilityId='{AbilityId}')");
            return false;
        }

        if (string.IsNullOrWhiteSpace(prefabResourcesPath))
        {
            Debug.LogError($"[ProjectileRangedAttackAbility] prefabPath is empty (abilityId='{AbilityId}')");
            return false;
        }

        // 0.5 Phase 2：优先走 AnimationEvent 发射（修复“火球施放会额外发箭”）
        // - 输入时只排队，不 Instantiate
        // - 动画事件调用 PlayerController.OnAbilityRelease() 时真正生成投射物
        if (animator != null && TryQueueProjectileForAbilityRelease())
        {
            return true;
        }

        if (cachedPrefab == null)
        {
            cachedPrefab = ResourcesGameAssetProvider.Shared.Load<GameObject>(prefabResourcesPath);
            if (cachedPrefab == null)
            {
                    if (!loggedMissingPrefab)
                    {
                        Debug.LogError(
                            $"[ProjectileRangedAttackAbility] 资源加载失败: '{prefabResourcesPath}' (abilityId='{AbilityId}')");
                        loggedMissingPrefab = true;
                    }
                    return false;
                }
            }

        Transform launchPoint = ResolveLaunchPoint();
        Vector3 spawnPosition = launchPoint != null ? launchPoint.position : playerController.transform.position;

        PrefabGameObjectPool pool = GetOrCreateProjectilePool(cachedPrefab);
        GameObject projectile = pool != null ? pool.Get(spawnPosition, cachedPrefab.transform.rotation) : null;
        if (projectile == null)
        {
            projectile = Object.Instantiate(cachedPrefab, spawnPosition, cachedPrefab.transform.rotation);
        }

        float dirSign = playerController.IsFacingRight ? 1f : -1f;
        Vector3 scale = projectile.transform.localScale;
        scale.x = Mathf.Abs(scale.x) * dirSign;
        projectile.transform.localScale = scale;

        // 结构化路径：禁用旧 Projectile 脚本，启用 AbilityProjectileController 统一结算
        if (projectileDef != null)
        {
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

            IReadOnlyList<AbilityOnHitNode> nodes = onHitSequence != null ? onHitSequence.nodes : null;
            controller.SetRecycler(pool);
            controller.SetVfxPool(GetOrCreateVfxPool());
            controller.Initialize(playerController.gameObject, AbilityId, projectileDef, nodes);
        }
        else
        {
            // 兼容旧投射物：复用时需要重新设置速度，并确保命中后能回收到对象池。
            var legacy = projectile.GetComponent<Projectile>();
            if (legacy != null)
            {
                legacy.SetRecycler(pool);
                legacy.ResetForSpawn();
            }
        }

        return true;
    }

    private bool TryQueueProjectileForAbilityRelease()
    {
        if (playerController == null)
        {
            return false;
        }

        GameObject prefab = cachedPrefab;
        if (prefab == null)
        {
            prefab = ResourcesGameAssetProvider.Shared.Load<GameObject>(prefabResourcesPath);
            if (prefab == null)
            {
                if (!loggedMissingPrefab)
                {
                    Debug.LogError(
                        $"[ProjectileRangedAttackAbility] 资源加载失败: '{prefabResourcesPath}' (abilityId='{AbilityId}')");
                    loggedMissingPrefab = true;
                }

                return false;
            }

            cachedPrefab = prefab;
        }

        Transform launchPoint = ResolveLaunchPoint();
        AbilityProjectileDefinition def = projectileDef;
        IReadOnlyList<AbilityOnHitNode> nodes = onHitSequence != null ? onHitSequence.nodes : null;

        return playerController.QueueAbilityRelease(
            AbilityId,
            () =>
            {
                if (playerController == null)
                {
                    return;
                }

                Transform spawnPoint = launchPoint != null ? launchPoint : playerController.transform;
                Vector3 spawnPosition = spawnPoint.position;

                PrefabGameObjectPool pool = GetOrCreateProjectilePool(prefab);
                GameObject projectile = pool != null ? pool.Get(spawnPosition, prefab.transform.rotation) : null;
                if (projectile == null)
                {
                    projectile = Object.Instantiate(prefab, spawnPosition, prefab.transform.rotation);
                }

                float dirSign = playerController.IsFacingRight ? 1f : -1f;
                Vector3 scale = projectile.transform.localScale;
                scale.x = Mathf.Abs(scale.x) * dirSign;
                projectile.transform.localScale = scale;

                var legacy = projectile.GetComponent<Projectile>();
                if (def == null)
                {
                    if (legacy != null)
                    {
                        legacy.SetRecycler(pool);
                        legacy.ResetForSpawn();
                    }
                    return;
                }

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
                controller.Initialize(playerController.gameObject, AbilityId, def, nodes);
            },
            expirySeconds: 1.5f);
    }

    private Transform ResolveLaunchPoint()
    {
        if (playerController == null)
        {
            return null;
        }

        return playerController.AbilityFirePoint;
    }

    public bool OnMove(AbilityInput input) => false;
    public bool OnRun(AbilityInput input) => false;
    public bool OnJump(AbilityInput input) => false;
    public bool OnAttack(AbilityInput input) => false;

    private PrefabGameObjectPool GetOrCreateProjectilePool(GameObject prefab)
    {
        if (playerController == null || prefab == null)
        {
            return null;
        }

        if (_projectilePool == null || _pooledPrefab != prefab)
        {
            _pooledPrefab = prefab;
            _projectilePool = new PrefabGameObjectPool(
                prefab,
                playerController.transform,
                $"[Pool] PlayerProjectiles({AbilityId})",
                DefaultProjectilePoolMaxSize);
        }

        return _projectilePool;
    }

    private VfxPoolService GetOrCreateVfxPool()
    {
        if (playerController == null)
        {
            return null;
        }

        if (_vfxPool == null)
        {
            _vfxPool = new VfxPoolService(playerController.transform, DefaultVfxPoolMaxSize);
        }

        return _vfxPool;
    }
}
