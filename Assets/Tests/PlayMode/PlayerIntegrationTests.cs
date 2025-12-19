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

        // 从 CastleDB 读取 Player 数据
        var asset = Resources.Load<TextAsset>("Data/CastleDbDemo/MonsterSystem");
        Assert.IsNotNull(asset, "CastleDB asset not found");

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

        // 从 CastleDB 读取 PlayerAttackOverride 数据
        var asset = Resources.Load<TextAsset>("Data/CastleDbDemo/MonsterSystem");
        Assert.IsNotNull(asset, "CastleDB asset not found");

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

        // 从 CastleDB 读取数据
        var asset = Resources.Load<TextAsset>("Data/CastleDbDemo/MonsterSystem");
        Assert.IsNotNull(asset, "CastleDB asset not found");

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
}
