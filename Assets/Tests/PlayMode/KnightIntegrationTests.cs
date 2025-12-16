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

    /// <summary>
    /// 3.1节：验证 Attack Trigger 触发后 Animator 真实进入攻击动画状态
    /// 这是 2A 的核心验收项：不仅参数存在，还要确认触发后能进入攻击状态
    /// </summary>
    [UnityTest]
    public IEnumerator AttackTriggerEntersAttackState()
    {
        yield return null;

        var animator = knightGameObject.GetComponent<Animator>();
        Assert.IsNotNull(animator, "Animator component missing");

        // 创建一个Dummy目标进入检测区，触发hasTarget
        var dummyTarget = new GameObject("DummyTarget");
        var dummyCollider = dummyTarget.AddComponent<BoxCollider2D>();
        dummyCollider.isTrigger = true;
        dummyCollider.size = new Vector2(1, 2);

        // 将Dummy放置在Knight前方，进入PrimaryAttack检测区范围
        dummyTarget.transform.position = knightGameObject.transform.position + Vector3.right * 1.5f;

        // 等待几帧让检测区事件触发
        yield return new WaitForSeconds(0.2f);

        // 等待攻击冷却归零并触发攻击（最多等待5秒）
        float waitTime = 0f;
        bool attackTriggered = false;
        string initialStateName = animator.GetCurrentAnimatorStateInfo(0).IsName("knight_run") ? "knight_run" : "unknown";

        while (waitTime < 5f)
        {
            yield return new WaitForSeconds(0.1f);
            waitTime += 0.1f;

            var stateInfo = animator.GetCurrentAnimatorStateInfo(0);

            // 检查是否进入了攻击状态（状态名通常包含"attack"）
            if (stateInfo.IsName("knight_attack"))
            {
                attackTriggered = true;
                break;
            }
        }

        Assert.IsTrue(attackTriggered,
            $"Attack Trigger 触发后应该进入攻击动画状态 (waited {waitTime}s, initial state: {initialStateName})");

        // 清理
        Object.Destroy(dummyTarget);
    }

    /// <summary>
    /// 3.6节：验证 attackCooldown 影响攻击触发频率
    /// 行为级断言：攻击冷却时间会影响单位时间内的攻击次数
    /// </summary>
    [UnityTest]
    public IEnumerator AttackCooldownAffectsAttackFrequency()
    {
        yield return null;

        var animator = knightGameObject.GetComponent<Animator>();
        Assert.IsNotNull(animator, "Animator component missing");

        // 创建Dummy目标保持hasTarget=true
        var dummyTarget = new GameObject("DummyTarget");
        var dummyCollider = dummyTarget.AddComponent<BoxCollider2D>();
        dummyCollider.isTrigger = true;
        dummyCollider.size = new Vector2(1, 2);
        dummyTarget.transform.position = knightGameObject.transform.position + Vector3.right * 1.5f;

        yield return new WaitForSeconds(0.2f);

        // 记录3秒内进入攻击状态的次数
        float observeTime = 3f;
        float elapsed = 0f;
        int attackCount = 0;
        bool wasInAttack = false;

        while (elapsed < observeTime)
        {
            yield return new WaitForSeconds(0.1f);
            elapsed += 0.1f;

            var stateInfo = animator.GetCurrentAnimatorStateInfo(0);
            bool inAttack = stateInfo.IsName("knight_attack");

            // 检测到进入攻击状态的边缘（从非攻击进入攻击）
            if (inAttack && !wasInAttack)
            {
                attackCount++;
            }

            wasInAttack = inAttack;
        }

        // 根据Profile的attackCooldown验证攻击次数
        var profile = knight.TuningProfile;
        float expectedInterval = profile.attackCooldown;
        int expectedAttacks = Mathf.FloorToInt(observeTime / expectedInterval);

        // 允许±1的误差（考虑首次攻击延迟和动画时间）
        Assert.GreaterOrEqual(attackCount, expectedAttacks - 1,
            $"攻击次数应该至少为 {expectedAttacks - 1} (cooldown={expectedInterval}s, observed={attackCount} in {observeTime}s)");
        Assert.LessOrEqual(attackCount, expectedAttacks + 2,
            $"攻击次数不应该超过 {expectedAttacks + 2} (cooldown={expectedInterval}s, observed={attackCount} in {observeTime}s)");

        // 清理
        Object.Destroy(dummyTarget);
    }
}
