using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class SummonAbilityTests
{
    private GameObject _playerObj;
    private PlayerController _player;

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

        yield return null;
    }

    [UnityTest]
    public IEnumerator Summon_RespectsMaxCount_AndLifetime()
    {
        var entry = new AbilityCatalogEntry
        {
            id = "Summon_Test",
            hookType = AbilityHookType.RangedAttack,
            priority = 10,
            enabled = true,
            kind = AbilityKind.Summon,
            projectileId = "",
            buffId = "",
            cooldown = 0f,
            onHitSequenceId = "",
            paramsJson = "{\"prefabPath\":\"Prefabs/Projectiles/Player/Arrow\",\"lifetime\":0.1,\"maxCount\":2,\"spawnRule\":\"Reject\"}"
        };

        IPlayerAbility summon = AbilityRegistry.CreateAbility(entry, _player, catalog: null);
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
}

