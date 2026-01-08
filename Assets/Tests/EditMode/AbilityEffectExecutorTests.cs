using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class AbilityEffectExecutorTests
{
    [Test]
    public void ExecuteOnHit_Damage_ReducesHealth()
    {
        GameObject target = null;

        try
        {
            target = new GameObject("Target");
            var damageable = target.AddComponent<Damageable>();
            damageable.MaxHealth = 100f;
            damageable.Health = 100f;
            damageable.invincibilityTime = 0f; // 避免 Hit 后进入无敌状态影响断言

            var effects = new List<AbilityEffectSpec>
            {
                new AbilityEffectSpec
                {
                    type = AbilityEffectType.Damage,
                    damage = 10,
                    knockback = Vector2.zero
                }
            };

            AbilityEffectExecutor.ExecuteOnHit("TestAbility", effects, caster: null, target: target);

            Assert.AreEqual(90f, damageable.Health, "Damage effect 应扣减目标生命值");
        }
        finally
        {
            if (target != null)
            {
                Object.DestroyImmediate(target);
            }
        }
    }

    [Test]
    public void ExecuteOnHit_ApplyStatus_AddsStatusId()
    {
        GameObject target = null;

        try
        {
            target = new GameObject("Target");
            var controller = target.AddComponent<StatusEffectController>();

            var effects = new List<AbilityEffectSpec>
            {
                new AbilityEffectSpec
                {
                    type = AbilityEffectType.ApplyStatus,
                    statusId = "Freeze",
                    durationOverride = 1.5f
                }
            };

            AbilityEffectExecutor.ExecuteOnHit("TestAbility", effects, caster: null, target: target);

            Assert.IsTrue(controller.HasStatus("Freeze"), "ApplyStatus effect 应记录状态 ID");
        }
        finally
        {
            if (target != null)
            {
                Object.DestroyImmediate(target);
            }
        }
    }
}

