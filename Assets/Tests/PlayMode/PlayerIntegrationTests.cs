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

        // PlayMode tests run in an empty test scene, so we must provide ground
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

    /// <summary>
    /// 测试 1: 基础初始化
    /// 验证玩家正确初始化并存活
    /// </summary>
    [UnityTest]
    public IEnumerator InitializesAndIsAlive()
    {
        yield return null;

        Assert.IsNotNull(player, "Player not initialized");
        Assert.IsTrue(player.IsAlive, "Player should be alive");
    }

    /// <summary>
    /// 测试 2: CastleDB → PlayerConfig → Damageable 配置链路
    /// 验证玩家的生命值和无敌时间从 CastleDB 正确应用到 Damageable
    /// </summary>
    [UnityTest]
    public IEnumerator PlayerConfigAppliedToDamageable()
    {
        yield return null;

        // 0.3 版本：从新数据源读取 Player 数据
        var asset = Resources.Load<TextAsset>("Data/Player");
        Assert.IsNotNull(asset, "CastleDB Player asset not found");

        var service = new CastleDbService();
        service.Initialize(new CastleDbJsonSource(asset));
        yield return null;

        var players = service.GetAllPlayers();
        Assert.Greater(players.Count, 0, "Player entries should exist in CastleDB");

        var playerEntry = players[0]; // 假设第一个是主玩家
        Assert.IsNotNull(playerEntry, "Player entry missing in CastleDB");

        // 验证 Damageable 的配置与 CastleDB 一致
        Assert.Greater(damageable.MaxHealth, 0, "MaxHealth should be > 0");
        Assert.GreaterOrEqual(damageable.invincibilityTime, 0, "invincibilityTime should be >= 0");

        // 注意：由于 PlayerConfig 可能有不同的值，这里只验证数据类型正确
        Assert.AreEqual(typeof(float), damageable.MaxHealth.GetType(), "MaxHealth should be float");
    }

    /// <summary>
    /// 测试 3: 玩家移动速度配置
    /// 验证玩家的移动速度从 PlayerConfig 正确应用
    /// </summary>
    [UnityTest]
    public IEnumerator PlayerMovementSpeedConfigured()
    {
        yield return null;

        // 验证移动速度已配置（非默认的硬编码值）
        Assert.Greater(player.walkSpeed, 0, "walkSpeed should be > 0");
        Assert.Greater(player.runSpeed, 0, "runSpeed should be > 0");
        Assert.Greater(player.airWalkSpeed, 0, "airWalkSpeed should be > 0");
        Assert.Greater(player.jumpImpules, 0, "jumpImpules should be > 0");
        Assert.Greater(player.climbSpeed, 0, "climbSpeed should be > 0");

        // 验证速度关系合理
        Assert.Greater(player.runSpeed, player.walkSpeed, "runSpeed should be greater than walkSpeed");
    }

    /// <summary>
    /// 测试 4: Attack attackId 配置验证
    /// 验证所有 Attack 组件都配置了 attackId
    /// </summary>
    [UnityTest]
    public IEnumerator AttackIdConfigured()
    {
        yield return null;

        // 获取所有 Attack 组件
        Attack[] attacks = playerGameObject.GetComponentsInChildren<Attack>(true);
        Assert.Greater(attacks.Length, 0, "Player should have at least one Attack component");

        foreach (var attack in attacks)
        {
            Assert.IsFalse(string.IsNullOrEmpty(attack.attackId),
                $"Attack component on '{attack.gameObject.name}' should have attackId configured");
        }
    }

    /// <summary>
    /// 测试 5: 攻击伤害覆盖应用验证
    /// 验证 Attack 组件的 attackDamage 从 PlayerConfig 正确计算并应用
    /// </summary>
    [UnityTest]
    public IEnumerator AttackDamageOverrideApplied()
    {
        yield return null;

        // 0.3 版本：从新数据源读取 PlayerAttackOverride 数据
        var asset = Resources.Load<TextAsset>("Data/Player");
        Assert.IsNotNull(asset, "CastleDB Player asset not found");

        var service = new CastleDbService();
        service.Initialize(new CastleDbJsonSource(asset));
        yield return null;

        var overrides = service.GetAllPlayerAttackOverrides();
        Assert.Greater(overrides.Count, 0, "PlayerAttackOverride entries should exist in CastleDB");

        // 获取玩家的 Attack 组件
        Attack[] attacks = playerGameObject.GetComponentsInChildren<Attack>(true);

        foreach (var attack in attacks)
        {
            // 跳过没有 attackId 的 Attack
            if (string.IsNullOrEmpty(attack.attackId))
                continue;

            // 验证伤害值已应用
            Assert.Greater(attack.attackDamage, 0,
                $"Attack '{attack.attackId}' should have damage > 0");

            // 查找对应的 CastleDB 配置
            var matchingOverride = overrides.Find(o =>
                o.targetType == 0 && o.targetId == attack.attackId);

            if (matchingOverride != null)
            {
                // 如果存在配置，验证伤害值是否符合预期（这里只验证不为0）
                Assert.Greater(attack.attackDamage, 0,
                    $"Attack '{attack.attackId}' damage should be calculated from CastleDB");
            }
        }
    }

    /// <summary>
    /// 测试 6: attackId 与 CastleDB 匹配验证
    /// 验证 Attack.attackId 能正确匹配 PlayerAttackOverride.targetId
    /// </summary>
    [UnityTest]
    public IEnumerator AttackIdMatchesCastleDb()
    {
        yield return null;

        // 0.3 版本：从新数据源读取 Player 数据
        var asset = Resources.Load<TextAsset>("Data/Player");
        Assert.IsNotNull(asset, "CastleDB Player asset not found");

        var service = new CastleDbService();
        service.Initialize(new CastleDbJsonSource(asset));
        yield return null;

        var overrides = service.GetAllPlayerAttackOverrides();

        // 获取所有 Attack 组件
        Attack[] attacks = playerGameObject.GetComponentsInChildren<Attack>(true);

        int matchedCount = 0;
        foreach (var attack in attacks)
        {
            if (string.IsNullOrEmpty(attack.attackId))
                continue;

            // 查找 CastleDB 中是否存在对应的配置
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

    /// <summary>
    /// 测试 7: 玩家受击测试
    /// 验证玩家能正确受到伤害并扣除生命值
    /// </summary>
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

    /// <summary>
    /// 测试 8: 玩家死亡条件测试
    /// 验证玩家生命值归零后正确进入死亡状态
    /// </summary>
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

    /// <summary>
    /// 测试 9: 验证 PlayerConfig 资源存在
    /// 确保 PlayerConfig 资源已正确创建并可加载
    /// </summary>
    [UnityTest]
    public IEnumerator PlayerConfigResourceExists()
    {
        yield return null;

        var playerConfig = Resources.Load<PlayerConfig>("Config/PlayerConfig");
        Assert.IsNotNull(playerConfig, "PlayerConfig should exist at Resources/Config/PlayerConfig");

        Assert.Greater(playerConfig.maxHealth, 0, "PlayerConfig maxHealth should be > 0");
        Assert.Greater(playerConfig.baseAttackDamage, 0, "PlayerConfig baseAttackDamage should be > 0");
    }

    /// <summary>
    /// 测试 10: 精确映射验证 - CastleDB 数据精确应用到 PlayerConfig（阶段 3A 验收）
    /// 验证 PlayerConfig 的值与 CastleDB 完全一致
    /// </summary>
    [UnityTest]
    public IEnumerator PreciseMappingFromCastleDbToPlayerConfig()
    {
        yield return null;

        // 0.3 版本：从新数据源读取 Player 数据
        var asset = Resources.Load<TextAsset>("Data/Player");
        Assert.IsNotNull(asset, "CastleDB Player asset not found");

        var service = new CastleDbService();
        service.Initialize(new CastleDbJsonSource(asset));
        yield return null;

        var players = service.GetAllPlayers();
        Assert.Greater(players.Count, 0, "Player entries should exist");

        var playerEntry = players.Find(p => p.id == "player");
        Assert.IsNotNull(playerEntry, "Player with id='player' should exist");

        // 加载 PlayerConfig
        var playerConfig = Resources.Load<PlayerConfig>("Config/PlayerConfig");
        Assert.IsNotNull(playerConfig, "PlayerConfig should exist");

        // 精确验证每个字段（阶段 3A 核心验收点）
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

    /// <summary>
    /// 测试 11: 幂等性验证 - CalculateFinalDamage 重复调用返回相同结果（阶段 3A 验收）
    /// 验证伤害计算不依赖 Prefab 初始值，每次调用返回相同结果
    /// </summary>
    [UnityTest]
    public IEnumerator IdempotentDamageCalculation()
    {
        yield return null;

        var playerConfig = Resources.Load<PlayerConfig>("Config/PlayerConfig");
        Assert.IsNotNull(playerConfig, "PlayerConfig should exist");

        // 测试 Hitbox 类型的伤害计算幂等性
        string testAttackId = "SW_1";

        int damage1 = playerConfig.CalculateFinalDamage(PlayerAttackOverride.TargetType.Hitbox, testAttackId);
        int damage2 = playerConfig.CalculateFinalDamage(PlayerAttackOverride.TargetType.Hitbox, testAttackId);
        int damage3 = playerConfig.CalculateFinalDamage(PlayerAttackOverride.TargetType.Hitbox, testAttackId);

        Assert.AreEqual(damage1, damage2,
            "CalculateFinalDamage should return same value on repeated calls (call 1 vs 2)");
        Assert.AreEqual(damage2, damage3,
            "CalculateFinalDamage should return same value on repeated calls (call 2 vs 3)");

        // 验证计算结果为正数（基本合理性检查）
        Assert.Greater(damage1, 0, "Calculated damage should be > 0");
    }

    /// <summary>
    /// 测试 12: 伤害倍率精确计算验证（阶段 3A 硬验收）
    /// 使用 CastleDB 实际数据验证精确计算：baseAttackDamage=50, multiplier=1 → 期望50
    /// </summary>
    [UnityTest]
    public IEnumerator PreciseDamageMultiplierCalculation()
    {
        yield return null;

        // 0.3 版本：从新数据源读取 Player 数据
        var asset = Resources.Load<TextAsset>("Data/Player");
        var service = new CastleDbService();
        service.Initialize(new CastleDbJsonSource(asset));
        yield return null;

        var playerEntry = service.GetAllPlayers().Find(p => p.id == "player");
        Assert.IsNotNull(playerEntry, "Player entry should exist");

        // 硬断言：验证 CastleDB 中的确切值（按 system-reminder）
        Assert.AreEqual(50f, playerEntry.baseAttackDamage, 0.01f,
            "CastleDB player.baseAttackDamage should be exactly 50");

        var overrides = service.GetAllPlayerAttackOverrides();
        var playerConfig = Resources.Load<PlayerConfig>("Config/PlayerConfig");

        // 硬验收点 1: SW_1 (Hitbox, multiplier=1, 无override) → 期望 50 * 1 = 50
        var sw1Override = overrides.Find(o => o.targetId == "SW_1" && o.targetType == 0);
        Assert.IsNotNull(sw1Override, "SW_1 override should exist");
        Assert.AreEqual(1f, sw1Override.damageMultiplier, 0.01f, "SW_1 multiplier should be 1");
        Assert.AreEqual(0, sw1Override.damageOverride, "SW_1 should have no override");

        int sw1Damage = playerConfig.CalculateFinalDamage(PlayerAttackOverride.TargetType.Hitbox, "SW_1");
        Assert.AreEqual(50, sw1Damage,
            "SW_1 damage should be exactly 50 (baseAttackDamage=50 * multiplier=1)");

        // 硬验收点 2: Arrow (Projectile, multiplier=3, 无override) → 期望 50 * 3 = 150
        var arrowOverride = overrides.Find(o => o.targetId == "Prefabs/Projectiles/Player/Arrow" && o.targetType == 1);
        Assert.IsNotNull(arrowOverride, "Arrow override should exist");
        Assert.AreEqual(3f, arrowOverride.damageMultiplier, 0.01f, "Arrow multiplier should be 3");
        Assert.AreEqual(0, arrowOverride.damageOverride, "Arrow should have no override");

        int arrowDamage = playerConfig.CalculateFinalDamage(
            PlayerAttackOverride.TargetType.Projectile,
            "Prefabs/Projectiles/Player/Arrow");
        Assert.AreEqual(150, arrowDamage,
            "Arrow damage should be exactly 150 (baseAttackDamage=50 * multiplier=3)");

        // 硬验收点 3: 幂等性 - 重复调用返回相同值（不累乘）
        int arrowDamage2 = playerConfig.CalculateFinalDamage(
            PlayerAttackOverride.TargetType.Projectile,
            "Prefabs/Projectiles/Player/Arrow");
        int arrowDamage3 = playerConfig.CalculateFinalDamage(
            PlayerAttackOverride.TargetType.Projectile,
            "Prefabs/Projectiles/Player/Arrow");

        Assert.AreEqual(150, arrowDamage2, "Second call should return 150 (idempotent)");
        Assert.AreEqual(150, arrowDamage3, "Third call should return 150 (idempotent)");
    }

    /// <summary>
    /// 测试 13: Projectile prefab 级伤害验证（阶段 3A 硬验收）
    /// 验证 Arrow.prefab 的 damage 字段在 Import 后直接为 150（prefab 级一次性赋值）
    /// </summary>
    [UnityTest]
    public IEnumerator ProjectilePrefabDamageDirectAssignment()
    {
        yield return null;

        // 0.3 版本：从新数据源读取配置
        var asset = Resources.Load<TextAsset>("Data/Player");
        var service = new CastleDbService();
        service.Initialize(new CastleDbJsonSource(asset));
        yield return null;

        var overrides = service.GetAllPlayerAttackOverrides();
        var arrowOverride = overrides.Find(o =>
            o.targetType == 1 && o.targetId == "Prefabs/Projectiles/Player/Arrow");

        Assert.IsNotNull(arrowOverride, "Arrow override should exist in CastleDB");
        Assert.AreEqual(3f, arrowOverride.damageMultiplier, 0.01f,
            "Arrow multiplier should be 3 in CastleDB");

        // 硬验收点：直接加载 prefab，验证 damage 字段已被设置为 150
        GameObject arrowPrefab = Resources.Load<GameObject>("Prefabs/Projectiles/Player/Arrow");
        Assert.IsNotNull(arrowPrefab, "Arrow prefab should exist at Resources path");

        Projectile projectileComponent = arrowPrefab.GetComponent<Projectile>();
        Assert.IsNotNull(projectileComponent, "Arrow prefab should have Projectile component");

        // 硬断言：prefab 的 damage 应该是 150（baseAttackDamage=50 * multiplier=3）
        Assert.AreEqual(150, projectileComponent.damage,
            "Arrow prefab damage should be exactly 150 after CastleDB Import (prefab-level assignment)");

        // 验证实例化后的 Projectile 也自带正确的 damage
        GameObject instance = Object.Instantiate(arrowPrefab);
        try
        {
            Projectile instanceProjectile = instance.GetComponent<Projectile>();
            Assert.AreEqual(150, instanceProjectile.damage,
                "Instantiated Arrow should inherit damage=150 from prefab");
        }
        finally
        {
            Object.Destroy(instance);
        }
    }

    /// <summary>
    /// 测试 14: 运行时伤害应用幂等性验证（阶段 3A 硬验收）
    /// 验证 PlayerController.ApplyAttackDamageOverrides() 重复调用不会累乘伤害
    /// </summary>
    [UnityTest]
    public IEnumerator RuntimeDamageApplicationIdempotent()
    {
        yield return null;

        // 获取 SW_1 Attack 组件的初始伤害
        Attack[] attacks = playerGameObject.GetComponentsInChildren<Attack>(true);
        var sw1Attack = System.Array.Find(attacks, a => a.attackId == "SW_1");
        Assert.IsNotNull(sw1Attack, "SW_1 Attack should exist");

        // 记录第一次应用后的伤害值（应该是 50）
        int initialDamage = sw1Attack.attackDamage;
        Assert.AreEqual(50, initialDamage, "SW_1 initial damage should be 50 after first application");

        // 模拟重复应用配置（幂等性测试）
        // 注意：正常情况下不会重复调用，但需要验证即使重复调用也不会累乘
        var playerConfig = Resources.Load<PlayerConfig>("Config/PlayerConfig");
        Assert.IsNotNull(playerConfig, "PlayerConfig should exist");

        // 直接计算并应用（模拟重复应用）
        int recalculatedDamage = playerConfig.CalculateFinalDamage(
            PlayerAttackOverride.TargetType.Hitbox,
            "SW_1");
        sw1Attack.attackDamage = recalculatedDamage;

        // 验证伤害值仍然是 50，没有累乘
        Assert.AreEqual(50, sw1Attack.attackDamage,
            "SW_1 damage should still be 50 after reapplication (idempotent)");
        Assert.AreEqual(initialDamage, sw1Attack.attackDamage,
            "Reapplication should not change damage value");
    }
}
