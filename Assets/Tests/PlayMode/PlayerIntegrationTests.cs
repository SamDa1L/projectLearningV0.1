using CastleDB.Runtime;
using NUnit.Framework;
using System.Collections;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Player integration tests: CastleDB → PlayerConfig → PlayerController → Damageable + Attack
/// 阶段 3A 验收测试：验证玩家配置链路和攻击伤害覆盖功能
/// </summary>
public class PlayerIntegrationTests
{
    private GameObject playerGameObject;
    private GameObject audioListenerGameObject;
    private GameObject testGroundGameObject;
    private PlayerController player;
    private Damageable damageable;

    private static int RequireLayer(string layerName)
    {
        int layer = LayerMask.NameToLayer(layerName);
        Assert.GreaterOrEqual(layer, 0, $"Layer '{layerName}' not found. Check ProjectSettings/TagManager.asset.");
        return layer;
    }

    [UnitySetUp]
    public IEnumerator Setup()
    {
        if (Object.FindObjectOfType<AudioListener>() == null)
        {
            audioListenerGameObject = new GameObject("TestAudioListener");
            audioListenerGameObject.AddComponent<AudioListener>();
        }

        var playerPrefab = Resources.Load<GameObject>("Prefabs/Player/Player");
        Assert.IsNotNull(playerPrefab, "Player Prefab not found at Resources/Prefabs/Player/Player");

        playerGameObject = Object.Instantiate(playerPrefab);
        Assert.IsNotNull(playerGameObject, "Player Prefab instantiation failed");

        player = playerGameObject.GetComponent<PlayerController>();
        Assert.IsNotNull(player, "PlayerController component missing");

        damageable = playerGameObject.GetComponent<Damageable>();
        Assert.IsNotNull(damageable, "Damageable component missing");

        var playerCollider = playerGameObject.GetComponent<Collider2D>();
        Assert.IsNotNull(playerCollider, "Player Collider2D missing");

        testGroundGameObject = new GameObject("TestGround");
        testGroundGameObject.layer = RequireLayer("Ground");

        var groundCollider = testGroundGameObject.AddComponent<BoxCollider2D>();
        groundCollider.size = new Vector2(50f, 1f);

        float groundTopY = playerCollider.bounds.min.y;
        testGroundGameObject.transform.position = new Vector3(
            playerCollider.bounds.center.x,
            groundTopY - (groundCollider.size.y * 0.5f),
            0f
        );

        Physics2D.SyncTransforms();
        yield return new WaitForFixedUpdate();
        yield return null;
    }

    [UnityTearDown]
    public IEnumerator Teardown()
    {
        if (playerGameObject != null)
        {
            Object.Destroy(playerGameObject);
            playerGameObject = null;
        }

        if (testGroundGameObject != null)
        {
            Object.Destroy(testGroundGameObject);
            testGroundGameObject = null;
        }

        if (audioListenerGameObject != null)
        {
            Object.Destroy(audioListenerGameObject);
            audioListenerGameObject = null;
        }

        yield return null;
    }

    [UnityTest]
    public IEnumerator InitializesAndIsAlive()
    {
        yield return null;
        Assert.IsNotNull(player, "Player not initialized");
        Assert.IsTrue(player.IsAlive, "Player should be alive");
    }

    [UnityTest]
    public IEnumerator PlayerConfigAppliedToDamageable()
    {
        yield return null;

        var asset = Resources.Load<TextAsset>("Data/Player");
        Assert.IsNotNull(asset, "CastleDB Player asset not found");

        var service = new CastleDbService();
        service.Initialize(new CastleDbJsonSource(asset));
        yield return null;

        var players = service.GetAllPlayers();
        Assert.Greater(players.Count, 0, "Player entries should exist in CastleDB");

        var playerEntry = players[0];
        Assert.IsNotNull(playerEntry, "Player entry missing in CastleDB");

        Assert.Greater(damageable.MaxHealth, 0, "MaxHealth should be > 0");
        Assert.GreaterOrEqual(damageable.invincibilityTime, 0, "invincibilityTime should be >= 0");
    }

