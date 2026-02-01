using System.Collections.Generic;
using CastleDB.Runtime;
using NUnit.Framework;
using UnityEngine;

public class StatusEffectControllerTests
{
    [Test]
    public void Apply_AddRule_IncreasesStacks_AndUpdatesMoveSpeedMultiplier()
    {
        GameObject target = null;
        StatusCatalog catalog = null;

        try
        {
            target = new GameObject("Target");
            var controller = target.AddComponent<StatusEffectController>();
            var stats = target.GetComponent<StatModifierLayer>();

            catalog = ScriptableObject.CreateInstance<StatusCatalog>();
            catalog.ApplyFromCastleDb(new List<StatusDefinition>
            {
                new StatusDefinition
                {
                    id = "Slow",
                    displayName = "Slow",
                    defaultDuration = 5f,
                    stackRule = StatusStackRule.Add,
                    maxStacks = 3,
                    modifiers = new StatusModifiers(moveSpeedMultiplier: 0.5f)
                }
            });

            controller.Initialize(catalog);

            Assert.IsTrue(controller.Apply("Slow"), "首次 Apply 应成功");
            Assert.AreEqual(1, controller.GetStacks("Slow"));
            Assert.AreEqual(0.5f, stats.MoveSpeedMultiplier, 1e-5f, "1 层 Slow 应将移速倍率设为 0.5");

            Assert.IsTrue(controller.Apply("Slow"), "二次 Apply(Add) 应成功");
            Assert.AreEqual(2, controller.GetStacks("Slow"));
            Assert.AreEqual(0.25f, stats.MoveSpeedMultiplier, 1e-5f, "2 层 Slow 应将移速倍率设为 0.25 (=0.5^2)");
        }
        finally
        {
            if (target != null)
            {
                Object.DestroyImmediate(target);
            }

            if (catalog != null)
            {
                Object.DestroyImmediate(catalog);
            }
        }
    }

    [Test]
    public void Tick_ExpiresTimedStatus_RemovesStatusAndResetsMultiplier()
    {
        GameObject target = null;
        StatusCatalog catalog = null;

        try
        {
            target = new GameObject("Target");
            var controller = target.AddComponent<StatusEffectController>();
            var stats = target.GetComponent<StatModifierLayer>();

            catalog = ScriptableObject.CreateInstance<StatusCatalog>();
            catalog.ApplyFromCastleDb(new List<StatusDefinition>
            {
                new StatusDefinition
                {
                    id = "Freeze",
                    displayName = "Freeze",
                    defaultDuration = 1f,
                    stackRule = StatusStackRule.Replace,
                    maxStacks = 1,
                    modifiers = new StatusModifiers(moveSpeedMultiplier: 0.2f)
                }
            });

            controller.Initialize(catalog);

            controller.Apply("Freeze");
            Assert.IsTrue(controller.HasStatus("Freeze"));
            Assert.AreEqual(0.2f, stats.MoveSpeedMultiplier, 1e-5f);

            controller.Tick(0.6f);
            Assert.IsTrue(controller.HasStatus("Freeze"));

            controller.Tick(0.6f); // 超过 1s
            Assert.IsFalse(controller.HasStatus("Freeze"), "超过持续时间后应自动过期");
            Assert.AreEqual(1f, stats.MoveSpeedMultiplier, 1e-5f, "过期后应清理移速倍率");
        }
        finally
        {
            if (target != null)
            {
                Object.DestroyImmediate(target);
            }

            if (catalog != null)
            {
                Object.DestroyImmediate(catalog);
            }
        }
    }
}

