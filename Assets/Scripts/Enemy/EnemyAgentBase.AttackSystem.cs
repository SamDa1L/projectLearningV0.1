using UnityEngine;

public abstract partial class EnemyAgentBase
{
    /// <summary>
    /// 0.5 阶段3：战斗判定（用于“命中即停走 + 冷却循环攻击”）
    /// - 只要 PrimaryAttack 或 SecondaryAttack 任意检测区里仍有目标，就视为“在战斗中”。
    /// - 控制器在战斗中应停止移动（原地输出），目标离开后恢复移动。
    /// </summary>
    protected bool HasAnyCombatTarget()
    {
        if (_hasTarget)
        {
            return true;
        }

        // SecondaryAttack 不走事件驱动（避免每个敌人都写额外绑定）。
        // 这里使用“阵营过滤后的目标列表”，避免把友军/中立也计入“战斗中”。
        var secondaryTargets = GetDetectedTargetsForRole(DetectionZoneBinding.Role.SecondaryAttack);
        return secondaryTargets != null && secondaryTargets.Count > 0;
    }

    /// <summary>
    /// 统一的攻击系统更新方法（供子类在 TickState 中调用）
    ///
    /// 功能：
    /// - 递减攻击冷却计时器
    /// - 同步 hasTarget 布尔参数到 Animator（可选）
    /// - 当满足攻击条件时（HasTarget && 冷却归零）触发攻击动画并执行回调
    ///
    /// 参数：
    /// - deltaTime: 帧间隔时间
    /// - onAttackTriggered: 攻击触发时的回调（可选），供子类做额外处理（如播放特效、状态切换）
    ///
    /// 文档来源：Docs代码冗余优化方案.md - 步骤2
    /// </summary>
    protected void TickAttackSystem(float deltaTime, System.Action onAttackTriggered = null)
    {
        // 1. 递减冷却计时器
        if (_attackCooldownTimer > 0f)
        {
            _attackCooldownTimer -= deltaTime;
        }

        // 2. PrimaryAttack / SecondaryAttack 互斥仲裁（0.5 阶段3）
        // - PrimaryAttack：只触发近战（不触发法术）
        // - SecondaryAttack：只触发法术（不触发近战）
        // - 同时命中：按 Profile.attackZonePriority / abilityZonePriority 互斥选择
        bool hasSecondaryTarget = false;
        if (_npcAbilityController != null)
        {
            if (_npcAbilityController.TickPendingCast())
            {
                if (animator != null)
                {
                    // 0.5 阶段3需求：只要主/副攻击检测区任意命中玩家，都应视为“战斗中”。
                    // - hasTarget=true：驱动 Animator 进入战斗/待机/攻击等状态机分支
                    // - 注意：canMove 由 AnimatorController 的 SetBoolBehaviour 统一控制（避免代码与动画双写导致抖动）
                    animator.SetBool(AnimationStrings.hasTarget, true);
                }

                return;
            }

            if (_npcAbilityController.TickPassiveAbilities(deltaTime))
            {
                if (animator != null)
                {
                    animator.SetBool(AnimationStrings.hasTarget, true);
                }

                return;
            }

            var secondaryTargets = GetDetectedTargetsForRole(DetectionZoneBinding.Role.SecondaryAttack);
            hasSecondaryTarget = secondaryTargets != null && secondaryTargets.Count > 0;
        }

        bool hasAnyTarget = _hasTarget || hasSecondaryTarget;
        if (animator != null)
        {
            animator.SetBool(AnimationStrings.hasTarget, hasAnyTarget);
        }

        bool shouldMelee = _hasTarget && (!hasSecondaryTarget || _attackZonePriority >= _abilityZonePriority);
        if (!shouldMelee && hasSecondaryTarget && _npcAbilityController != null)
        {
            if (_npcAbilityController.Tick(DetectionZoneBinding.Role.SecondaryAttack, deltaTime))
            {
                return;
            }
        }

        // 3. 检查是否满足近战攻击条件
        if (shouldMelee && _attackCooldownTimer <= 0f)
        {
            // 触发攻击动画
            TriggerAttackAnimation();

            // 执行回调（供子类做额外处理）
            onAttackTriggered?.Invoke();

            // 重置冷却计时器
            _attackCooldownTimer = _attackCooldown;

            if (debugStateOverlay)
            {
                Debug.Log($"[{gameObject.name}] TickAttackSystem 触发攻击 - Cooldown={_attackCooldown}s");
            }
        }
    }

    /// <summary>
    /// 触发攻击动画（统一入口）
    /// 使用 Profile 中配置的 animationTrigger 而不是硬编码
    ///
    /// 2A 要求：所有攻击动画触发必须通过此方法，禁止硬编码 Trigger 名称
    /// </summary>
    protected void TriggerAttackAnimation()
    {
        if (animator == null)
        {
            Debug.LogWarning($"[{gameObject.name}] Animator 为空，无法触发攻击动画", gameObject);
            return;
        }

        if (string.IsNullOrEmpty(_attackTriggerName))
        {
            Debug.LogWarning($"[{gameObject.name}] attackTriggerName 为空，无法触发攻击动画。请检查 TuningProfile 配置。", gameObject);
            return;
        }

        animator.SetTrigger(_attackTriggerName);

        if (debugStateOverlay)
        {
            Debug.Log($"[{gameObject.name}] 触发攻击动画: {_attackTriggerName}", gameObject);
        }
    }
}