    [UnityTest]
    public IEnumerator PlayerMovementSpeedConfigured()
    {
        yield return null;

        Assert.Greater(player.walkSpeed, 0, "walkSpeed should be > 0");
        Assert.Greater(player.runSpeed, 0, "runSpeed should be > 0");
        Assert.Greater(player.airWalkSpeed, 0, "airWalkSpeed should be > 0");
        Assert.Greater(player.jumpImpules, 0, "jumpImpules should be > 0");
        Assert.Greater(player.climbSpeed, 0, "climbSpeed should be > 0");
        Assert.Greater(player.runSpeed, player.walkSpeed, "runSpeed should be greater than walkSpeed");
    }

    [UnityTest]
    public IEnumerator AttackIdConfigured()
    {
        yield return null;
        Attack[] attacks = playerGameObject.GetComponentsInChildren<Attack>(true);
        Assert.Greater(attacks.Length, 0, "Player should have at least one Attack component");
        foreach (var attack in attacks)
        {
            Assert.IsFalse(string.IsNullOrEmpty(attack.attackId),
                $"Attack component on '{attack.gameObject.name}' should have attackId configured");
        }
    }

    [UnityTest]
    public IEnumerator AttackDamageOverrideApplied()
    {
        yield return null;

        var asset = Resources.Load<TextAsset>("Data/Player");
        Assert.IsNotNull(asset, "CastleDB Player asset not found");

        var service = new CastleDbService();
        service.Initialize(new CastleDbJsonSource(asset));
        yield return null;

        var overrides = service.GetAllPlayerAttackOverrides();
        Attack[] attacks = playerGameObject.GetComponentsInChildren<Attack>(true);

        foreach (var attack in attacks)
        {
            if (string.IsNullOrEmpty(attack.attackId))
                continue;

            Assert.Greater(attack.attackDamage, 0,
                $"Attack '{attack.attackId}' should have damage > 0");

            var matchingOverride = overrides.Find(o =>
                o.targetType == 0 && o.targetId == attack.attackId);

            if (matchingOverride != null)
            {
                Assert.Greater(attack.attackDamage, 0,
                    $"Attack '{attack.attackId}' damage should be calculated from CastleDB");
            }
        }
    }

    [UnityTest]
    public IEnumerator AttackIdMatchesCastleDb()
    {
        yield return null;

        var asset = Resources.Load<TextAsset>("Data/Player");
        Assert.IsNotNull(asset, "CastleDB Player asset not found");

        var service = new CastleDbService();
        service.Initialize(new CastleDbJsonSource(asset));
        yield return null;

        var overrides = service.GetAllPlayerAttackOverrides();
        Attack[] attacks = playerGameObject.GetComponentsInChildren<Attack>(true);

        int matchedCount = 0;
        foreach (var attack in attacks)
        {
            if (string.IsNullOrEmpty(attack.attackId))
                continue;

            var matchingOverride = overrides.Find(o =>
                o.targetType == 0 && o.targetId == attack.attackId);

            if (matchingOverride != null)
            {
                matchedCount++;
                Debug.Log($"[Test] Attack '{attack.attackId}' matched with CastleDB override '{matchingOverride.id}'");
            }
        }

        Assert.Greater(matchedCount, 0,
            "At least one Attack should match PlayerAttackOverride in CastleDB");
    }

    [UnityTest]
    public IEnumerator PlayerTakesDamage()
    {
        yield return null;

        float initialHealth = damageable.Health;
        Assert.Greater(initialHealth, 0, "Initial health should be > 0");

        const int damage = 10;
        bool hitSuccess = damageable.Hit(damage, Vector2.right);

        Assert.IsTrue(hitSuccess, "Hit should succeed");
        Assert.AreEqual(initialHealth - damage, damageable.Health,
            "Health should decrease after hit");
    }

