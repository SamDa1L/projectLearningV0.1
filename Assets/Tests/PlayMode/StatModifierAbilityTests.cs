using System.Collections;
using System.Collections.Generic;
using CastleDB.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class StatModifierAbilityTests
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
        Assert.IsNotNull(_player, "PlayerController component missing");

        yield return null;

        _stats = _playerObj.GetComponent<StatModifierLayer>();
        Assert.IsNotNull(_stats, "StatModifierLayer component missing");
        _stats.ClearAll();
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
    public IEnumerator EnableDisable_RollsBackMoveSpeedMultiplier()
    {
        Assert.AreEqual(1f, _stats.MoveSpeedMultiplier, 1e-4f, "Baseline multiplier should be 1");

        _catalog = ScriptableObject.CreateInstance<AbilityCatalog>();
        var buffDefs = new List<AbilityBuffDefinition>
        {
            new AbilityBuffDefinition
            {
                id = "Passive_MoveSpeedUp_Buff",
                duration = 0f,
                stackRule = StatusStackRule.Replace,
                maxStacks = 1,
                uniqueKey = "",
                modifiersJson = "{\"moveSpeedMultiplier\":1.2}"
            }
        };

        _catalog.ApplyFromCastleDb(new List<AbilityEntry>(), null, null, buffDefs);

        var entry = new AbilityCatalogEntry
        {
            id = "Passive_MoveSpeedUp",
            hookType = AbilityHookType.Move,
            priority = 100,
            enabled = false,
            kind = AbilityKind.StatModifier,
            projectileId = "",
            buffId = "Passive_MoveSpeedUp_Buff",
            cooldown = 0f,
            onHitSequenceId = "",
            paramsJson = ""
        };

        IPlayerAbility ability = AbilityRegistry.CreateAbility(entry, _player, _catalog);
        Assert.IsNotNull(ability, "AbilityRegistry should create StatModifier ability");

        var abilitySystem = new AbilitySystem();
        abilitySystem.RegisterAbility(entry.hookType, ability);

        abilitySystem.SetAbilityEnabled(entry.id, true);
        abilitySystem.FlushPendingChanges();
        Assert.AreEqual(1.2f, _stats.MoveSpeedMultiplier, 1e-4f, "Multiplier should apply when enabled");

        abilitySystem.SetAbilityEnabled(entry.id, false);
        abilitySystem.FlushPendingChanges();
        Assert.AreEqual(1f, _stats.MoveSpeedMultiplier, 1e-4f, "Multiplier should rollback when disabled");

        yield return null;
    }
}

