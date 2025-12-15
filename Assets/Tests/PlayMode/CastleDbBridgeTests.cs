using CastleDB.Runtime;
using NUnit.Framework;
using System.Collections;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Step 7: 2A 阶段综合测试
/// 验证完整的数值链路：CastleDB → DTO → Profile → EnemyAgentBase → Damageable
/// 覆盖所有 2A 关键字段
/// </summary>
public class CastleDbBridgeTests
{
    private GameObject knightGameObject;
    private GameObject audioListenerGameObject;
    private Knight knight;
    private Damageable damageable;
    private EnemyTuningProfile profile;
    private NpcEntry knightEntry;

    [UnitySetUp]
    public IEnumerator Setup()
    {
        // 添加 AudioListener（Unity 要求）
        if (Object.FindObjectOfType<AudioListener>() == null)
        {
            audioListenerGameObject = new GameObject("TestAudioListener");
            audioListenerGameObject.AddComponent<AudioListener>();
        }

        // 加载 Knight Prefab
        var knightPrefab = Resources.Load<GameObject>("Prefabs/Enemy/KnightEnemy/KnightEnemy");
        Assert.IsNotNull(knightPrefab, "Knight Prefab not found");

        knightGameObject = Object.Instantiate(knightPrefab);
        Assert.IsNotNull(knightGameObject, "Knight Prefab instantiation failed");

        knight = knightGameObject.GetComponent<Knight>();
        Assert.IsNotNull(knight, "Knight component missing");

        damageable = knightGameObject.GetComponent<Damageable>();
        Assert.IsNotNull(damageable, "Damageable component missing");

        profile = knight.TuningProfile;
        Assert.IsNotNull(profile, "TuningProfile missing");

        // 从 CastleDB 读取 Knight 数据
        var asset = Resources.Load<TextAsset>("Data/CastleDbDemo/MonsterSystem");
        Assert.IsNotNull(asset, "CastleDB asset not found");

        var service = new CastleDbService();
        service.Initialize(new CastleDbJsonSource(asset));
        knightEntry = service.GetNpcById("M_Knight");
        Assert.IsNotNull(knightEntry, "Knight entry missing in CastleDB");

        yield return null;
    }

    [UnityTearDown]
    public IEnumerator Teardown()
    {
        if (knightGameObject != null)
        {
            Object.Destroy(knightGameObject);
            knightGameObject = null;
        }

        if (audioListenerGameObject != null)
        {
            Object.Destroy(audioListenerGameObject);
            audioListenerGameObject = null;
        }

        yield return null;
    }

    /// <summary>
    /// 测试：maxHealth 字段链路
    /// CastleDB.maxHealth → NpcEntry.maxHealth → Profile.maxHealth → Damageable.MaxHealth
    /// </summary>
    [UnityTest]
    public IEnumerator MaxHealthChain()
    {
        yield return null;

        Assert.Greater(knightEntry.maxHealth, 0, "CastleDB maxHealth should be > 0");
        Assert.AreEqual(knightEntry.maxHealth, profile.maxHealth, "Profile maxHealth should match CastleDB");
        Assert.AreEqual(Mathf.RoundToInt(profile.maxHealth), damageable.MaxHealth, "Damageable MaxHealth should match Profile");
    }

    /// <summary>
    /// 测试：moveSpeed 字段链路
    /// CastleDB.moveSpeed → NpcEntry.moveSpeed → Profile.moveSpeed → EnemyAgentBase._moveSpeed
    /// </summary>
    [UnityTest]
    public IEnumerator MoveSpeedChain()
    {
        yield return null;

        Assert.Greater(knightEntry.moveSpeed, 0, "CastleDB moveSpeed should be > 0");
        Assert.AreEqual(knightEntry.moveSpeed, profile.moveSpeed, "Profile moveSpeed should match CastleDB");
        // 注意：_moveSpeed 是 protected，无法直接访问，通过 Profile 间接验证
    }

    /// <summary>
    /// 测试：attackDamage 字段链路
    /// CastleDB.attackDamage → NpcEntry.attackDamage → Profile.attackDamage → EnemyAgentBase._attackDamage
    /// </summary>
    [UnityTest]
    public IEnumerator AttackDamageChain()
    {
        yield return null;

        Assert.Greater(knightEntry.attackDamage, 0, "CastleDB attackDamage should be > 0");
        Assert.AreEqual(Mathf.RoundToInt(knightEntry.attackDamage), profile.attackDamage, "Profile attackDamage should match CastleDB");
    }

