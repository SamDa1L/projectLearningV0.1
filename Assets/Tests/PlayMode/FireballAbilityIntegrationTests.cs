using System.Collections.Generic;
using System.Reflection;
using CastleDB.Runtime;
using NUnit.Framework;
using UnityEngine;

public class FireballAbilityIntegrationTests
{
    private static void InvokeOnTriggerEnter2D(AbilityProjectileController controller, Collider2D other)
    {
        var method = typeof(AbilityProjectileController).GetMethod(
            "OnTriggerEnter2D",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.IsNotNull(method, "OnTriggerEnter2D method not found");
        method.Invoke(controller, new object[] { other });
    }

    private static void SetPrivateField<TTarget>(TTarget target, string fieldName, object value)
    {
        var field = typeof(TTarget).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, $"Field '{fieldName}' not found on {typeof(TTarget).Name}");
        field.SetValue(target, value);
    }

    private static StatusCatalog CreateTestStatusCatalog()
    {
        var catalog = ScriptableObject.CreateInstance<StatusCatalog>();
        catalog.ApplyFromCastleDb(new List<StatusDefinition>
        {
            new StatusDefinition
            {
                id = "Freeze",
                displayName = "Freeze",
                defaultDuration = 5f,
                stackRule = StatusStackRule.Replace,
                maxStacks = 1,
                modifiers = new StatusModifiers(0.1f)
            }
        });
        return catalog;
    }