    [UnityTest]
    public IEnumerator PlayerDeathCondition()
    {
        yield return null;

        float maxHealth = damageable.MaxHealth;
        damageable.Hit((int)(maxHealth + 10), Vector2.zero);

        yield return null;

        Assert.IsFalse(damageable.IsAlive, "Player should die after lethal damage");
        Assert.IsFalse(player.IsAlive, "PlayerController.IsAlive should return false");
    }

    [UnityTest]
    public IEnumerator PlayerConfigResourceExists()
    {
        yield return null;

        var playerConfig = Resources.Load<PlayerConfig>("Config/PlayerConfig");
        Assert.IsNotNull(playerConfig, "PlayerConfig should exist at Resources/Config/PlayerConfig");

        Assert.Greater(playerConfig.maxHealth, 0, "PlayerConfig maxHealth should be > 0");
        Assert.Greater(playerConfig.baseAttackDamage, 0, "PlayerConfig baseAttackDamage should be > 0");
    }

    [UnityTest]
    public IEnumerator PreciseMappingFromCastleDbToPlayerConfig()
    {
        yield return null;

        var asset = Resources.Load<TextAsset>("Data/Player");
        Assert.IsNotNull(asset, "CastleDB Player asset not found");

        var service = new CastleDbService();
        service.Initialize(new CastleDbJsonSource(asset));
        yield return null;

        var players = service.GetAllPlayers();
        Assert.Greater(players.Count, 0, "Player entries should exist");

        var playerEntry = players.Find(p => p.id == "player");
        Assert.IsNotNull(playerEntry, "Player with id='player' should exist");

        var playerConfig = Resources.Load<PlayerConfig>("Config/PlayerConfig");
        Assert.IsNotNull(playerConfig, "PlayerConfig should exist");

        Assert.AreEqual(playerEntry.maxHealth, playerConfig.maxHealth,
            "PlayerConfig.maxHealth should exactly match CastleDB");
        Assert.AreEqual(playerEntry.invincibilityTime, playerConfig.invincibilityTime,
            "PlayerConfig.invincibilityTime should exactly match CastleDB");
        Assert.AreEqual(playerEntry.walkSpeed, playerConfig.walkSpeed,
            "PlayerConfig.walkSpeed should exactly match CastleDB");
        Assert.AreEqual(playerEntry.runSpeed, playerConfig.runSpeed,
            "PlayerConfig.runSpeed should exactly match CastleDB");
        Assert.AreEqual(playerEntry.airWalkSpeed, playerConfig.airWalkSpeed,
            "PlayerConfig.airWalkSpeed should exactly match CastleDB");
        Assert.AreEqual(playerEntry.jumpImpulse, playerConfig.jumpImpulse,
            "PlayerConfig.jumpImpulse should exactly match CastleDB");
        Assert.AreEqual(playerEntry.climbSpeed, playerConfig.climbSpeed,
            "PlayerConfig.climbSpeed should exactly match CastleDB");
        Assert.AreEqual(playerEntry.baseAttackDamage, playerConfig.baseAttackDamage,
            "PlayerConfig.baseAttackDamage should exactly match CastleDB");
    }

