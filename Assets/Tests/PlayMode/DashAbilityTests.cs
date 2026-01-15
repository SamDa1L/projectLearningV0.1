using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class DashAbilityTests
{
    private GameObject _playerObj;
    private PlayerController _player;
    private Damageable _damageable;
    private Rigidbody2D _rb;

    [UnitySetUp]
    public IEnumerator Setup()
    {
        var playerPrefab = Resources.Load<GameObject>("Prefabs/Player/Player");
        Assert.IsNotNull(playerPrefab, "Player Prefab not found at Resources/Prefabs/Player/Player");

        _playerObj = Object.Instantiate(playerPrefab);
        _player = _playerObj.GetComponent<PlayerController>();
        _damageable = _playerObj.GetComponent<Damageable>();
        _rb = _playerObj.GetComponent<Rigidbody2D>();

        Assert.IsNotNull(_player, "PlayerController component missing");
        Assert.IsNotNull(_damageable, "Damageable component missing");
        Assert.IsNotNull(_rb, "Rigidbody2D component missing");

        _damageable.isInvincible = false;
        _rb.velocity = Vector2.zero;
        _rb.gravityScale = 0f; // keep test stable (avoid falling affecting x assertions)

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

        yield return null;
    }

    [UnityTest]
    public IEnumerator Dash_MovesAndGrantsInvulnerabilityWindow()
    {
        Vector3 start = _playerObj.transform.position;

        var entry = new AbilityCatalogEntry
        {
            id = "Dash_Test",
            hookType = AbilityHookType.Run,
            priority = 100,
            enabled = true,
            kind = AbilityKind.Dash,
            projectileId = "",
            buffId = "",
            cooldown = 0f,
            onHitSequenceId = "",
            paramsJson = "{\"distance\":1,\"speed\":10,\"invincibleWindow\":0.2}"
        };

        IPlayerAbility dash = AbilityRegistry.CreateAbility(entry, _player, catalog: null);
        Assert.IsNotNull(dash, "AbilityRegistry should create Dash ability");

        var system = new AbilitySystem();
        system.RegisterAbility(entry.hookType, dash);

        bool handled = system.Dispatch(AbilityHookType.Run, AbilityInput.Started(isPressed: true));
        Assert.IsTrue(handled, "Run input should be handled");
        Assert.IsTrue(_damageable.IsInvulnerable, "Dash should grant invulnerability window");

        for (int i = 0; i < 6; i++)
        {
            yield return new WaitForFixedUpdate();
        }

        Assert.Greater(_playerObj.transform.position.x, start.x + 0.05f, "Dash should move player on X");

        yield return new WaitForSeconds(0.25f);
        Assert.IsFalse(_damageable.IsInvulnerable, "Invulnerability window should end");
    }
}