    [Test]
    public void FireballPickup_Cast_Hit_DealsDamageAndAppliesFreeze()
    {
        GameObject player = null;
        GameObject target = null;
        AbilityCatalog abilityCatalog = null;
        ItemCatalog itemCatalog = null;
        StatusCatalog statusCatalog = null;

        try
        {
            // ===== Player setup =====
            player = new GameObject("TestPlayer");
            player.SetActive(false);
            // AbilityProjectileController only deals damage to hostile factions (Enemy <-> Friend).
            // Tests must set factions explicitly; otherwise new GameObjects default to Neutral.
            player.AddComponent<FactionMember>().Faction = FactionId.Friend;
            player.AddComponent<Rigidbody2D>();
            player.AddComponent<TouchingDirections>();
            player.AddComponent<Damageable>();
            player.AddComponent<Animator>();

            var inventory = player.AddComponent<PlayerInventory>();
            var equipment = player.AddComponent<PlayerEquipmentController>();
            var playerController = player.AddComponent<PlayerController>();

            // 避免 PlayerController.Awake 自动构建 AbilityCatalog（本测试使用自建 AbilitySystem）
            SetPrivateField(playerController, "usePlayerConfigFromCastleDb", false);
            player.SetActive(true);

            // ===== Ability setup (Catalog -> Registry -> AbilitySystem) =====
            abilityCatalog = ScriptableObject.CreateInstance<AbilityCatalog>();
            var abilityEntries = new List<AbilityEntry>
            {
                new AbilityEntry
                {
                    id = "Fireball_Player",
                    hookType = (int)AbilityHookType.RangedAttack,
                    priority = 1,
                    enabled = false,
                    paramsJson = "",
                    kind = (int)AbilityKind.Projectile,
                    projectileId = "FireBall_player",
                    buffId = "",
                    cooldown = 0f,
                    onHitSequenceId = "Fireball_Player_OnHit"
                }
            };

            var projectileDefs = new List<AbilityProjectileDefinition>
            {
                new AbilityProjectileDefinition
                {
                    id = "FireBall_player",
                    prefabPath = "Prefabs/Projectiles/Abilitys/FireBall/FireBallProjectile",
                    speed = 5f,
                    lifetime = 0f,
                    baseDamage = 10,
                    hitMask = "",
                    onHitVfxPath = "",
                    onExpireVfxPath = "",
                    tags = ""
                }
            };

            var onHitSequences = new List<AbilityOnHitSequenceDefinition>
            {
                new AbilityOnHitSequenceDefinition
                {
                    sequenceId = "Fireball_Player_OnHit",
                    nodes = new List<AbilityOnHitNode>
                    {
                        new AbilityOnHitNode
                        {
                            order = 1,
                            nodeType = AbilityOnHitNodeType.ApplyStatus,
                            statusId = "Freeze"
                        }
                    }
                }
            };

            abilityCatalog.ApplyFromCastleDb(
                abilityEntries,
                projectileDefs,
                summonDefinitions: null,
                onHitSequenceDefinitions: onHitSequences,
                buffDefinitions: new List<AbilityBuffDefinition>());

            var abilitySystem = new AbilitySystem();
            var fireballEntry = abilityCatalog.entries[0];
            var fireballAbility = AbilityRegistry.CreateAbility(fireballEntry, playerController, abilityCatalog);
            Assert.IsNotNull(fireballAbility, "Fireball ability should be created");
            abilitySystem.RegisterAbility(fireballEntry.hookType, fireballAbility);

            // ===== Item + Inventory + Equipment setup =====
            itemCatalog = ScriptableObject.CreateInstance<ItemCatalog>();
            itemCatalog.ApplyFromCastleDb(new List<ItemDefinition>
            {
                new ItemDefinition
                {
                    id = "spellbook_fireball",
                    displayName = "Fireball Spellbook",
                    itemType = ItemType.Ability,
                    icon = "",
                    abilityId = "Fireball_Player",
                    maxStack = 1,
                    consumeEffect = new ItemConsumeEffect(0),
                    consumeEffectRawJson = "",
                    uiTag = ""
                }
            });

            var itemsService = new CastleDbService();
            itemsService.SetItemCatalog(itemCatalog);

            inventory.Initialize(itemsService, cfg: null);
            equipment.Initialize(itemsService, abilitySystem, inventory);

            // 拾取 -> 入槽 -> EquipmentController 启用能力（通过 AbilitySystem 队列）
            var pickupResult = inventory.TryPickup(new PickupRequest("spellbook_fireball", 1, null), out _);
            Assert.AreEqual(PickupResult.Success, pickupResult, "Pickup should succeed");

            abilitySystem.FlushPendingChanges();
            Assert.IsTrue(abilitySystem.IsAbilityEnabled("Fireball_Player"), "Fireball ability should be enabled after pickup");

            // ===== Cast (input -> queue release) =====
            int beforeRelease = Object.FindObjectsOfType<AbilityProjectileController>(true).Length;
            Assert.AreEqual(0, beforeRelease, "Before release there should be no spawned AbilityProjectileController");

            bool handled = abilitySystem.Dispatch(AbilityHookType.RangedAttack, AbilityInput.Started(isPressed: true));
            Assert.IsTrue(handled, "RangedAttack input should be handled by Fireball ability");

            playerController.OnAbilityRelease(); // 模拟 AnimationEvent

            var controllers = Object.FindObjectsOfType<AbilityProjectileController>(true);
            Assert.AreEqual(1, controllers.Length, "After release there should be exactly 1 AbilityProjectileController");
            var projectileController = controllers[0];

            // ===== Target + Hit =====
            statusCatalog = CreateTestStatusCatalog();

            target = new GameObject("Target");
            target.AddComponent<FactionMember>().Faction = FactionId.Enemy;
            var targetCollider = target.AddComponent<BoxCollider2D>();
            targetCollider.isTrigger = false;

            var statLayer = target.AddComponent<StatModifierLayer>();
            var statusController = target.AddComponent<StatusEffectController>();
            statusController.Initialize(statusCatalog);

            var damageable = target.AddComponent<Damageable>();
            damageable.Configure(new DamageableStats
            {
                maxHealth = 100f,
                invincibilityTime = 0f,
                knockbackMultiplier = 1f
            });

            float initialHealth = damageable.Health;

            InvokeOnTriggerEnter2D(projectileController, targetCollider);

            Assert.Less(damageable.Health, initialHealth, "Hit should reduce target health");
            Assert.IsTrue(statusController.HasStatus("Freeze"), "Hit should apply Freeze");
            Assert.Less(statLayer.MoveSpeedMultiplier, 1f, "Freeze should reduce MoveSpeedMultiplier");
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

            if (target != null) Object.DestroyImmediate(target);
            if (player != null) Object.DestroyImmediate(player);
            if (abilityCatalog != null) Object.DestroyImmediate(abilityCatalog);
            if (itemCatalog != null) Object.DestroyImmediate(itemCatalog);
            if (statusCatalog != null) Object.DestroyImmediate(statusCatalog);
        }
    }
}