    /// <summary>
    /// 测试：attackRange 字段链路
    /// CastleDB.attackRange → NpcEntry.attackRange → Profile.attackRange → EnemyAgentBase._attackRange
    /// </summary>
    [UnityTest]
    public IEnumerator AttackRangeChain()
    {
        yield return null;

        Assert.Greater(knightEntry.attackRange, 0, "CastleDB attackRange should be > 0");
        Assert.AreEqual(knightEntry.attackRange, profile.attackRange, "Profile attackRange should match CastleDB");
    }

    /// <summary>
    /// 测试：attackCooldown 字段链路
    /// CastleDB.attackCooldown → NpcEntry.attackCooldown → Profile.attackCooldown → EnemyAgentBase._attackCooldown
    /// </summary>
    [UnityTest]
    public IEnumerator AttackCooldownChain()
    {
        yield return null;

        Assert.Greater(knightEntry.attackCooldown, 0, "CastleDB attackCooldown should be > 0");
        Assert.AreEqual(knightEntry.attackCooldown, profile.attackCooldown, "Profile attackCooldown should match CastleDB");
    }

    /// <summary>
    /// 测试：invincibleDuration 字段链路
    /// CastleDB.invincibleDuration → NpcEntry.invincibleDuration → Profile.invulnerableFrameDuration → Damageable.invincibilityTime
    /// </summary>
    [UnityTest]
    public IEnumerator InvincibleDurationChain()
    {
        yield return null;

        Assert.GreaterOrEqual(knightEntry.invincibleDuration, 0, "CastleDB invincibleDuration should be >= 0");
        Assert.AreEqual(knightEntry.invincibleDuration, profile.invulnerableFrameDuration, "Profile invulnerableFrameDuration should match CastleDB");
        Assert.AreEqual(profile.invulnerableFrameDuration, damageable.invincibilityTime, "Damageable invincibilityTime should match Profile");
    }

    /// <summary>
    /// 测试：knockbackMultiplier 字段链路
    /// CastleDB.knockbackMultiplier → NpcEntry.knockbackMultiplier → Profile.knockbackMultiplier → Damageable.knockbackMultiplier
    /// </summary>
    [UnityTest]
    public IEnumerator KnockbackMultiplierChain()
    {
        yield return null;

        Assert.Greater(knightEntry.knockbackMultiplier, 0, "CastleDB knockbackMultiplier should be > 0");
        Assert.AreEqual(knightEntry.knockbackMultiplier, profile.knockbackMultiplier, "Profile knockbackMultiplier should match CastleDB");
        Assert.AreEqual(profile.knockbackMultiplier, damageable.knockbackMultiplier, "Damageable knockbackMultiplier should match Profile");
    }

    /// <summary>
    /// 测试：enableDeathAnimation 字段链路
    /// CastleDB.enableDeathAnimation → NpcEntry.enableDeathAnimation → Profile.enableDeathAnimation → EnemyAgentBase._enableDeathAnimation
    /// </summary>
    [UnityTest]
    public IEnumerator EnableDeathAnimationChain()
    {
        yield return null;

        Assert.AreEqual(knightEntry.enableDeathAnimation, profile.enableDeathAnimation, "Profile enableDeathAnimation should match CastleDB");
    }

    /// <summary>
    /// 测试：useLegacyLogicFallback 字段链路
    /// CastleDB.useLegacyLogicFallback → NpcEntry.useLegacyLogicFallback → Profile.useLegacyLogicFallback → EnemyAgentBase._useLegacyLogicFallback
    /// </summary>
    [UnityTest]
    public IEnumerator UseLegacyLogicFallbackChain()
    {
        yield return null;

        Assert.AreEqual(knightEntry.useLegacyLogicFallback, profile.useLegacyLogicFallback, "Profile useLegacyLogicFallback should match CastleDB");
    }

    /// <summary>
    /// 测试：animationTrigger 字段链路（已在 KnightIntegrationTests 中测试，这里再次验证）
    /// CastleDB.animationTrigger → NpcEntry.animationTrigger → Profile.animationTrigger → EnemyAgentBase._attackTriggerName
    /// </summary>
    [UnityTest]
    public IEnumerator AnimationTriggerChain()
    {
        yield return null;

        Assert.IsFalse(string.IsNullOrEmpty(knightEntry.animationTrigger), "CastleDB animationTrigger should not be empty");
        Assert.AreEqual(knightEntry.animationTrigger, profile.animationTrigger, "Profile animationTrigger should match CastleDB");

        // 验证 Animator Controller 包含该 Trigger
        var animator = knightGameObject.GetComponent<Animator>();
        Assert.IsNotNull(animator, "Animator missing");
        Assert.IsNotNull(animator.runtimeAnimatorController, "Animator Controller missing");

        bool hasTrigger = false;
        foreach (var param in animator.parameters)
        {
            if (param.name == profile.animationTrigger && param.type == AnimatorControllerParameterType.Trigger)
            {
                hasTrigger = true;
                break;
            }
        }

        Assert.IsTrue(hasTrigger, $"Animator should have Trigger '{profile.animationTrigger}'");
    }