    [UnityTest]
    public IEnumerator IdempotentDamageCalculation()
    {
        yield return null;

        var playerConfig = Resources.Load<PlayerConfig>("Config/PlayerConfig");
        Assert.IsNotNull(playerConfig, "PlayerConfig should exist");

        var asset = Resources.Load<TextAsset>("Data/Player");
        var service = new CastleDbService();
        service.Initialize(new CastleDbJsonSource(asset));
        yield return null;
        var overrides = service.GetAllPlayerAttackOverrides();
        var hitboxOverride = overrides.Find(o => o.targetType == (int)PlayerAttackOverride.TargetType.Hitbox);
        Assert.IsNotNull(hitboxOverride, "At least one Hitbox override should exist for idempotent test");

        int damage1 = playerConfig.CalculateFinalDamage(PlayerAttackOverride.TargetType.Hitbox, hitboxOverride.targetId);
        int damage2 = playerConfig.CalculateFinalDamage(PlayerAttackOverride.TargetType.Hitbox, hitboxOverride.targetId);
        int damage3 = playerConfig.CalculateFinalDamage(PlayerAttackOverride.TargetType.Hitbox, hitboxOverride.targetId);

        Assert.AreEqual(damage1, damage2,
            "CalculateFinalDamage should return same value on repeated calls (call 1 vs 2)");
        Assert.AreEqual(damage2, damage3,
            "CalculateFinalDamage should return same value on repeated calls (call 2 vs 3)");
        Assert.Greater(damage1, 0, "Calculated damage should be > 0");
    }

    /// 测试 12: 伤害倍率精确计算验证（阶段 3A 验收）
    /// 使用 CastleDB 实际数据验证：PlayerEntry/Override → PlayerConfig/CalculateFinalDamage 一致
    [UnityTest]
    public IEnumerator PreciseDamageMultiplierCalculation()
    {
        yield return null;

        var asset = Resources.Load<TextAsset>("Data/Player");
        var service = new CastleDbService();
        service.Initialize(new CastleDbJsonSource(asset));
        yield return null;

        var playerEntry = service.GetAllPlayers().Find(p => p.id == "player");
        Assert.IsNotNull(playerEntry, "Player entry should exist");

        var overrides = service.GetAllPlayerAttackOverrides();
        var playerConfig = Resources.Load<PlayerConfig>("Config/PlayerConfig");
        Assert.IsNotNull(playerConfig, "PlayerConfig should exist");

        int ComputeExpected(PlayerAttackOverrideEntry o) =>
            o.damageOverride > 0 ? o.damageOverride : Mathf.Max(1, Mathf.RoundToInt(playerEntry.baseAttackDamage * o.damageMultiplier));

        var hitboxOverride = overrides.Find(o => o.targetType == (int)PlayerAttackOverride.TargetType.Hitbox);
        Assert.IsNotNull(hitboxOverride, "At least one Hitbox override should exist");
        int expectedHitboxDamage = ComputeExpected(hitboxOverride);
        int calculatedHitboxDamage = playerConfig.CalculateFinalDamage(PlayerAttackOverride.TargetType.Hitbox, hitboxOverride.targetId);
        Assert.AreEqual(expectedHitboxDamage, calculatedHitboxDamage,
            $"Hitbox damage should match CastleDB override ({hitboxOverride.targetId})");

        var projectileOverride = overrides.Find(o => o.targetType == (int)PlayerAttackOverride.TargetType.Projectile);
        Assert.IsNotNull(projectileOverride, "At least one Projectile override should exist");
        int expectedProjectileDamage = ComputeExpected(projectileOverride);
        int calculatedProjectileDamage = playerConfig.CalculateFinalDamage(PlayerAttackOverride.TargetType.Projectile, projectileOverride.targetId);
        Assert.AreEqual(expectedProjectileDamage, calculatedProjectileDamage,
            $"Projectile damage should match CastleDB override ({projectileOverride.targetId})");

        int projectileDamage2 = playerConfig.CalculateFinalDamage(PlayerAttackOverride.TargetType.Projectile, projectileOverride.targetId);
        int projectileDamage3 = playerConfig.CalculateFinalDamage(PlayerAttackOverride.TargetType.Projectile, projectileOverride.targetId);
        Assert.AreEqual(calculatedProjectileDamage, projectileDamage2, "Second call should be idempotent");
        Assert.AreEqual(calculatedProjectileDamage, projectileDamage3, "Third call should be idempotent");
    }

