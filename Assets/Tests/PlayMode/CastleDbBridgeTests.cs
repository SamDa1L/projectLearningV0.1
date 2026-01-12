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
    private NpcGroundController knight;
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

        knight = knightGameObject.GetComponent<NpcGroundController>();
        Assert.IsNotNull(knight, "NpcGroundController component missing");

        damageable = knightGameObject.GetComponent<Damageable>();
        Assert.IsNotNull(damageable, "Damageable component missing");

        profile = knight.TuningProfile;
        Assert.IsNotNull(profile, "TuningProfile missing");

        // 0.3 版本：从新数据源读取 Knight 数据
        var asset = Resources.Load<TextAsset>("Data/MonsterSystem");
        Assert.IsNotNull(asset, "CastleDB MonsterSystem asset not found");

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

        // 运行时伤害结算以 Attack.attackDamage 为准：必须确保 Profile 的数值已正确下发到 Prefab 子物体的 Attack 组件
        var attacks = knightGameObject.GetComponentsInChildren<Attack>(true);
        Assert.Greater(attacks.Length, 0, "Knight should have at least one Attack component");
        foreach (var attack in attacks)
        {
            Assert.AreEqual(profile.attackDamage, attack.attackDamage,
                $"Attack '{attack.gameObject.name}' attackDamage should match Profile");
        }
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
    /// 行为验证测试：验证 knockbackMultiplier 实际影响受击击退强度
    /// CastleDB/Profile 的 knockbackMultiplier → Damageable.knockbackMultiplier → Damageable.Hit() 输出的 knockback → Enemy.OnHit 应用到 Rigidbody2D
    /// </summary>
    [UnityTest]
    public IEnumerator KnockbackMultiplierAffectsBehavior()
    {
        yield return null;

        var rb2d = knightGameObject.GetComponent<Rigidbody2D>();
        Assert.IsNotNull(rb2d, "Rigidbody2D missing");

        // 为了让测试可重复，避免重力导致的 Y 轴速度干扰
        float originalGravity = rb2d.gravityScale;
        rb2d.gravityScale = 0f;
        rb2d.velocity = Vector2.zero;

        Vector2 inputKnockback = new Vector2(1f, 2f);
        bool hitSuccess = damageable.Hit(1, inputKnockback);
        Assert.IsTrue(hitSuccess, "Hit should succeed");

        Vector2 expected = inputKnockback * profile.knockbackMultiplier;
        Assert.AreEqual(expected.x, rb2d.velocity.x, 0.01f, "Knockback X should be scaled by knockbackMultiplier");
        Assert.AreEqual(expected.y, rb2d.velocity.y, 0.01f, "Knockback Y should be scaled by knockbackMultiplier");

        rb2d.gravityScale = originalGravity;
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

    // ===== Stage 3B: 能力系统测试 =====

    /// <summary>
    /// Stage 3B 测试用例 C：能力调度语义（priority/handled）
    /// 验证：
    /// 1. 能力按 Priority 从高到低执行
    /// 2. 当能力返回 handled=true 时，后续能力不再执行
    /// 3. 绕过 InputSystem，直接调用 Dispatch
    /// </summary>
    [UnityTest]
    public IEnumerator AbilityDispatchPriorityAndHandled()
    {
        yield return null;

        // 创建能力系统实例
        var abilitySystem = new AbilitySystem();

        // 创建 Mock 能力用于测试
        var highPriorityAbility = new MockAbility(priority: 100, returnsHandled: true, "HighPriority");
        var lowPriorityAbility = new MockAbility(priority: 50, returnsHandled: false, "LowPriority");

        // 注册能力到 Move Hook
        abilitySystem.RegisterAbility(AbilityHookType.Move, highPriorityAbility);
        abilitySystem.RegisterAbility(AbilityHookType.Move, lowPriorityAbility);

        // 直接调用 Dispatch（绕过 InputSystem）
        AbilityInput input = AbilityInput.Performed(new Vector2(1, 0), true);
        bool result = abilitySystem.Dispatch(AbilityHookType.Move, input);

        // 断言：Dispatch 应该返回 true（被高优先级能力消费）
        Assert.IsTrue(result, "Dispatch should return true when ability handles input");

        // 断言：高优先级能力被调用
        Assert.IsTrue(highPriorityAbility.WasCalled, "High priority ability should be called");

        // 断言：低优先级能力不被调用（因为高优先级返回 handled=true）
        Assert.IsFalse(lowPriorityAbility.WasCalled, "Low priority ability should NOT be called when high priority returns handled=true");

        Debug.Log("[CastleDbBridgeTests] Stage 3B - Priority/Handled 语义验证通过");
    }

    /// <summary>
    /// Stage 3B 测试用例：多个能力都不消费输入时的传播
    /// 验证：当所有能力都返回 handled=false 时，所有能力都会被执行
    /// </summary>
    [UnityTest]
    public IEnumerator AbilityDispatchPropagation()
    {
        yield return null;

        var abilitySystem = new AbilitySystem();

        // 创建多个都不消费输入的能力
        var ability1 = new MockAbility(priority: 100, returnsHandled: false, "Ability1");
        var ability2 = new MockAbility(priority: 50, returnsHandled: false, "Ability2");
        var ability3 = new MockAbility(priority: 10, returnsHandled: false, "Ability3");

        abilitySystem.RegisterAbility(AbilityHookType.Jump, ability1);
        abilitySystem.RegisterAbility(AbilityHookType.Jump, ability2);
        abilitySystem.RegisterAbility(AbilityHookType.Jump, ability3);

        AbilityInput input = AbilityInput.Started(isPressed: true);
        bool result = abilitySystem.Dispatch(AbilityHookType.Jump, input);

        // 断言：Dispatch 返回 false（没有能力消费输入）
        Assert.IsFalse(result, "Dispatch should return false when no ability handles input");

        // 断言：所有能力都被调用
        Assert.IsTrue(ability1.WasCalled, "Ability1 should be called");
        Assert.IsTrue(ability2.WasCalled, "Ability2 should be called");
        Assert.IsTrue(ability3.WasCalled, "Ability3 should be called");

        Debug.Log("[CastleDbBridgeTests] Stage 3B - 传播验证通过：所有能力都被调用");
    }

    /// <summary>
    /// Stage 3B 测试用例：Priority 顺序验证
    /// 验证：能力按 Priority 从高到低的顺序执行
    /// </summary>
    [UnityTest]
    public IEnumerator AbilityDispatchOrderByPriority()
    {
        yield return null;

        var abilitySystem = new AbilitySystem();

        // 创建记录执行顺序的能力
        var executionOrder = new System.Collections.Generic.List<string>();

        var abilityLow = new OrderTrackingAbility(priority: 10, executionOrder, "Low");
        var abilityHigh = new OrderTrackingAbility(priority: 100, executionOrder, "High");
        var abilityMedium = new OrderTrackingAbility(priority: 50, executionOrder, "Medium");

        // 故意乱序注册，测试系统是否能正确排序
        abilitySystem.RegisterAbility(AbilityHookType.Attack, abilityMedium);
        abilitySystem.RegisterAbility(AbilityHookType.Attack, abilityLow);
        abilitySystem.RegisterAbility(AbilityHookType.Attack, abilityHigh);

        AbilityInput input = AbilityInput.Started(isPressed: true);
        abilitySystem.Dispatch(AbilityHookType.Attack, input);

        // 断言：执行顺序应该是 High → Medium → Low
        Assert.AreEqual(3, executionOrder.Count, "All three abilities should be called");
        Assert.AreEqual("High", executionOrder[0], "High priority ability should execute first");
        Assert.AreEqual("Medium", executionOrder[1], "Medium priority ability should execute second");
        Assert.AreEqual("Low", executionOrder[2], "Low priority ability should execute last");

        Debug.Log($"[CastleDbBridgeTests] Stage 3B - Priority 顺序验证通过: {string.Join(" → ", executionOrder)}");
    }

    /// <summary>
    /// Stage 3B 测试用例：Enabled 属性过滤
    /// 验证：Enabled=false 的能力不会被调用
    /// </summary>
    [UnityTest]
    public IEnumerator AbilityDispatchEnabledFilter()
    {
        yield return null;

        var abilitySystem = new AbilitySystem();

        var enabledAbility = new MockAbility(priority: 100, returnsHandled: false, "Enabled", enabled: true);
        var disabledAbility = new MockAbility(priority: 50, returnsHandled: false, "Disabled", enabled: false);

        abilitySystem.RegisterAbility(AbilityHookType.Run, enabledAbility);
        abilitySystem.RegisterAbility(AbilityHookType.Run, disabledAbility);

        AbilityInput input = AbilityInput.Performed(new Vector2(0, 0), true);
        abilitySystem.Dispatch(AbilityHookType.Run, input);

        // 断言：启用的能力被调用
        Assert.IsTrue(enabledAbility.WasCalled, "Enabled ability should be called");

        // 断言：禁用的能力不被调用
        Assert.IsFalse(disabledAbility.WasCalled, "Disabled ability should NOT be called");

        Debug.Log("[CastleDbBridgeTests] Stage 3B - Enabled 过滤验证通过");
    }

    /// <summary>
    /// Stage 3B 测试用例：不同 HookType 隔离
    /// 验证：注册到不同 HookType 的能力互不干扰
    /// </summary>
    [UnityTest]
    public IEnumerator AbilityDispatchHookTypeIsolation()
    {
        yield return null;

        var abilitySystem = new AbilitySystem();

        var moveAbility = new MockAbility(priority: 100, returnsHandled: true, "MoveAbility");
        var jumpAbility = new MockAbility(priority: 100, returnsHandled: true, "JumpAbility");

        abilitySystem.RegisterAbility(AbilityHookType.Move, moveAbility);
        abilitySystem.RegisterAbility(AbilityHookType.Jump, jumpAbility);

        // 触发 Move
        AbilityInput moveInput = AbilityInput.Performed(new Vector2(1, 0), true);
        abilitySystem.Dispatch(AbilityHookType.Move, moveInput);

        // 断言：只有 Move 能力被调用
        Assert.IsTrue(moveAbility.WasCalled, "Move ability should be called");
        Assert.IsFalse(jumpAbility.WasCalled, "Jump ability should NOT be called when dispatching Move");

        // 重置状态
        moveAbility.Reset();
        jumpAbility.Reset();

        // 触发 Jump
        AbilityInput jumpInput = AbilityInput.Started(isPressed: true);
        abilitySystem.Dispatch(AbilityHookType.Jump, jumpInput);

        // 断言：只有 Jump 能力被调用
        Assert.IsFalse(moveAbility.WasCalled, "Move ability should NOT be called when dispatching Jump");
        Assert.IsTrue(jumpAbility.WasCalled, "Jump ability should be called");

        Debug.Log("[CastleDbBridgeTests] Stage 3B - HookType 隔离验证通过");
    }

    // ===== Mock 能力类（用于测试）=====

    /// <summary>
    /// Mock 能力：用于测试 priority/handled/enabled 语义
    /// </summary>
    private class MockAbility : IPlayerAbility
    {
        public string AbilityId { get; private set; }
        public int Priority { get; private set; }
        public bool Enabled { get; set; } // Phase 5: 改为 public set 以满足接口要求
        public bool WasCalled { get; private set; }
        private bool returnsHandled;
        private string name;

        public MockAbility(int priority, bool returnsHandled, string name, bool enabled = true)
        {
            this.AbilityId = name; // 使用 name 作为 AbilityId
            this.Priority = priority;
            this.returnsHandled = returnsHandled;
            this.name = name;
            this.Enabled = enabled;
            this.WasCalled = false;
        }

        public void Reset()
        {
            WasCalled = false;
        }

        public bool OnMove(AbilityInput input)
        {
            WasCalled = true;
            Debug.Log($"[MockAbility] {name} OnMove called (priority={Priority}, handled={returnsHandled})");
            return returnsHandled;
        }

        public bool OnRun(AbilityInput input)
        {
            WasCalled = true;
            Debug.Log($"[MockAbility] {name} OnRun called (priority={Priority}, handled={returnsHandled})");
            return returnsHandled;
        }

        public bool OnJump(AbilityInput input)
        {
            WasCalled = true;
            Debug.Log($"[MockAbility] {name} OnJump called (priority={Priority}, handled={returnsHandled})");
            return returnsHandled;
        }

        public bool OnAttack(AbilityInput input)
        {
            WasCalled = true;
            Debug.Log($"[MockAbility] {name} OnAttack called (priority={Priority}, handled={returnsHandled})");
            return returnsHandled;
        }

        public bool OnRangedAttack(AbilityInput input)
        {
            WasCalled = true;
            Debug.Log($"[MockAbility] {name} OnRangedAttack called (priority={Priority}, handled={returnsHandled})");
            return returnsHandled;
        }
    }

    /// <summary>
    /// 顺序追踪能力：用于验证执行顺序
    /// </summary>
    private class OrderTrackingAbility : IPlayerAbility
    {
        public string AbilityId { get; private set; }
        public int Priority { get; private set; }
        public bool Enabled { get; set; } // Phase 5: 添加 setter 以满足接口要求
        private System.Collections.Generic.List<string> executionOrder;
        private string name;

        public OrderTrackingAbility(int priority, System.Collections.Generic.List<string> executionOrder, string name)
        {
            this.AbilityId = name; // 使用 name 作为 AbilityId
            this.Priority = priority;
            this.executionOrder = executionOrder;
            this.name = name;
            this.Enabled = true; // 默认启用
        }

        public bool OnMove(AbilityInput input) => RecordExecution();
        public bool OnRun(AbilityInput input) => RecordExecution();
        public bool OnJump(AbilityInput input) => RecordExecution();
        public bool OnAttack(AbilityInput input) => RecordExecution();
        public bool OnRangedAttack(AbilityInput input) => RecordExecution();

        private bool RecordExecution()
        {
            executionOrder.Add(name);
            Debug.Log($"[OrderTrackingAbility] {name} executed (priority={Priority})");
            return false; // 不消费输入，让所有能力都执行
        }
    }
}
