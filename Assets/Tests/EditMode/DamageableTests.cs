using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Damageable 组件的 EditMode 单元测试
///
/// 阶段2A新增：
/// - 测试DamageableStats数据结构
/// - 测试Configure()方法
/// - 测试Hit()方法
/// - 测试事件触发
/// - 测试参数应用
///
/// 测试覆盖：
/// - 参数配置
/// - 生命值管理
/// - 无敌帧管理
/// - 事件系统
/// - 击退倍数应用
/// </summary>
public class DamageableTests
{
    private GameObject testGameObject;
    private Damageable damageable;
    private Animator animator;

    [SetUp]
    public void Setup()
    {
        // 创建测试用的GameObject
        testGameObject = new GameObject("TestDamageable");

        // 添加Damageable组件
        damageable = testGameObject.AddComponent<Damageable>();

        // 添加Animator组件（Damageable需要）
        animator = testGameObject.AddComponent<Animator>();

        // 初始化
        damageable.Awake();
    }

    [TearDown]
    public void Teardown()
    {
        Object.DestroyImmediate(testGameObject);
    }

    // ===== DamageableStats 测试 =====

    [Test]
    public void TestDamageableStatsCreation()
    {
        var stats = new DamageableStats
        {
            maxHealth = 100,
            invincibilityTime = 0.5f,
            knockbackMultiplier = 1.5f
        };

        Assert.AreEqual(100, stats.maxHealth);
        Assert.AreEqual(0.5f, stats.invincibilityTime);
        Assert.AreEqual(1.5f, stats.knockbackMultiplier);
    }

    [Test]
    public void TestDamageableStatsToString()
    {
        var stats = new DamageableStats
        {
            maxHealth = 100,
            invincibilityTime = 0.5f,
            knockbackMultiplier = 1.5f
        };

        string str = stats.ToString();
        Assert.IsTrue(str.Contains("100"));
        Assert.IsTrue(str.Contains("0.5"));
        Assert.IsTrue(str.Contains("1.5"));
    }

    // ===== Configure 方法测试 =====

    [Test]
    public void TestConfigureSetsStats()
    {
        var stats = new DamageableStats
        {
            maxHealth = 100,
            invincibilityTime = 2f,
            knockbackMultiplier = 1.5f
        };

        damageable.Configure(stats);

        Assert.AreEqual(100, damageable.MaxHealth);
        Assert.AreEqual(2f, damageable.invincibilityTime);
        Assert.AreEqual(1.5f, damageable.knockbackMultiplier);
    }

    [Test]
    public void TestConfigureResetsHealth()
    {
        var stats = new DamageableStats
        {
            maxHealth = 100,
            invincibilityTime = 0.5f,
            knockbackMultiplier = 1f
        };

        damageable.Configure(stats);

        // 生命值应该被重置为最大值
        Assert.AreEqual(100, damageable.Health);
    }

    [Test]
    public void TestConfigureWithNullStats()
    {
        // 应该不抛出异常
        damageable.Configure(null);

        // 参数应该保持不变
        Assert.AreEqual(100, damageable.MaxHealth);
    }

    [Test]
    public void TestConfigureTriggersEvent()
    {
        bool eventCalled = false;
        DamageableStats receivedStats = null;

        damageable.DamageableStateChanged += (stats) =>
        {
            eventCalled = true;
            receivedStats = stats;
        };

        var stats = new DamageableStats
        {
            maxHealth = 100,
            invincibilityTime = 0.5f,
            knockbackMultiplier = 1.5f
        };

        damageable.Configure(stats);

        Assert.IsTrue(eventCalled);
        Assert.IsNotNull(receivedStats);
        Assert.AreEqual(100, receivedStats.maxHealth);
    }

    // ===== Hit 方法测试 =====

    [Test]
    public void TestHitReducesHealth()
    {
        var stats = new DamageableStats { maxHealth = 100 };
        damageable.Configure(stats);

        bool result = damageable.Hit(30, Vector2.zero);

        Assert.IsTrue(result);
        Assert.AreEqual(70, damageable.Health);
    }

    [Test]
    public void TestHitTriggersEvent()
    {
        var stats = new DamageableStats { maxHealth = 100 };
        damageable.Configure(stats);

        bool eventCalled = false;
        damageable.damageableHit.AddListener((damage, knockback) =>
        {
            eventCalled = true;
        });

        damageable.Hit(10, Vector2.zero);

        Assert.IsTrue(eventCalled);
    }

    [Test]
    public void TestHitSetsInvincible()
    {
        var stats = new DamageableStats { maxHealth = 100 };
        damageable.Configure(stats);

        damageable.Hit(10, Vector2.zero);

        Assert.IsTrue(damageable.isInvincible);
    }

