using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Damageable 组件的单元测试
/// 测试生命值管理、无敌帧、击退等核心功能
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

    }

    [TearDown]
    public void Teardown()
    {
        Object.DestroyImmediate(testGameObject);
    }

    /// <summary>
    /// 测试Configure方法正确设置所有参数
    /// </summary>
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

    /// <summary>
    /// 测试Configure方法触发事件
    /// </summary>
    [Test]
    public void TestConfigureTriggersEvent()
    {
        bool eventCalled = false;
        DamageableStats? receivedStats = null;

        damageable.DamageableStateChanged += (stats) =>
        {
            eventCalled = true;
            receivedStats = stats;
        };

        var stats = new DamageableStats
        {
            maxHealth = 50,
            invincibilityTime = 1f,
            knockbackMultiplier = 2f
        };

        damageable.Configure(stats);

        Assert.IsTrue(eventCalled);
        Assert.IsTrue(receivedStats.HasValue);
        Assert.AreEqual(50, receivedStats!.Value.maxHealth);
        Assert.AreEqual(1f, receivedStats.Value.invincibilityTime);
        Assert.AreEqual(2f, receivedStats.Value.knockbackMultiplier);
    }

    /// <summary>
    /// 测试Hit方法减少生命值
    /// </summary>
    [Test]
    public void TestHitReducesHealth()
    {
        damageable.Configure(new DamageableStats { maxHealth = 100, invincibilityTime = 0.1f, knockbackMultiplier = 1f });

        bool hitResult = damageable.Hit(30, Vector2.zero);

        Assert.IsTrue(hitResult);
        Assert.AreEqual(70, damageable.Health);
    }

    /// <summary>
    /// 测试Hit方法触发事件
    /// </summary>
    [Test]
    public void TestHitTriggersEvent()
    {
        damageable.Configure(new DamageableStats { maxHealth = 100, invincibilityTime = 0.1f, knockbackMultiplier = 1f });

        bool eventCalled = false;
        int receivedDamage = 0;
        Vector2 receivedKnockback = Vector2.zero;

        damageable.damageableHit.AddListener((damage, knockback) =>
        {
            eventCalled = true;
            receivedDamage = damage;
            receivedKnockback = knockback;
        });

        damageable.Hit(25, new Vector2(5f, 3f));

        Assert.IsTrue(eventCalled);
        Assert.AreEqual(25, receivedDamage);
        Assert.AreEqual(new Vector2(5f, 3f), receivedKnockback);
    }

    /// <summary>
    /// 测试击退倍数应用
    /// </summary>
    [Test]
    public void TestKnockbackMultiplierApplied()
    {
        damageable.Configure(new DamageableStats { maxHealth = 100, invincibilityTime = 0.1f, knockbackMultiplier = 2f });

        Vector2 receivedKnockback = Vector2.zero;
        damageable.damageableHit.AddListener((damage, knockback) =>
        {
            receivedKnockback = knockback;
        });

        Vector2 originalKnockback = new Vector2(5f, 3f);
        damageable.Hit(10, originalKnockback);

        // 应该应用2倍的击退倍数
        Assert.AreEqual(new Vector2(10f, 6f), receivedKnockback);
    }

    /// <summary>
    /// 测试无敌帧期间无法受伤
    /// </summary>
    [Test]
    public void TestCannotHitDuringInvincibility()
    {
        damageable.Configure(new DamageableStats { maxHealth = 100, invincibilityTime = 1f, knockbackMultiplier = 1f });

        // 第一次Hit
        bool firstHit = damageable.Hit(30, Vector2.zero);
        Assert.IsTrue(firstHit);
        Assert.AreEqual(70, damageable.Health);

        // 第二次Hit（应该失败，因为在无敌帧内）
        bool secondHit = damageable.Hit(20, Vector2.zero);
        Assert.IsFalse(secondHit);
        Assert.AreEqual(70, damageable.Health); // 生命值不变
    }

    /// <summary>
    /// 测试生命值为0时死亡
    /// </summary>
    [Test]
    public void TestDeathWhenHealthReachesZero()
    {
        damageable.Configure(new DamageableStats { maxHealth = 50, invincibilityTime = 0.1f, knockbackMultiplier = 1f });

        bool deathEventCalled = false;
        damageable.damageableDeath.AddListener(() =>
        {
            deathEventCalled = true;
        });

        damageable.Hit(50, Vector2.zero);

        Assert.IsFalse(damageable.IsAlive);
        Assert.IsTrue(deathEventCalled);
    }

    /// <summary>
    /// 测试生命值不能超过最大值
    /// </summary>
    [Test]
    public void TestHealthCannotExceedMax()
    {
        damageable.Configure(new DamageableStats { maxHealth = 100, invincibilityTime = 0.1f, knockbackMultiplier = 1f });

        damageable.Hit(30, Vector2.zero);
        Assert.AreEqual(70, damageable.Health);

        damageable.Heal(50); // 尝试治疗50点
        Assert.AreEqual(100, damageable.Health); // 应该只治疗到100
    }

    /// <summary>
    /// 测试无敌状态属性
    /// </summary>
    [Test]
    public void TestIsInvulnerableProperty()
    {
        damageable.Configure(new DamageableStats { maxHealth = 100, invincibilityTime = 0.1f, knockbackMultiplier = 1f });

        Assert.IsFalse(damageable.IsInvulnerable);

        damageable.Hit(10, Vector2.zero);

        Assert.IsTrue(damageable.IsInvulnerable);
    }

    /// <summary>
    /// 测试Configure为空时的处理
    /// </summary>
    [Test]
    public void TestConfigureWithNullStats()
    {
        // 应该不抛出异常
        damageable.Configure(null);

        // 参数应该保持默认值
        Assert.AreEqual(100, damageable.MaxHealth);
    }

    /// <summary>
    /// 测试已死亡的角色无法受伤
    /// </summary>
    [Test]
    public void TestDeadCharacterCannotBeDamaged()
    {
        damageable.Configure(new DamageableStats { maxHealth = 50, invincibilityTime = 0.1f, knockbackMultiplier = 1f });

        // 造成致命伤害
        damageable.Hit(50, Vector2.zero);
        Assert.IsFalse(damageable.IsAlive);

        // 尝试再次伤害
        bool secondHit = damageable.Hit(10, Vector2.zero);
        Assert.IsFalse(secondHit);
    }

    /// <summary>
    /// 测试已死亡的角色无法治疗
    /// </summary>
    [Test]
    public void TestDeadCharacterCannotBeHealed()
    {
        damageable.Configure(new DamageableStats { maxHealth = 50, invincibilityTime = 0.1f, knockbackMultiplier = 1f });

        // 造成致命伤害
        damageable.Hit(50, Vector2.zero);
        Assert.IsFalse(damageable.IsAlive);

        // 尝试治疗
        bool healResult = damageable.Heal(25);
        Assert.IsFalse(healResult);
        Assert.AreEqual(0, damageable.Health);
    }
}
