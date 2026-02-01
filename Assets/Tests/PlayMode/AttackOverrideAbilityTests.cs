using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CastleDB.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class AttackOverrideAbilityTests
{
    private GameObject _playerObj;
    private PlayerController _player;
    private AbilityCatalog _catalog;

    [UnitySetUp]
    public IEnumerator Setup()
    {
        var playerPrefab = Resources.Load<GameObject>("Prefabs/Player/Player");
        Assert.IsNotNull(playerPrefab, "Player Prefab not found at Resources/Prefabs/Player/Player");

        _playerObj = Object.Instantiate(playerPrefab);
        _player = _playerObj.GetComponent<PlayerController>();
        Assert.IsNotNull(_player, "PlayerController component missing");

        yield return null;
    }

    [UnityTearDown]
    public IEnumerator Teardown()
    {
        foreach (var controller in Object.FindObjectsOfType<AbilityProjectileController>(true))
        {
            if (controller != null)
            {
                Object.Destroy(controller.gameObject);
            }
        }

        if (_playerObj != null)
        {
            Object.Destroy(_playerObj);
            _playerObj = null;
        }

        if (_catalog != null)
        {
            Object.Destroy(_catalog);
            _catalog = null;
        }

        yield return null;
    }

    private static AbilityProjectileDefinition GetDef(AbilityProjectileController controller)
    {
        var field = typeof(AbilityProjectileController).GetField("_def", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, "AbilityProjectileController._def field not found");
        return field.GetValue(controller) as AbilityProjectileDefinition;
    }

    [UnityTest]
    public IEnumerator EnableDisable_OverridesAttackToProjectile_AndScalesDamage()
    {
        _catalog = ScriptableObject.CreateInstance<AbilityCatalog>();
        _catalog.ApplyFromCastleDb(
            abilityEntries: new List<AbilityEntry>(),
            projectileDefinitions: new List<AbilityProjectileDefinition>
            {
                new AbilityProjectileDefinition
                {
                    id = "TestArrow",
                    prefabPath = "Prefabs/Projectiles/Player/Arrow",
                    speed = 5f,
                    lifetime = 0.05f,
                    baseDamage = 10,
                    hitMask = "",
                    onHitVfxPath = "",
                    onHitVfxDuration = 0f,
                    onExpireVfxPath = "",
                    onExpireVfxDuration = 0f,
                    tags = ""
                }
            },
            summonDefinitions: null,
            onHitSequenceDefinitions: null,
            buffDefinitions: new List<AbilityBuffDefinition>());

        var defaultAttackEntry = new AbilityCatalogEntry
        {
            id = "DefaultAttack_Test",
            hookType = AbilityHookType.Attack,
            priority = 0,
            enabled = true,
            kind = AbilityKind.BuiltinDefault,
            projectileId = "",
            buffId = "",
            cooldown = 0f,
            onHitSequenceId = "",
            paramsJson = ""
        };

        var overrideEntry = new AbilityCatalogEntry
        {
            id = "AttackOverride_Test",
            hookType = AbilityHookType.Attack,
            priority = 10,
            enabled = false,
            kind = AbilityKind.AttackOverride,
            projectileId = "TestArrow",
            buffId = "",
            cooldown = 0f,
            onHitSequenceId = "",
            paramsJson = "{\"damageMultiplier\":2}"
        };

        var system = new AbilitySystem();
        var defaultAttack = AbilityRegistry.CreateAbility(defaultAttackEntry, _player, _catalog);
        var attackOverride = AbilityRegistry.CreateAbility(overrideEntry, _player, _catalog);
        Assert.IsNotNull(defaultAttack);
        Assert.IsNotNull(attackOverride);

        system.RegisterAbility(defaultAttackEntry.hookType, defaultAttack);
        system.RegisterAbility(overrideEntry.hookType, attackOverride);

        // Before enabling: should not spawn projectile.
        Assert.IsTrue(system.Dispatch(AbilityHookType.Attack, AbilityInput.Started(isPressed: true)));
        Assert.AreEqual(0, Object.FindObjectsOfType<AbilityProjectileController>(true).Length);

        // Enable override: should spawn projectile on ability release.
        system.SetAbilityEnabled(overrideEntry.id, true);
        system.FlushPendingChanges();
        Assert.IsTrue(system.IsAbilityEnabled(overrideEntry.id));

        Assert.IsTrue(system.Dispatch(AbilityHookType.Attack, AbilityInput.Started(isPressed: true)));
        _player.OnAbilityRelease();

        yield return null;

        var controllers = Object.FindObjectsOfType<AbilityProjectileController>(true);
        Assert.AreEqual(1, controllers.Length, "AttackOverride should spawn exactly one AbilityProjectileController");

        var def = GetDef(controllers[0]);
        Assert.IsNotNull(def);
        Assert.AreEqual(20, def.baseDamage, "damageMultiplier=2 should scale baseDamage");

        // Disable override: should fall back to default attack (no new projectile spawn).
        Object.Destroy(controllers[0].gameObject);
        yield return null;

        system.SetAbilityEnabled(overrideEntry.id, false);
        system.FlushPendingChanges();
        Assert.IsFalse(system.IsAbilityEnabled(overrideEntry.id));

        Assert.IsTrue(system.Dispatch(AbilityHookType.Attack, AbilityInput.Started(isPressed: true)));
        Assert.AreEqual(0, Object.FindObjectsOfType<AbilityProjectileController>(true).Length);
    }
}
