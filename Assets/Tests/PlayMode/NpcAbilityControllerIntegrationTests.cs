using System.Collections.Generic;
using System.Reflection;
using CastleDB.Runtime;
using NUnit.Framework;
using UnityEngine;

public class NpcAbilityControllerIntegrationTests
{
    private class TestEnemyAgent : EnemyAgentBase
    {
        protected override void TickState(float deltaTime) { }
        protected override void TickPhysics(float fixedDeltaTime) { }
    }

    private static FieldInfo FindField(object target, string fieldName)
    {
        var type = target?.GetType();
        while (type != null)
        {
            var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field != null)
            {
                return field;
            }

            type = type.BaseType;
        }

        return null;
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        var field = FindField(target, fieldName);
        Assert.IsNotNull(field, $"Field '{fieldName}' not found on {target?.GetType().Name}");
        field.SetValue(target, value);
    }

    private static TField GetPrivateField<TField>(object target, string fieldName)
    {
        var field = FindField(target, fieldName);
        Assert.IsNotNull(field, $"Field '{fieldName}' not found on {target?.GetType().Name}");
        return (TField)field.GetValue(target);
    }

    private static void InvokeOnTriggerEnter2D(AbilityProjectileController controller, Collider2D other)
    {
        var method = typeof(AbilityProjectileController).GetMethod(
            "OnTriggerEnter2D",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.IsNotNull(method, "OnTriggerEnter2D method not found");
        method.Invoke(controller, new object[] { other });
    }

    [Test]
    public void NpcAbility_SecondaryAttack_CastsProjectileAndDamagesPlayer()
    {
        AbilityCatalog catalog = null;
        EnemyTuningProfile profile = null;
        GameObject projectilePrefab = null;
        GameObject enemy = null;
        GameObject player = null;

        try
        {
            // ===== AbilityCatalog (in-memory override) =====
            catalog = ScriptableObject.CreateInstance<AbilityCatalog>();
            catalog.ApplyFromCastleDb(
                abilityEntries: new List<AbilityEntry>
                {
                    new AbilityEntry
                    {
                        id = "Fireball_Enemy",
                        hookType = (int)AbilityHookType.RangedAttack,
                        priority = 1,
                        enabled = false,
                        paramsJson = "",
                        kind = (int)AbilityKind.Projectile,
                        projectileId = "FireBall_enemy",
                        buffId = "",
                        cooldown = 0f,
                        onHitSequenceId = ""
                    }
                },
                projectileDefinitions: new List<AbilityProjectileDefinition>
                {
                    new AbilityProjectileDefinition
                    {
                        id = "FireBall_enemy",
                        prefabPath = "Test/ProjectilePrefab",
                        speed = 5f,
                        lifetime = 0f,
                        baseDamage = 10,
                        hitMask = "",
                        onHitVfxPath = "",
                        onHitVfxDuration = 0f,
                        onExpireVfxPath = "",
                        tags = ""
                    }
                },
                onHitSequenceDefinitions: new List<AbilityOnHitSequenceDefinition>(),
                buffDefinitions: new List<AbilityBuffDefinition>());

            // ===== Profile (in-memory) =====
            profile = ScriptableObject.CreateInstance<EnemyTuningProfile>();
            profile.attackRange = 10f;
            profile.animationTrigger = "Attack";
            profile.npcAbilities = new List<NpcAbilityEntry>
            {
                new NpcAbilityEntry
                {
                    id = "M_Knight_Fireball",
                    npcId = "M_Knight",
                    abilityId = "Fireball_Enemy",
                    enabled = true,
                    priority = 100,
                    cooldownOverride = 0f,
                    triggerRole = 1,
                    minRange = 0f,
                    maxRange = 0f,
                    paramsJson = ""
                }
            };

            // ===== Projectile prefab (in-memory) =====
            projectilePrefab = new GameObject("ProjectilePrefab");
            projectilePrefab.AddComponent<Rigidbody2D>();
            var projCollider = projectilePrefab.AddComponent<BoxCollider2D>();
            projCollider.isTrigger = true;

            // ===== Player target =====
            player = new GameObject("Player");
            var playerCollider = player.AddComponent<BoxCollider2D>();
            playerCollider.isTrigger = false;

            var playerDamageable = player.AddComponent<Damageable>();
            playerDamageable.Configure(new DamageableStats
            {
                maxHealth = 100f,
                invincibilityTime = 0f,
                knockbackMultiplier = 1f
            });

            // ===== Enemy + controller =====
            enemy = new GameObject("Enemy");
            enemy.SetActive(false);
            enemy.AddComponent<Rigidbody2D>();
            enemy.AddComponent<Animator>();
            enemy.AddComponent<Damageable>();

            var agent = enemy.AddComponent<TestEnemyAgent>();
            var npcAbilityController = enemy.AddComponent<NpcAbilityController>();

            SetPrivateField(agent, "tuningProfile", profile);

            // DetectionZone binding (SecondaryAttack -> contains player collider)
            var dzObj = new GameObject("DZ_Ability");
            dzObj.transform.SetParent(enemy.transform);
            var dzCollider = dzObj.AddComponent<BoxCollider2D>();
            dzCollider.isTrigger = true;
            var dz = dzObj.AddComponent<DetectionZone>();
            dz.detectedColliders.Add(playerCollider);

            SetPrivateField(agent, "zoneBindings", new List<DetectionZoneBinding>
            {
                new DetectionZoneBinding
                {
                    role = DetectionZoneBinding.Role.SecondaryAttack,
                    zone = dz,
                    note = ""
                }
            });

            // Inject catalog + prefab (avoid Resources.Load in tests)
            SetPrivateField(npcAbilityController, "abilityCatalogOverride", catalog);
            var prefabCache = GetPrivateField<Dictionary<string, GameObject>>(npcAbilityController, "_prefabCache");
            prefabCache["Test/ProjectilePrefab"] = projectilePrefab;

             enemy.SetActive(true);
 
             bool handled = npcAbilityController.Tick(DetectionZoneBinding.Role.SecondaryAttack, 0f);
             Assert.IsTrue(handled, "NpcAbilityController should take over SecondaryAttack when bindings exist");

             // 模拟施法动画的 AnimationEvent：OnAbilityRelease
             npcAbilityController.OnAbilityRelease();
 
             var controllers = Object.FindObjectsOfType<AbilityProjectileController>(true);
             Assert.AreEqual(1, controllers.Length, "Should spawn exactly one AbilityProjectileController");

            float initialHealth = playerDamageable.Health;
            InvokeOnTriggerEnter2D(controllers[0], playerCollider);
            Assert.Less(playerDamageable.Health, initialHealth, "Enemy projectile should damage player");
        }
        finally
        {
            foreach (var controller in Object.FindObjectsOfType<AbilityProjectileController>(true))
            {
                if (controller != null)
                {
                    Object.DestroyImmediate(controller.gameObject);
                }
            }

            if (enemy != null) Object.DestroyImmediate(enemy);
            if (player != null) Object.DestroyImmediate(player);
            if (projectilePrefab != null) Object.DestroyImmediate(projectilePrefab);
            if (catalog != null) Object.DestroyImmediate(catalog);
            if (profile != null) Object.DestroyImmediate(profile);
        }
    }
}
