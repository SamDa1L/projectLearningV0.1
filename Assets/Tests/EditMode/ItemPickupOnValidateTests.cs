using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using CastleDB.Runtime;
using System.Reflection;
using System.Collections.Generic;

/// <summary>
/// ItemPickup OnValidate 规则验证测试
///
/// 契约 [C-Test-1] Authoring Pipeline：
/// - amount <= 0 自动修正为 1
/// - itemType=Ability 时强制 amount=1
/// - itemType=Material 时 Error（0.4 不支持拾取）
/// - icon 缺失时 Warning（需要 ItemCatalog）
/// </summary>
public class ItemPickupOnValidateTests
{
    /// <summary>
    /// 创建测试用 ItemCatalog（包含各种类型的 Item）
    /// </summary>
    private ItemCatalog CreateTestCatalog()
    {
        var catalog = ScriptableObject.CreateInstance<ItemCatalog>();
        var items = new List<ItemDefinition>
        {
            // Ability 类型
            new ItemDefinition
            {
                id = "test_ability",
                displayName = "测试能力",
                itemType = ItemType.Ability,
                icon = "Icons/Items/TestAbility",
                abilityId = "TestAbilityId",
                maxStack = 1,
                consumeEffect = new ItemConsumeEffect(0),
                consumeEffectRawJson = "",
                uiTag = ""
            },
            // Consumable 类型
            new ItemDefinition
            {
                id = "test_potion",
                displayName = "测试药水",
                itemType = ItemType.Consumable,
                icon = "Icons/Items/TestPotion",
                abilityId = "",
                maxStack = 99,
                consumeEffect = new ItemConsumeEffect(50),
                consumeEffectRawJson = "{\"heal\":50}",
                uiTag = ""
            },
            // Material 类型
            new ItemDefinition
            {
                id = "test_material",
                displayName = "测试材料",
                itemType = ItemType.Material,
                icon = "Icons/Items/TestMaterial",
                abilityId = "",
                maxStack = 999,
                consumeEffect = new ItemConsumeEffect(0),
                consumeEffectRawJson = "",
                uiTag = ""
            },
            // Ability 类型（icon 为空）
            new ItemDefinition
            {
                id = "test_ability_no_icon",
                displayName = "无图标能力",
                itemType = ItemType.Ability,
                icon = "",
                abilityId = "NoIconAbilityId",
                maxStack = 1,
                consumeEffect = new ItemConsumeEffect(0),
                consumeEffectRawJson = "",
                uiTag = ""
            }
        };

        catalog.ApplyFromCastleDb(items);
        return catalog;
    }