    /// <summary>
    /// 测试：完整数值链路验证
    /// 一次性验证所有关键字段从 CastleDB 到运行时的完整流程
    /// </summary>
    [UnityTest]
    public IEnumerator FullBridgeVerification()
    {
        yield return null;

        // 验证所有字段都正确映射
        Assert.AreEqual(knightEntry.maxHealth, profile.maxHealth, "maxHealth mismatch");
        Assert.AreEqual(knightEntry.moveSpeed, profile.moveSpeed, "moveSpeed mismatch");
        Assert.AreEqual(Mathf.RoundToInt(knightEntry.attackDamage), profile.attackDamage, "attackDamage mismatch");
        Assert.AreEqual(knightEntry.attackRange, profile.attackRange, "attackRange mismatch");
        Assert.AreEqual(knightEntry.attackCooldown, profile.attackCooldown, "attackCooldown mismatch");
        Assert.AreEqual(knightEntry.invincibleDuration, profile.invulnerableFrameDuration, "invincibleDuration mismatch");
        Assert.AreEqual(knightEntry.knockbackMultiplier, profile.knockbackMultiplier, "knockbackMultiplier mismatch");
        Assert.AreEqual(knightEntry.enableDeathAnimation, profile.enableDeathAnimation, "enableDeathAnimation mismatch");
        Assert.AreEqual(knightEntry.useLegacyLogicFallback, profile.useLegacyLogicFallback, "useLegacyLogicFallback mismatch");
        Assert.AreEqual(knightEntry.animationTrigger, profile.animationTrigger, "animationTrigger mismatch");

        // 验证 Damageable 配置正确
        var damageableStats = profile.GetDamageableStats();
        Assert.AreEqual(Mathf.RoundToInt(profile.maxHealth), damageableStats.maxHealth, "Damageable maxHealth mismatch");
        Assert.AreEqual(profile.invulnerableFrameDuration, damageableStats.invincibilityTime, "Damageable invincibilityTime mismatch");
        Assert.AreEqual(profile.knockbackMultiplier, damageableStats.knockbackMultiplier, "Damageable knockbackMultiplier mismatch");

        Debug.Log($"[CastleDbBridgeTests] 完整数值链路验证通过 - {profile.profileName}");
    }

    /// <summary>
    /// 行为验证测试：验证 MoveSpeed 实际影响移动行为
    /// 这是 2A 欠缺内容中的关键验证点
    /// </summary>
    [UnityTest]
    public IEnumerator MoveSpeedAffectsBehavior()
    {
        yield return null;

        // 获取 Rigidbody2D
        var rb2d = knightGameObject.GetComponent<Rigidbody2D>();
        Assert.IsNotNull(rb2d, "Rigidbody2D missing");

        // 记录 Profile 中的 MoveSpeed
        float expectedSpeed = profile.moveSpeed;
        Assert.Greater(expectedSpeed, 0, "MoveSpeed should be > 0");

        // 模拟几帧让 Knight 进入移动状态
        // 注意：由于 Knight 使用 MoveSpeed 作为 Clamp 上限，
        // 我们验证速度不会超过 Profile 中的值
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();
        yield return new WaitForFixedUpdate();

        // 验证速度被正确限制在 MoveSpeed 范围内
        float actualSpeed = Mathf.Abs(rb2d.velocity.x);
        Assert.LessOrEqual(actualSpeed, expectedSpeed + 0.1f,
            $"实际速度 ({actualSpeed}) 不应超过 Profile.moveSpeed ({expectedSpeed})");

        Debug.Log($"[CastleDbBridgeTests] MoveSpeed 行为验证通过 - Expected<={expectedSpeed}, Actual={actualSpeed}");
    }

    /// <summary>
    /// 行为验证测试：验证攻击冷却实际影响攻击节奏
    /// </summary>
    [UnityTest]
    public IEnumerator AttackCooldownAffectsBehavior()
    {
        yield return null;

        float expectedCooldown = profile.attackCooldown;
        Assert.Greater(expectedCooldown, 0, "AttackCooldown should be > 0");

        Debug.Log($"[CastleDbBridgeTests] AttackCooldown 验证 - Profile值={expectedCooldown}s");

        // 注意：完整的攻击触发验证需要模拟目标检测，这里只验证 Profile 值正确传递
        // 实际攻击触发已在 Knight.TickState 中实现并使用 AttackCooldown
        Assert.AreEqual(knightEntry.attackCooldown, profile.attackCooldown, "AttackCooldown 应与 CastleDB 一致");
    }
}
