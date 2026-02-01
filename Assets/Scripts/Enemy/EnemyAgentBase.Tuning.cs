using UnityEngine;

public abstract partial class EnemyAgentBase
{
    // ===== 初始化钩子 =====

    /// <summary>
    /// 初始化钩子，在Awake之后、Start之前
    /// 子类可覆盖此方法来进行自定义初始化
    /// </summary>
    protected virtual void Initialize()
    {
        // 验证调参配置
        if (tuningProfile == null)
            Debug.LogWarning($"[{gameObject.name}] TuningProfile未分配，敌人参数将无法正确加载", gameObject);

        // 应用调参配置到基础组件
        ApplyTuningProfile();

        // 确保受击事件链路可用（否则 knockbackMultiplier 虽然导入成功，但不会产生任何位移效果）
        EnsureDamageableHitListener();

        // 子类实现
    }

    private void EnsureDamageableHitListener()
    {
        if (damageable == null || damageable.damageableHit == null)
            return;

        // Prefab/Inspector 已经配置了持久化回调时，不在运行时重复绑定，避免重复击退
        if (damageable.damageableHit.GetPersistentEventCount() > 0)
            return;

        // 没有持久化回调时，默认绑定到本基类的 OnHit（用于敌人受击击退）
        damageable.damageableHit.RemoveListener(OnHit);
        damageable.damageableHit.AddListener(OnHit);
    }

    /// <summary>
    /// 应用调参配置到敌人
    /// 从EnemyTuningProfile读取所有参数并应用到敌人组件
    /// 在Initialize()中自动调用
    ///
    /// 2A 要求：填充运行时缓存，让所有数值从 Profile 下发
    /// 子类应覆盖此方法来应用自己特有的参数
    /// </summary>
    protected virtual void ApplyTuningProfile()
    {
        if (tuningProfile == null)
            return;

        // ===== 应用阵营（0.5 Summon 扩展）=====
        // 说明：项目的战斗/检测依赖 Layer 碰撞矩阵，因此阵营需要映射到 Layer。
        var factionMember = GetComponent<FactionMember>();
        if (factionMember == null)
        {
            factionMember = gameObject.AddComponent<FactionMember>();
        }
        factionMember.Faction = tuningProfile.faction;
        FactionLayerApplier.Apply(gameObject, factionMember.Faction);

        // ===== 填充运行时数值缓存 =====
        _moveSpeed = tuningProfile.moveSpeed;
        _attackDamage = tuningProfile.attackDamage;
        _attackRange = tuningProfile.attackRange;
        _attackCooldown = tuningProfile.attackCooldown;
        _attackZonePriority = tuningProfile.attackZonePriority;
        _abilityZonePriority = tuningProfile.abilityZonePriority;
        _perceptionRadius = tuningProfile.perceptionRadius;
        _knockbackMultiplier = tuningProfile.knockbackMultiplier;
        _knockbackToPlayer = tuningProfile.knockbackToPlayer;
        _enableDeathAnimation = tuningProfile.enableDeathAnimation;
        _attackTriggerName = tuningProfile.animationTrigger;

        // ===== 应用 Damageable 配置 =====
        if (damageable != null)
        {
            var stats = tuningProfile.GetDamageableStats();
            damageable.Configure(stats);
        }

        // ===== 应用 knockbackToPlayer 到所有 Attack 组件（Monster → Player 击退缩放）=====
        ApplyKnockbackToPlayerScale();

        // TODO: 同步感知半径到 DetectionZone（如果支持动态半径）
        // 例如：if (detectionZone != null) detectionZone.SetRadius(_perceptionRadius);

        if (debugStateOverlay)
        {
            Debug.Log(
                $"[{gameObject.name}] 调参配置已应用\n" +
                $"  Profile: {tuningProfile.profileName}\n" +
                $"  数值缓存：\n" +
                $"    MoveSpeed={_moveSpeed}, AttackDamage={_attackDamage}\n" +
                $"    AttackRange={_attackRange}, AttackCooldown={_attackCooldown}, ZonePriority={_attackZonePriority}/{_abilityZonePriority}\n" +
                $"    PerceptionRadius={_perceptionRadius}, KnockbackMult={_knockbackMultiplier}\n" +
                $"    KnockbackToPlayer={_knockbackToPlayer}\n" +
                $"    AnimTrigger='{_attackTriggerName}'",
                gameObject
            );
        }
    }

    /// <summary>
    /// 将 Profile 下发到本敌人层级下所有 Attack 组件
    ///
    /// 实现说明：
    /// - 遍历敌人及其所有子物体上的 Attack 组件
    /// - 缓存每个 Attack 的原始 knockback（Prefab 基础值），避免重复缩放
    /// - 应用缩放：attack.knockback = baseKnockback * knockbackToPlayer
    /// - 下发伤害：attack.attackDamage = Profile.attackDamage
    ///
    /// 关键点：
    /// - Attack 脚本同时被玩家与敌人复用，因此不在 Attack 里做"向上找 Profile"
    /// - 由敌人侧（EnemyAgentBase）统一下发，避免误伤玩家攻击
    /// - 使用 _attackBaseKnockbacks 字典缓存基础值，防止重复初始化时数值被乘多次
    /// </summary>
    private void ApplyKnockbackToPlayerScale()
    {
        // 获取本敌人层级下所有 Attack 组件（包括子物体）
        var attacks = GetComponentsInChildren<Attack>(true);

        if (attacks.Length == 0)
        {
            if (debugStateOverlay)
            {
                Debug.Log($"[{gameObject.name}] 未找到任何 Attack 组件，跳过 knockbackToPlayer 应用", gameObject);
            }
            return;
        }

        int appliedCount = 0;

        foreach (var attack in attacks)
        {
            // 如果是首次处理该 Attack，缓存其原始 knockback
            if (!_attackBaseKnockbacks.ContainsKey(attack))
            {
                _attackBaseKnockbacks[attack] = attack.knockback;
            }

            // 获取基础击退值
            Vector2 baseKnockback = _attackBaseKnockbacks[attack];

            // 应用缩放
            attack.knockback = baseKnockback * _knockbackToPlayer;

            // 下发伤害（命中结算以 Attack.attackDamage 为准）
            attack.attackDamage = _attackDamage;
            appliedCount++;

            if (debugStateOverlay)
            {
                Debug.Log(
                    $"[{gameObject.name}] Attack '{attack.gameObject.name}' knockback 已缩放\n" +
                    $"  基础值: {baseKnockback} → 缩放后: {attack.knockback} (scale={_knockbackToPlayer})",
                    attack.gameObject
                );
            }
        }

        if (debugStateOverlay)
        {
            Debug.Log($"[{gameObject.name}] knockbackToPlayer 已应用到 {appliedCount} 个 Attack 组件", gameObject);
        }
    }
}