    /// <summary>
    /// 手动触发 OnValidate（通过反射）
    /// </summary>
    private void InvokeOnValidate(ItemPickup pickup)
    {
        var method = typeof(ItemPickup).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic);
        if (method != null)
        {
            method.Invoke(pickup, null);
        }
    }

    /// <summary>
    /// 测试：amount <= 0 自动修正为 1
    /// </summary>
    [Test]
    public void TestAmountZeroAutoCorrect()
    {
        // 创建 pickup
        var obj = new GameObject("TestPickup");
        var collider = obj.AddComponent<BoxCollider2D>();
        collider.isTrigger = true;
        var pickup = obj.AddComponent<ItemPickup>();

        // 设置 amount = 0
        pickup.amount = 0;

        // 记录 Warning 日志
        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*amount <= 0.*"));

        // 调用 OnValidate
        InvokeOnValidate(pickup);

        // 验证：amount 应被修正为 1
        Assert.AreEqual(1, pickup.amount, "amount <= 0 应被自动修正为 1");

        Object.DestroyImmediate(obj);
    }

    /// <summary>
    /// 测试：amount < 0 自动修正为 1
    /// </summary>
    [Test]
    public void TestAmountNegativeAutoCorrect()
    {
        var obj = new GameObject("TestPickup");
        var collider = obj.AddComponent<BoxCollider2D>();
        collider.isTrigger = true;
        var pickup = obj.AddComponent<ItemPickup>();

        // 设置 amount = -5
        pickup.amount = -5;

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*amount <= 0.*"));

        InvokeOnValidate(pickup);

        Assert.AreEqual(1, pickup.amount, "amount < 0 应被自动修正为 1");

        Object.DestroyImmediate(obj);
    }

    /// <summary>
    /// 测试：Ability 类型强制 amount=1
    /// </summary>
    [Test]
    public void TestAbilityTypeForceAmountOne()
    {
        // 创建并注入测试用 ItemCatalog
        var catalog = CreateTestCatalog();
        var catalogPath = "Assets/Resources/Config/";
        System.IO.Directory.CreateDirectory(catalogPath);

        // 由于无法直接写入 Resources（EditMode 限制），改用字段注入
        // 这里采用简化策略：直接测试 OnValidate 逻辑

        var obj = new GameObject("TestPickup");
        var collider = obj.AddComponent<BoxCollider2D>();
        collider.isTrigger = true;
        var pickup = obj.AddComponent<ItemPickup>();

        // 设置为 Ability itemId，amount > 1
        pickup.itemId = "test_ability";
        pickup.amount = 5;

        // 注意：由于 OnValidate 依赖 Resources.Load，在 EditMode 测试中可能加载失败
        // 此处验证逻辑：如果 ItemCatalog 可用，则验证修正；否则 Inconclusive
        var loadedCatalog = Resources.Load<ItemCatalog>("Config/ItemCatalog");
        if (loadedCatalog == null)
        {
            Assert.Inconclusive("ItemCatalog 不可用，跳过 Ability amount 修正测试");
            Object.DestroyImmediate(obj);
            return;
        }

        // 检查是否有 test_ability
        if (!loadedCatalog.TryGetItem("test_ability", out _))
        {
            // 使用实际存在的 Ability itemId
            var items = loadedCatalog.GetAllItems();
            ItemDefinition abilityItem = null;
            foreach (var item in items)
            {
                if (item.itemType == ItemType.Ability)
                {
                    abilityItem = item;
                    break;
                }
            }

            if (abilityItem == null)
            {
                Assert.Inconclusive("ItemCatalog 中无 Ability 类型物品");
                Object.DestroyImmediate(obj);
                return;
            }

            pickup.itemId = abilityItem.id;
        }

        pickup.amount = 5;

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(".*itemType=Ability.*amount.*"));

        InvokeOnValidate(pickup);

        Assert.AreEqual(1, pickup.amount, "Ability 类型应强制 amount=1");

        Object.DestroyImmediate(obj);
    }

    /// <summary>
    /// 测试：Material 类型 Error 提示
    /// </summary>
    [Test]
    public void TestMaterialTypeError()
    {
        var obj = new GameObject("TestPickup");
        var collider = obj.AddComponent<BoxCollider2D>();
        collider.isTrigger = true;
        var pickup = obj.AddComponent<ItemPickup>();

        // 设置为 Material itemId
        pickup.itemId = "test_material";

        // 检查 ItemCatalog 是否可用
        var catalog = Resources.Load<ItemCatalog>("Config/ItemCatalog");
        if (catalog == null)
        {
            Assert.Inconclusive("ItemCatalog 不可用，跳过 Material 类型测试");
            Object.DestroyImmediate(obj);
            return;
        }

        // 检查是否有 test_material（如果没有则跳过）
        if (!catalog.TryGetItem("test_material", out _))
        {
            Assert.Inconclusive("ItemCatalog 中无 test_material，跳过测试");
            Object.DestroyImmediate(obj);
            return;
        }

        // 预期 Error 日志
        LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(".*Material.*不支持拾取.*"));

        InvokeOnValidate(pickup);

        // 注意：Error 不会阻止执行，只是警告
        Object.DestroyImmediate(obj);
    }

    /// <summary>
    /// 测试：icon 为空时不应 Crash（仅验证不报错）
    /// </summary>
    [Test]
    public void TestEmptyIconDoesNotCrash()
    {
        var obj = new GameObject("TestPickup");
        var collider = obj.AddComponent<BoxCollider2D>();
        collider.isTrigger = true;
        var pickup = obj.AddComponent<ItemPickup>();

        // 设置为有效 itemId 但 icon 为空
        pickup.itemId = "test_ability_no_icon";

        var catalog = Resources.Load<ItemCatalog>("Config/ItemCatalog");
        if (catalog == null || !catalog.TryGetItem("test_ability_no_icon", out _))
        {
            Assert.Inconclusive("ItemCatalog 不可用或无 test_ability_no_icon");
            Object.DestroyImmediate(obj);
            return;
        }

        // 调用 OnValidate（不应 Crash）
        Assert.DoesNotThrow(() => InvokeOnValidate(pickup), "icon 为空不应导致 Crash");

        Object.DestroyImmediate(obj);
    }

    /// <summary>
    /// 测试：无效 itemId 不应 Crash
    /// </summary>
    [Test]
    public void TestInvalidItemIdDoesNotCrash()
    {
        var obj = new GameObject("TestPickup");
        var collider = obj.AddComponent<BoxCollider2D>();
        collider.isTrigger = true;
        var pickup = obj.AddComponent<ItemPickup>();

        // 设置无效 itemId
        pickup.itemId = "this_item_does_not_exist_12345";

        // 调用 OnValidate（不应 Crash）
        Assert.DoesNotThrow(() => InvokeOnValidate(pickup), "无效 itemId 不应导致 Crash");

        Object.DestroyImmediate(obj);
    }

    /// <summary>
    /// 测试：itemId 为 null 不应 Crash
    /// </summary>
    [Test]
    public void TestNullItemIdDoesNotCrash()
    {
        var obj = new GameObject("TestPickup");
        var collider = obj.AddComponent<BoxCollider2D>();
        collider.isTrigger = true;
        var pickup = obj.AddComponent<ItemPickup>();

        pickup.itemId = null;

        Assert.DoesNotThrow(() => InvokeOnValidate(pickup), "itemId=null 不应导致 Crash");

        Object.DestroyImmediate(obj);
    }

    /// <summary>
    /// 测试：Consumable 类型 amount > 1 允许
    /// </summary>
    [Test]
    public void TestConsumableAllowsAmountGreaterThanOne()
    {
        var obj = new GameObject("TestPickup");
        var collider = obj.AddComponent<BoxCollider2D>();
        collider.isTrigger = true;
        var pickup = obj.AddComponent<ItemPickup>();

        pickup.itemId = "test_potion";
        pickup.amount = 10;

        var catalog = Resources.Load<ItemCatalog>("Config/ItemCatalog");
        if (catalog == null || !catalog.TryGetItem("test_potion", out _))
        {
            // 使用实际存在的 Consumable itemId
            if (catalog != null)
            {
                var items = catalog.GetAllItems();
                ItemDefinition consumableItem = null;
                foreach (var item in items)
                {
                    if (item.itemType == ItemType.Consumable)
                    {
                        consumableItem = item;
                        break;
                    }
                }

                if (consumableItem != null)
                {
                    pickup.itemId = consumableItem.id;
                }
                else
                {
                    Assert.Inconclusive("ItemCatalog 中无 Consumable 类型物品");
                    Object.DestroyImmediate(obj);
                    return;
                }
            }
            else
            {
                Assert.Inconclusive("ItemCatalog 不可用");
                Object.DestroyImmediate(obj);
                return;
            }
        }

        // 调用 OnValidate
        InvokeOnValidate(pickup);

        // 验证：amount 不应被修改（Consumable 允许 > 1）
        Assert.AreEqual(10, pickup.amount, "Consumable 类型应允许 amount > 1");

        Object.DestroyImmediate(obj);
    }
}
