using System.Collections.Generic;
using System.Reflection;
using CastleDB.Runtime;
using NUnit.Framework;
using UnityEngine;

public class AbilityProjectileControllerTests
{
    private static void InvokeOnTriggerEnter2D(AbilityProjectileController controller, Collider2D other)
    {
        var method = typeof(AbilityProjectileController).GetMethod(
            "OnTriggerEnter2D",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.IsNotNull(method, "OnTriggerEnter2D method not found");
        method.Invoke(controller, new object[] { other });
    }

    private static StatusCatalog CreateStatusCatalog()
    {
        var catalog = ScriptableObject.CreateInstance<StatusCatalog>();
        catalog.ApplyFromCastleDb(new List<StatusDefinition>
        {
            new StatusDefinition
            {
                id = "Freeze",
                displayName = "Freeze",
                defaultDuration = 1f,
                stackRule = StatusStackRule.Replace,
                maxStacks = 1,
                modifiers = new StatusModifiers(0.5f)
            }
        });
        return catalog;
    }

    private static GameObject CreateTarget(
        StatusCatalog statusCatalog,
        out Damageable damageable,
        out StatusEffectController statusController,
        out StatModifierLayer statLayer,
        out Collider2D collider)
    {
        var target = new GameObject("Target");
        collider = target.AddComponent<BoxCollider2D>();
        collider.isTrigger = false;

        statLayer = target.AddComponent<StatModifierLayer>();
        statusController = target.AddComponent<StatusEffectController>();
        statusController.Initialize(statusCatalog);

        damageable = target.AddComponent<Damageable>();
        damageable.Configure(new DamageableStats
        {
            maxHealth = 100f,
            invincibilityTime = 0f,
            knockbackMultiplier = 1f
        });

        return target;
    }

    private static GameObject CreateProjectile(
        GameObject owner,
        AbilityProjectileDefinition def,
        IReadOnlyList<AbilityOnHitNode> nodes,
        out AbilityProjectileController controller)
    {
        var projectile = new GameObject("Projectile");
        projectile.AddComponent<Rigidbody2D>();
        var projCollider = projectile.AddComponent<BoxCollider2D>();
        projCollider.isTrigger = true;
        controller = projectile.AddComponent<AbilityProjectileController>();
        controller.Initialize(owner, "TestAbility", def, nodes);
        return projectile;
    }

    [Test]
    public void ProjectileHit_AppliesDamageAndStatus()
    {
        GameObject owner = null;
        GameObject projectile = null;
        GameObject target = null;
        StatusCatalog statusCatalog = null;

        try
        {
            owner = new GameObject("Owner");
            statusCatalog = CreateStatusCatalog();

            target = CreateTarget(statusCatalog, out var damageable, out var statusController, out var statLayer, out var targetCollider);

            var def = new AbilityProjectileDefinition
            {
                id = "TestProjectile",
                prefabPath = "",
                speed = 5f,
                lifetime = 0f,
                baseDamage = 10,
                hitMask = "",
                onHitVfxPath = "",
                onExpireVfxPath = "",
                tags = ""
            };

            var nodes = new List<AbilityOnHitNode>
            {
                new AbilityOnHitNode
                {
                    order = 1,
                    nodeType = AbilityOnHitNodeType.ApplyStatus,
                    statusId = "Freeze",
                    aoeId = "",
                    summonId = "",
                    waitMode = "",
                    paramsJson = ""
                }
            };

            projectile = CreateProjectile(owner, def, nodes, out var controller);
            float initialHealth = damageable.Health;

            InvokeOnTriggerEnter2D(controller, targetCollider);

            Assert.Less(damageable.Health, initialHealth, "命中后应扣血");
            Assert.IsTrue(statusController.HasStatus("Freeze"), "命中后应附加 Freeze");
            Assert.Less(statLayer.MoveSpeedMultiplier, 1f, "Freeze modifiers 应影响 MoveSpeedMultiplier");
        }
        finally
        {
            if (projectile != null) Object.DestroyImmediate(projectile);
            if (target != null) Object.DestroyImmediate(target);
            if (owner != null) Object.DestroyImmediate(owner);
            if (statusCatalog != null) Object.DestroyImmediate(statusCatalog);
        }
    }

    [Test]
    public void ProjectileHit_TargetIsSiblingUnderSameRoot_DoesNotGetIgnoredAsSelfHit()
    {
        GameObject root = null;
        GameObject owner = null;
        GameObject projectile = null;
        GameObject target = null;
        StatusCatalog statusCatalog = null;

        try
        {
            root = new GameObject("Root");
            owner = new GameObject("Owner");
            owner.transform.SetParent(root.transform);

            statusCatalog = CreateStatusCatalog();
            target = CreateTarget(statusCatalog, out var damageable, out var statusController, out var statLayer, out var targetCollider);
            target.transform.SetParent(root.transform);

            var def = new AbilityProjectileDefinition
            {
                id = "TestProjectile",
                prefabPath = "",
                speed = 5f,
                lifetime = 0f,
                baseDamage = 10,
                hitMask = "",
                onHitVfxPath = "",
                onExpireVfxPath = "",
                tags = ""
            };

            var nodes = new List<AbilityOnHitNode>
            {
                new AbilityOnHitNode
                {
                    order = 1,
                    nodeType = AbilityOnHitNodeType.ApplyStatus,
                    statusId = "Freeze"
                }
            };

            projectile = CreateProjectile(owner, def, nodes, out var controller);
            float initialHealth = damageable.Health;

            InvokeOnTriggerEnter2D(controller, targetCollider);

            Assert.Less(damageable.Health, initialHealth, "同 Root 下的非 Owner 子物体不应被误判为自伤过滤");
            Assert.IsTrue(statusController.HasStatus("Freeze"), "命中后应附加 Freeze");
            Assert.Less(statLayer.MoveSpeedMultiplier, 1f, "Freeze modifiers 应影响 MoveSpeedMultiplier");
        }
        finally
        {
            if (projectile != null) Object.DestroyImmediate(projectile);
            if (target != null) Object.DestroyImmediate(target);
            if (owner != null) Object.DestroyImmediate(owner);
            if (root != null) Object.DestroyImmediate(root);
            if (statusCatalog != null) Object.DestroyImmediate(statusCatalog);
        }
    }

    [Test]
    public void ProjectileHit_TargetIsOwnerChild_IgnoresCollision()
    {
        GameObject root = null;
        GameObject owner = null;
        GameObject projectile = null;
        GameObject target = null;
        StatusCatalog statusCatalog = null;

        try
        {
            root = new GameObject("Root");
            owner = new GameObject("Owner");
            owner.transform.SetParent(root.transform);

            statusCatalog = CreateStatusCatalog();
            target = CreateTarget(statusCatalog, out var damageable, out var statusController, out var statLayer, out var targetCollider);
            target.transform.SetParent(owner.transform);

            var def = new AbilityProjectileDefinition
            {
                id = "TestProjectile",
                prefabPath = "",
                speed = 5f,
                lifetime = 0f,
                baseDamage = 10,
                hitMask = "",
                onHitVfxPath = "",
                onExpireVfxPath = "",
                tags = ""
            };

            var nodes = new List<AbilityOnHitNode>
            {
                new AbilityOnHitNode
                {
                    order = 1,
                    nodeType = AbilityOnHitNodeType.ApplyStatus,
                    statusId = "Freeze"
                }
            };

            projectile = CreateProjectile(owner, def, nodes, out var controller);
            float initialHealth = damageable.Health;

            InvokeOnTriggerEnter2D(controller, targetCollider);

            Assert.AreEqual(initialHealth, damageable.Health, 0.001f, "Owner 子物体应被过滤（防自伤）");
            Assert.IsFalse(statusController.HasStatus("Freeze"), "Owner 子物体被过滤时不应附加状态");
            Assert.AreEqual(1f, statLayer.MoveSpeedMultiplier, 0.001f, "Owner 子物体被过滤时不应修改 MoveSpeedMultiplier");
        }
        finally
        {
            if (projectile != null) Object.DestroyImmediate(projectile);
            if (target != null) Object.DestroyImmediate(target);
            if (owner != null) Object.DestroyImmediate(owner);
            if (root != null) Object.DestroyImmediate(root);
            if (statusCatalog != null) Object.DestroyImmediate(statusCatalog);
        }
    }

    [Test]
    public void ProjectileHit_WithHitMaskMismatch_IgnoresCollision()
    {
        GameObject owner = null;
        GameObject projectile = null;
        GameObject target = null;
        StatusCatalog statusCatalog = null;

        try
        {
            owner = new GameObject("Owner");
            statusCatalog = CreateStatusCatalog();

            target = CreateTarget(statusCatalog, out var damageable, out var statusController, out var statLayer, out var targetCollider);

            int enemyHitBoxLayer = LayerMask.NameToLayer("EnemyHitBox");
            Assert.GreaterOrEqual(enemyHitBoxLayer, 0, "Layer 'EnemyHitBox' not defined in TagManager");

            var def = new AbilityProjectileDefinition
            {
                id = "TestProjectile",
                prefabPath = "",
                speed = 5f,
                lifetime = 0f,
                baseDamage = 10,
                hitMask = "EnemyHitBox",
                onHitVfxPath = "",
                onExpireVfxPath = "",
                tags = ""
            };

            var nodes = new List<AbilityOnHitNode>
            {
                new AbilityOnHitNode
                {
                    order = 1,
                    nodeType = AbilityOnHitNodeType.ApplyStatus,
                    statusId = "Freeze"
                }
            };

            projectile = CreateProjectile(owner, def, nodes, out var controller);
            float initialHealth = damageable.Health;

            InvokeOnTriggerEnter2D(controller, targetCollider);

            Assert.AreEqual(initialHealth, damageable.Health, 0.001f, "hitMask 不匹配时不应扣血");
            Assert.IsFalse(statusController.HasStatus("Freeze"), "hitMask 不匹配时不应附加状态");
            Assert.AreEqual(1f, statLayer.MoveSpeedMultiplier, 0.001f, "hitMask 不匹配时不应修改 MoveSpeedMultiplier");
        }
        finally
        {
            if (projectile != null) Object.DestroyImmediate(projectile);
            if (target != null) Object.DestroyImmediate(target);
            if (owner != null) Object.DestroyImmediate(owner);
            if (statusCatalog != null) Object.DestroyImmediate(statusCatalog);
        }
    }

    [Test]
    public void ProjectileHit_WithHitMaskMatch_AllowsCollision()
    {
        GameObject owner = null;
        GameObject projectile = null;
        GameObject target = null;
        StatusCatalog statusCatalog = null;

        try
        {
            owner = new GameObject("Owner");
            statusCatalog = CreateStatusCatalog();

            target = CreateTarget(statusCatalog, out var damageable, out var statusController, out var statLayer, out var targetCollider);

            int enemyHitBoxLayer = LayerMask.NameToLayer("EnemyHitBox");
            Assert.GreaterOrEqual(enemyHitBoxLayer, 0, "Layer 'EnemyHitBox' not defined in TagManager");
            target.layer = enemyHitBoxLayer;

            var def = new AbilityProjectileDefinition
            {
                id = "TestProjectile",
                prefabPath = "",
                speed = 5f,
                lifetime = 0f,
                baseDamage = 10,
                hitMask = "EnemyHitBox",
                onHitVfxPath = "",
                onExpireVfxPath = "",
                tags = ""
            };

            var nodes = new List<AbilityOnHitNode>
            {
                new AbilityOnHitNode
                {
                    order = 1,
                    nodeType = AbilityOnHitNodeType.ApplyStatus,
                    statusId = "Freeze"
                }
            };

            projectile = CreateProjectile(owner, def, nodes, out var controller);
            float initialHealth = damageable.Health;

            InvokeOnTriggerEnter2D(controller, targetCollider);

            Assert.Less(damageable.Health, initialHealth, "hitMask 匹配时应扣血");
            Assert.IsTrue(statusController.HasStatus("Freeze"), "hitMask 匹配时应附加状态");
            Assert.Less(statLayer.MoveSpeedMultiplier, 1f, "hitMask 匹配时 Freeze modifiers 应影响 MoveSpeedMultiplier");
        }
        finally
        {
            if (projectile != null) Object.DestroyImmediate(projectile);
            if (target != null) Object.DestroyImmediate(target);
            if (owner != null) Object.DestroyImmediate(owner);
            if (statusCatalog != null) Object.DestroyImmediate(statusCatalog);
        }
    }
}
