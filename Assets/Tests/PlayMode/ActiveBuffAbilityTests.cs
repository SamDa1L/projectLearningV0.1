using System.Collections;
using System.Collections.Generic;
using CastleDB.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class ActiveBuffAbilityTests
{
    private GameObject _playerObj;
    private PlayerController _player;
    private StatModifierLayer _stats;
    private AbilityCatalog _catalog;

    [UnitySetUp]
    public IEnumerator Setup()
    {
        var playerPrefab = Resources.Load<GameObject>("Prefabs/Player/Player");
        Assert.IsNotNull(playerPrefab, "Player Prefab not found at Resources/Prefabs/Player/Player");

        _playerObj = Object.Instantiate(playerPrefab);
        _player = _playerObj.GetComponent<PlayerController>();
        _stats = _playerObj.GetComponent<StatModifierLayer>();

        Assert.IsNotNull(_player, "PlayerController component missing");
        Assert.IsNotNull(_stats, "StatModifierLayer component missing");

        _stats.ClearAll();

        yield return null;
    }

    [UnityTearDown]
    public IEnumerator Teardown()
    {
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
    public IEnumerator Cast_StacksAndExpires_RollsBackStats()
    {
        _catalog = ScriptableObject.CreateInstance<AbilityCatalog>();
        _catalog.ApplyFromCastleDb(
            abilityEntries: new List<AbilityEntry>(),
            projectileDefinitions: null,
            summonDefinitions: null,
            onHitSequenceDefinitions: null,
            buffDefinitions: new List<AbilityBuffDefinition>
            {
                new AbilityBuffDefinition
                {
                    id = "TestActiveBuff",
                    duration = 0.2f,
                    stackRule = StatusStackRule.Add,
                    maxStacks = 2,
                    uniqueKey = "",
                    modifiersJson = "{\"moveSpeedMultiplier\":1.1,\"attackMultiplier\":1.2}",
                    prefabPath = "",
                    prefabDuration = 0f,
                    onExpireVfxPath = "",
                    onExpireVfxDuration = 0f,
                    attachPointPath = "",
                    followTarget = true
                }
            });

        var entry = new AbilityCatalogEntry
        {
            id = "Buff_Test",
            hookType = AbilityHookType.RangedAttack,
            priority = 10,
            enabled = true,
            kind = AbilityKind.Buff,
            projectileId = "",
            buffId = "TestActiveBuff",
            cooldown = 0f,
            onHitSequenceId = "",
            paramsJson = ""
        };

        IPlayerAbility ability = AbilityRegistry.CreateAbility(entry, _player, _catalog);
        Assert.IsNotNull(ability, "AbilityRegistry should create ActiveBuffAbility");

        var system = new AbilitySystem();
        system.RegisterAbility(entry.hookType, ability);

        Assert.AreEqual(1f, _stats.MoveSpeedMultiplier, 1e-4f);
        Assert.AreEqual(1f, _stats.AttackMultiplier, 1e-4f);

        Assert.IsTrue(system.Dispatch(entry.hookType, AbilityInput.Started(isPressed: true)));
        Assert.AreEqual(1.1f, _stats.MoveSpeedMultiplier, 1e-3f, "1st stack applied");
        Assert.AreEqual(1.2f, _stats.AttackMultiplier, 1e-3f, "1st stack applied");

        Assert.IsTrue(system.Dispatch(entry.hookType, AbilityInput.Started(isPressed: true)));
        Assert.AreEqual(1.21f, _stats.MoveSpeedMultiplier, 1e-2f, "2nd stack applied (pow)");
        Assert.AreEqual(1.44f, _stats.AttackMultiplier, 1e-2f, "2nd stack applied (pow)");

        yield return new WaitForSeconds(0.25f);
        yield return null; // allow Destroy callbacks / coroutine completion

        Assert.AreEqual(1f, _stats.MoveSpeedMultiplier, 1e-3f, "buff expired -> rollback");
        Assert.AreEqual(1f, _stats.AttackMultiplier, 1e-3f, "buff expired -> rollback");
    }
}
