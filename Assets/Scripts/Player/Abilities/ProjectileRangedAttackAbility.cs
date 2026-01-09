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
    private Transform cachedLaunchPoint;
    private bool loggedMissingPrefab;

    public string AbilityId { get; private set; }
    public int Priority { get; private set; }
    public bool Enabled { get; set; }

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

        if (cachedPrefab == null)
        {
            cachedPrefab = Resources.Load<GameObject>(prefabResourcesPath);
            if (cachedPrefab == null)
            {
                if (!loggedMissingPrefab)
                {
                    Debug.LogError(
                        $"[ProjectileRangedAttackAbility] Resources.Load failed: '{prefabResourcesPath}' (abilityId='{AbilityId}')");
                    loggedMissingPrefab = true;
                }
                return false;
            }
        }

        Transform launchPoint = ResolveLaunchPoint();
        Vector3 spawnPosition = launchPoint != null ? launchPoint.position : playerController.transform.position;

        GameObject projectile = Object.Instantiate(cachedPrefab, spawnPosition, cachedPrefab.transform.rotation);

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
            controller.Initialize(playerController.gameObject, AbilityId, projectileDef, nodes);
        }

        return true;
    }

    private Transform ResolveLaunchPoint()
    {
        if (cachedLaunchPoint != null)
        {
            return cachedLaunchPoint;
        }

        // 优先：子物体上的 ProjectileLauncher（常见为 FirePoint），避免拿到根节点上的 Launcher（例如箭矢）。
        ProjectileLauncher[] launchers = playerController.GetComponentsInChildren<ProjectileLauncher>(true);
        foreach (var launcher in launchers)
        {
            if (launcher == null)
            {
                continue;
            }

            if (launcher.gameObject == playerController.gameObject)
            {
                continue;
            }

            cachedLaunchPoint = launcher.launchPoint != null ? launcher.launchPoint : launcher.transform;
            return cachedLaunchPoint;
        }

        // 回退：根节点上的 ProjectileLauncher（如果存在）
        ProjectileLauncher rootLauncher = playerController.GetComponent<ProjectileLauncher>();
        if (rootLauncher != null)
        {
            cachedLaunchPoint = rootLauncher.launchPoint != null ? rootLauncher.launchPoint : rootLauncher.transform;
            return cachedLaunchPoint;
        }

        // 最终回退：玩家自身
        cachedLaunchPoint = playerController.transform;
        return cachedLaunchPoint;
    }

    public bool OnMove(AbilityInput input) => false;
    public bool OnRun(AbilityInput input) => false;
    public bool OnJump(AbilityInput input) => false;
    public bool OnAttack(AbilityInput input) => false;
}