    [Test]
    public void TestHitWhileInvincibleFails()
    {
        var stats = new DamageableStats { maxHealth = 100 };
        damageable.Configure(stats);

        damageable.Hit(10, Vector2.zero);
        int healthAfterFirstHit = damageable.Health;

        bool result = damageable.Hit(10, Vector2.zero);

        Assert.IsFalse(result);
        Assert.AreEqual(healthAfterFirstHit, damageable.Health);
    }

    [Test]
    public void TestHitToZeroHealthTriggersDeathEvent()
    {
        var stats = new DamageableStats { maxHealth = 100 };
        damageable.Configure(stats);

        bool deathEventCalled = false;
        damageable.damageableDeath.AddListener(() =>
        {
            deathEventCalled = true;
        });

        damageable.Hit(100, Vector2.zero);

        Assert.IsTrue(deathEventCalled);
        Assert.IsFalse(damageable.IsAlive);
    }

    // ===== 无敌帧测试 =====

    [Test]
    public void TestInvincibilityTimeProperty()
    {
        var stats = new DamageableStats
        {
            maxHealth = 100,
            invincibilityTime = 1.5f
        };

        damageable.Configure(stats);

        Assert.AreEqual(1.5f, damageable.invincibilityTime);
    }

    [Test]
    public void TestIsInvulnerableProperty()
    {
        Assert.IsFalse(damageable.IsInvulnerable);

        var stats = new DamageableStats { maxHealth = 100 };
        damageable.Configure(stats);
        damageable.Hit(10, Vector2.zero);

        Assert.IsTrue(damageable.IsInvulnerable);
    }

    // ===== 击退倍数测试 =====

    [Test]
    public void TestKnockbackMultiplier()
    {
        var stats = new DamageableStats
        {
            maxHealth = 100,
            knockbackMultiplier = 2f
        };

        damageable.Configure(stats);

        Assert.AreEqual(2f, damageable.knockbackMultiplier);
    }

    [Test]
    public void TestKnockbackMultiplierDefault()
    {
        var stats = new DamageableStats { maxHealth = 100 };
        damageable.Configure(stats);

        Assert.AreEqual(1f, damageable.knockbackMultiplier);
    }

    // ===== 生命值管理测试 =====

    [Test]
    public void TestMaxHealthProperty()
    {
        var stats = new DamageableStats { maxHealth = 150 };
        damageable.Configure(stats);

        Assert.AreEqual(150, damageable.MaxHealth);
    }

    [Test]
    public void TestHealthProperty()
    {
        var stats = new DamageableStats { maxHealth = 100 };
        damageable.Configure(stats);

        damageable.Health = 50;
        Assert.AreEqual(50, damageable.Health);
    }

    [Test]
    public void TestHealthZeroSetsNotAlive()
    {
        var stats = new DamageableStats { maxHealth = 100 };
        damageable.Configure(stats);

        damageable.Health = 0;

        Assert.IsFalse(damageable.IsAlive);
    }

    // ===== 集成测试 =====

    [Test]
    public void TestFullDamageFlow()
    {
        // 1. 配置
        var stats = new DamageableStats
        {
            maxHealth = 100,
            invincibilityTime = 0.5f,
            knockbackMultiplier = 1.5f
        };
        damageable.Configure(stats);

        // 2. 验证初始状态
        Assert.AreEqual(100, damageable.Health);
        Assert.IsTrue(damageable.IsAlive);
        Assert.IsFalse(damageable.isInvincible);

        // 3. 造成伤害
        bool hitResult = damageable.Hit(30, Vector2.right);

        // 4. 验证伤害结果
        Assert.IsTrue(hitResult);
        Assert.AreEqual(70, damageable.Health);
        Assert.IsTrue(damageable.isInvincible);

        // 5. 尝试再次伤害（应该失败）
        bool secondHitResult = damageable.Hit(30, Vector2.right);
        Assert.IsFalse(secondHitResult);
        Assert.AreEqual(70, damageable.Health);
    }

    [Test]
    public void TestMultipleConfigureCalls()
    {
        // 第一次配置
        var stats1 = new DamageableStats { maxHealth = 100 };
        damageable.Configure(stats1);
        Assert.AreEqual(100, damageable.MaxHealth);

        // 第二次配置
        var stats2 = new DamageableStats { maxHealth = 200 };
        damageable.Configure(stats2);
        Assert.AreEqual(200, damageable.MaxHealth);
        Assert.AreEqual(200, damageable.Health); // 生命值应该被重置
    }
}