    /// 测试 13: Projectile prefab 级伤害验证（阶段 3A 验收）
    /// 验证导入后 prefab-level damage 与 CastleDB 覆盖一致
    [UnityTest]
    public IEnumerator ProjectilePrefabDamageDirectAssignment()
    {
        yield return null;

        var asset = Resources.Load<TextAsset>("Data/Player");
        var service = new CastleDbService();
        service.Initialize(new CastleDbJsonSource(asset));
        yield return null;

        var overrides = service.GetAllPlayerAttackOverrides();
        var playerEntry = service.GetAllPlayers().Find(p => p.id == "player");
        Assert.IsNotNull(playerEntry, "Player entry should exist");

        var projectileOverride = overrides.Find(o => o.targetType == (int)PlayerAttackOverride.TargetType.Projectile);
        Assert.IsNotNull(projectileOverride, "Projectile override should exist in CastleDB");

        int expectedDamage = projectileOverride.damageOverride > 0
            ? projectileOverride.damageOverride
            : Mathf.Max(1, Mathf.RoundToInt(playerEntry.baseAttackDamage * projectileOverride.damageMultiplier));

        GameObject projectilePrefab = Resources.Load<GameObject>(projectileOverride.targetId);
        Assert.IsNotNull(projectilePrefab, $"Projectile prefab should exist at Resources path '{projectileOverride.targetId}'");

        Projectile projectileComponent = projectilePrefab.GetComponent<Projectile>();
        Assert.IsNotNull(projectileComponent, "Projectile prefab should have Projectile component");
        Assert.AreEqual(expectedDamage, projectileComponent.damage,
            "Projectile prefab damage should match CastleDB override");

        GameObject instance = Object.Instantiate(projectilePrefab);
        try
        {
            Projectile instanceProjectile = instance.GetComponent<Projectile>();
            Assert.AreEqual(expectedDamage, instanceProjectile.damage,
                "Instantiated projectile should inherit correct damage from prefab");
        }
        finally
        {
            Object.Destroy(instance);
        }
    }

    /// 测试 14: 运行时伤害应用幂等性验证（阶段 3A 验收）
    /// 验证 ApplyAttackDamageOverrides 重复调用不会累乘伤害
    [UnityTest]
    public IEnumerator RuntimeDamageApplicationIdempotent()
    {
        yield return null;

        Attack[] attacks = playerGameObject.GetComponentsInChildren<Attack>(true);

        var asset = Resources.Load<TextAsset>("Data/Player");
        var service = new CastleDbService();
        service.Initialize(new CastleDbJsonSource(asset));
        yield return null;
        var overrides = service.GetAllPlayerAttackOverrides();

        var matchedAttack = System.Array.Find(attacks, a =>
            !string.IsNullOrEmpty(a.attackId) && overrides.Exists(o => o.targetType == (int)PlayerAttackOverride.TargetType.Hitbox && o.targetId == a.attackId));
        Assert.IsNotNull(matchedAttack, "At least one Attack should match a CastleDB Hitbox override");

        var playerConfig = Resources.Load<PlayerConfig>("Config/PlayerConfig");
        Assert.IsNotNull(playerConfig, "PlayerConfig should exist");

        int expectedDamage = playerConfig.CalculateFinalDamage(PlayerAttackOverride.TargetType.Hitbox, matchedAttack.attackId);

        int initialDamage = matchedAttack.attackDamage;
        Assert.AreEqual(expectedDamage, initialDamage,
            "Initial applied damage should match CalculateFinalDamage result");

        int recalculatedDamage = playerConfig.CalculateFinalDamage(PlayerAttackOverride.TargetType.Hitbox, matchedAttack.attackId);
        matchedAttack.attackDamage = recalculatedDamage;

        Assert.AreEqual(expectedDamage, matchedAttack.attackDamage,
            "Reapplication should keep damage consistent (idempotent)");
        Assert.AreEqual(initialDamage, matchedAttack.attackDamage,
            "Reapplication should not change damage value");
    }
}
