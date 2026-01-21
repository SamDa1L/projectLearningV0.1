using System.Collections;
using System.Collections.Generic;
using CastleDB.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class SummonAbilityTests
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
        foreach (var marker in Object.FindObjectsOfType<SummonedByAbility>(true))
        {
            if (marker != null)
            {
                Object.Destroy(marker.gameObject);
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

    [UnityTest]
    public IEnumerator Summon_RespectsMaxCount_AndLifetime()
    {
        _catalog = ScriptableObject.CreateInstance<AbilityCatalog>();
        _catalog.ApplyFromCastleDb(
            abilityEntries: new List<AbilityEntry>(),
            projectileDefinitions: null,
            summonDefinitions: new List<AbilitySummonDefinition>
            {
                new AbilitySummonDefinition
                {
                    id = "TestSummon",
                    prefabPath = "Prefabs/Projectiles/Player/Arrow",
                    lifetime = 0.1f,
                    maxCount = 2,
                    spawnRule = AbilitySummonSpawnRule.Reject,
                    tags = ""
                }
            },
            onHitSequenceDefinitions: null,
            buffDefinitions: new List<AbilityBuffDefinition>());

        var entry = new AbilityCatalogEntry
        {
            id = "Summon_Test",
            hookType = AbilityHookType.RangedAttack,
            priority = 10,
            enabled = true,
            kind = AbilityKind.Summon,
            projectileId = "",
            summonId = "TestSummon",
            buffId = "",
            cooldown = 0f,
            onHitSequenceId = "",
            paramsJson = ""
        };

        IPlayerAbility summon = AbilityRegistry.CreateAbility(entry, _player, _catalog);
        Assert.IsNotNull(summon, "AbilityRegistry should create Summon ability");

        var system = new AbilitySystem();
        system.RegisterAbility(entry.hookType, summon);

        Assert.IsTrue(system.Dispatch(entry.hookType, AbilityInput.Started(isPressed: true)));
        Assert.IsTrue(system.Dispatch(entry.hookType, AbilityInput.Started(isPressed: true)));
        Assert.IsTrue(system.Dispatch(entry.hookType, AbilityInput.Started(isPressed: true)));

        yield return null; // allow Instantiate to complete

        int count = 0;
        foreach (var marker in Object.FindObjectsOfType<SummonedByAbility>(true))
        {
            if (marker != null && marker.abilityId == entry.id)
            {
                count++;
            }
        }

        Assert.AreEqual(2, count, "maxCount=2 should cap active summons");

        yield return new WaitForSeconds(0.15f);
        yield return null; // allow Destroy to flush

        int remaining = 0;
        foreach (var marker in Object.FindObjectsOfType<SummonedByAbility>(true))
        {
            if (marker != null && marker.abilityId == entry.id)
            {
                remaining++;
            }
        }

        Assert.AreEqual(0, remaining, "lifetime should auto-destroy summons");
    }

    [UnityTest]
    public IEnumerator Summon_LifetimeMinusOne_DestroyOnDeath_WhenIsDeadTrue()
    {
        _catalog = ScriptableObject.CreateInstance<AbilityCatalog>();
        _catalog.ApplyFromCastleDb(
            abilityEntries: new List<AbilityEntry>(),
            projectileDefinitions: null,
            summonDefinitions: new List<AbilitySummonDefinition>
            {
                new AbilitySummonDefinition
                {
                    id = "TestSummonKnight",
                    prefabPath = "Prefabs/Enemy/KnightEnemy/KnightEnemy",
                    lifetime = -1f,
                    isDead = true,
                    factionOverride = FactionId.None,
                    maxCount = 1,
                    spawnRule = AbilitySummonSpawnRule.ReplaceOldest,
                    tags = ""
                }
            },
            onHitSequenceDefinitions: null,
            buffDefinitions: new List<AbilityBuffDefinition>());

        var entry = new AbilityCatalogEntry
        {
            id = "Summon_Knight_DeathOnly",
            hookType = AbilityHookType.RangedAttack,
            priority = 10,
            enabled = true,
            kind = AbilityKind.Summon,
            projectileId = "",
            summonId = "TestSummonKnight",
            buffId = "",
            cooldown = 0f,
            onHitSequenceId = "",
            paramsJson = ""
        };

        IPlayerAbility summon = AbilityRegistry.CreateAbility(entry, _player, _catalog);
        Assert.IsNotNull(summon, "AbilityRegistry should create Summon ability");

        var system = new AbilitySystem();
        system.RegisterAbility(entry.hookType, summon);

        Assert.IsTrue(system.Dispatch(entry.hookType, AbilityInput.Started(isPressed: true)));
        yield return null;

        SummonedByAbility marker = null;
        foreach (var m in Object.FindObjectsOfType<SummonedByAbility>(true))
        {
            if (m != null && m.abilityId == entry.id)
            {
                marker = m;
                break;
            }
        }

        Assert.IsNotNull(marker, "Summon should create an instance with SummonedByAbility marker");

        var dmg = marker.GetComponent<Damageable>();
        Assert.IsNotNull(dmg, "Summoned Knight should have Damageable");

        dmg.Hit(Mathf.RoundToInt(dmg.MaxHealth + 999f), Vector2.zero);
        yield return null;
        yield return null;

        Assert.IsTrue(marker == null, "When isDead=true and lifetime=-1, summon should be destroyed on death");
    }

    [UnityTest]
    public IEnumerator Summon_FactionOverride_AppliesFriendLayers()
    {
        _catalog = ScriptableObject.CreateInstance<AbilityCatalog>();
        _catalog.ApplyFromCastleDb(
            abilityEntries: new List<AbilityEntry>(),
            projectileDefinitions: null,
            summonDefinitions: new List<AbilitySummonDefinition>
            {
                new AbilitySummonDefinition
                {
                    id = "TestSummonKnightFriend",
                    prefabPath = "Prefabs/Enemy/KnightEnemy/KnightEnemy",
                    lifetime = 0f,
                    isDead = false,
                    factionOverride = FactionId.Friend,
                    maxCount = 1,
                    spawnRule = AbilitySummonSpawnRule.ReplaceOldest,
                    tags = ""
                }
            },
            onHitSequenceDefinitions: null,
            buffDefinitions: new List<AbilityBuffDefinition>());

        var entry = new AbilityCatalogEntry
        {
            id = "Summon_Knight_Friend",
            hookType = AbilityHookType.RangedAttack,
            priority = 10,
            enabled = true,
            kind = AbilityKind.Summon,
            projectileId = "",
            summonId = "TestSummonKnightFriend",
            buffId = "",
            cooldown = 0f,
            onHitSequenceId = "",
            paramsJson = ""
        };

        IPlayerAbility summon = AbilityRegistry.CreateAbility(entry, _player, _catalog);
        Assert.IsNotNull(summon, "AbilityRegistry should create Summon ability");

        var system = new AbilitySystem();
        system.RegisterAbility(entry.hookType, summon);

        Assert.IsTrue(system.Dispatch(entry.hookType, AbilityInput.Started(isPressed: true)));
        yield return null;

        SummonedByAbility marker = null;
        foreach (var m in Object.FindObjectsOfType<SummonedByAbility>(true))
        {
            if (m != null && m.abilityId == entry.id)
            {
                marker = m;
                break;
            }
        }

        Assert.IsNotNull(marker, "Summon should create an instance with SummonedByAbility marker");

        int playerLayer = LayerMask.NameToLayer("Player");
        int playerHitBoxLayer = LayerMask.NameToLayer("PlayerHitBox");
        Assert.GreaterOrEqual(playerLayer, 0, "Layer 'Player' missing");
        Assert.GreaterOrEqual(playerHitBoxLayer, 0, "Layer 'PlayerHitBox' missing");

        Assert.AreEqual(playerLayer, marker.gameObject.layer, "Summoned Friend unit root layer should be Player");

        foreach (var zone in marker.GetComponentsInChildren<DetectionZone>(true))
        {
            Assert.AreEqual(playerHitBoxLayer, zone.gameObject.layer, "Summoned Friend unit DetectionZone should be on PlayerHitBox layer");
        }

        foreach (var atk in marker.GetComponentsInChildren<Attack>(true))
        {
            Assert.AreEqual(playerHitBoxLayer, atk.gameObject.layer, "Summoned Friend unit Attack hitbox should be on PlayerHitBox layer");
        }
    }
}
