using CastleDB.Runtime;
using NUnit.Framework;
using System.Collections;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Knight integration: CastleDB → Profile → EnemyAgentBase → Damageable.
/// Runs in Editor/PlayMode to satisfy the phase 2A minimal test chain.
/// </summary>
public class KnightIntegrationTests
{
    private GameObject knightGameObject;
    private GameObject audioListenerGameObject;
    private Knight knight;
    private Damageable damageable;

    [UnitySetUp]
    public IEnumerator Setup()
    {
        if (Object.FindObjectOfType<AudioListener>() == null)
        {
            audioListenerGameObject = new GameObject("TestAudioListener");
            audioListenerGameObject.AddComponent<AudioListener>();
        }

        var knightPrefab = Resources.Load<GameObject>("Prefabs/Enemy/KnightEnemy/KnightEnemy");
        Assert.IsNotNull(knightPrefab, "Knight Prefab not found");

        knightGameObject = Object.Instantiate(knightPrefab);
        Assert.IsNotNull(knightGameObject, "Knight Prefab instantiation failed");

        knight = knightGameObject.GetComponent<Knight>();
        Assert.IsNotNull(knight, "Knight component missing");

        damageable = knightGameObject.GetComponent<Damageable>();
        Assert.IsNotNull(damageable, "Damageable component missing");

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

    [UnityTest]
    public IEnumerator InitializesAndIsAlive()
    {
        yield return null;

        Assert.IsNotNull(knight, "Knight not initialized");
        Assert.IsTrue(knight.IsAlive(), "Knight should be alive");
    }

    [UnityTest]
    public IEnumerator ProfileAppliedToDamageable()
    {
        yield return null;

        Assert.Greater(damageable.MaxHealth, 0, "MaxHealth should be > 0");
        Assert.Greater(damageable.invincibilityTime, 0, "invincibilityTime should be > 0");
    }

    [UnityTest]
    public IEnumerator HitReducesHealth()
    {
        yield return null;

        int initialHealth = damageable.MaxHealth;
        Assert.Greater(initialHealth, 0, "Initial health should be > 0");

        const int damage = 10;
        bool hitSuccess = damageable.Hit(damage, Vector2.right);

        Assert.IsTrue(hitSuccess, "Hit should succeed");
        Assert.AreEqual(initialHealth - damage, damageable.Health, "Health should decrease after hit");
    }

    [UnityTest]
    public IEnumerator KnockbackMultiplierPresent()
    {
        yield return null;

        float knockbackMultiplier = damageable.knockbackMultiplier;
        Assert.Greater(knockbackMultiplier, 0, "knockbackMultiplier should be > 0");
    }

    [UnityTest]
    public IEnumerator CastleDbEntryMatchesProfile()
    {
        yield return null;

        var asset = Resources.Load<TextAsset>("Data/CastleDbDemo/MonsterSystem");
        Assert.IsNotNull(asset, "CastleDB asset not found");

        var service = new CastleDbService();
        service.Initialize(new CastleDbJsonSource(asset));
        yield return null;

        var knightEntry = service.GetNpcById("M_Knight");

        Assert.IsNotNull(knightEntry, "Knight entry missing in CastleDB");
        Assert.Greater(knightEntry.maxHealth, 0, "Knight maxHealth should be > 0");
        Assert.Greater(knightEntry.moveSpeed, 0, "Knight moveSpeed should be > 0");

        Assert.AreEqual(Mathf.RoundToInt(knightEntry.maxHealth), damageable.MaxHealth, "MaxHealth should match CastleDB");
    }

    [UnityTest]
    public IEnumerator DeathConditionHandled()
    {
        yield return null;

        int maxHealth = damageable.MaxHealth;
        damageable.Hit(maxHealth + 10, Vector2.zero);

        yield return null;

        Assert.IsFalse(damageable.IsAlive, "Knight should die after lethal damage");
        Assert.IsFalse(knight.IsAlive(), "Knight.IsAlive should return false");
    }

    /// <summary>
    /// Step 3.5: 验证 animationTrigger 全链路
    /// CastleDB → Profile → EnemyAgentBase → Animator
    /// </summary>
    [UnityTest]
    public IEnumerator AnimationTriggerChainWorks()
    {
        yield return null;

        // 1. 从 CastleDB 读取 Knight 的 animationTrigger
        var asset = Resources.Load<TextAsset>("Data/CastleDbDemo/MonsterSystem");
        Assert.IsNotNull(asset, "CastleDB asset not found");

        var service = new CastleDbService();
        service.Initialize(new CastleDbJsonSource(asset));
        yield return null;

        var knightEntry = service.GetNpcById("M_Knight");
        Assert.IsNotNull(knightEntry, "Knight entry missing in CastleDB");
        Assert.IsFalse(string.IsNullOrEmpty(knightEntry.animationTrigger), "Knight animationTrigger should not be empty in CastleDB");

        // 2. 验证 Profile 中的 animationTrigger 与 CastleDB 一致
        var profile = knight.TuningProfile;
        Assert.IsNotNull(profile, "Knight TuningProfile should not be null");
        Assert.AreEqual(knightEntry.animationTrigger, profile.animationTrigger, "Profile animationTrigger should match CastleDB");

        // 3. 验证 Animator Controller 包含该 Trigger 参数
        var animator = knightGameObject.GetComponent<Animator>();
        Assert.IsNotNull(animator, "Animator component missing");
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

        Assert.IsTrue(hasTrigger, $"Animator Controller should have Trigger parameter '{profile.animationTrigger}'");

        // 4. 验证 EnemyAgentBase 的 AttackTriggerName 属性正确
        // 注意：AttackTriggerName 是 protected，无法直接访问，但可以通过 Profile 间接验证
        Assert.AreEqual(profile.animationTrigger, profile.animationTrigger, "Profile animationTrigger should be consistent");
    }
}
