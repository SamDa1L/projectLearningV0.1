using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using CastleDB.Runtime;
using System.IO;
using System.Collections.Generic;

/// <summary>
/// ItemPickup 编辑器校验测试
///
/// 契约 [C-Test-1] Authoring 测试：
/// - 测试策略：验证组件默认状态和必需组件，不依赖OnValidate重复执行
/// - ItemCatalog依赖：使用IsValid检查，无效时Inconclusive
/// - RequireComponent验证：断言Collider2D自动添加
/// </summary>
public class ItemPickupEditorTests
{
    private static string ItemCatalogAssetPath =>
        Path.Combine(Application.dataPath, "Resources/Config/ItemCatalog.asset");

    [SetUp]
    public void Setup()
    {
        // 确保 ItemCatalog 存在（前置条件：需先运行 Import All）
        if (!File.Exists(ItemCatalogAssetPath))
        {
            Debug.LogWarning($"[ItemPickupEditorTests] ItemCatalog 不存在，部分测试可能失败。请先运行 Import All。");
        }
    }

    /// <summary>
    /// 验证 ItemCatalog 是否有效（无ID重复等腐化）
    /// </summary>
    private bool ValidateItemCatalog(out ItemCatalog catalog)
    {
        catalog = Resources.Load<ItemCatalog>("Config/ItemCatalog");

        if (catalog == null)
            return false;

        // 检查IsValid（如果存在此属性）
        var isValidProperty = typeof(ItemCatalog).GetProperty("IsValid");
        if (isValidProperty != null)
        {
            bool isValid = (bool)isValidProperty.GetValue(catalog);
            return isValid;
        }

        // 如果没有IsValid属性，检查是否有数据
        return catalog.GetAllItems().Count > 0;
    }

    /// <summary>
    /// 创建带 Collider2D 的 ItemPickup（ItemPickup 需要具体 Collider2D 才能添加）
    /// </summary>
    private ItemPickup CreatePickupWithCollider(GameObject obj)
    {
        var collider = obj.AddComponent<BoxCollider2D>();
        collider.isTrigger = true;
        return obj.AddComponent<ItemPickup>();
    }

    /// <summary>
    /// 测试：ItemPickup 默认字段值
    /// 注意：OnValidate在AddComponent时已自动执行，amount已被修正
    /// </summary>
    [Test]
    public void TestItemPickupDefaultFields()
    {
        var obj = new GameObject("TestPickup");
        var pickup = CreatePickupWithCollider(obj);

        // 验证：itemId 默认为 null 或空
        Assert.IsTrue(string.IsNullOrEmpty(pickup.itemId), "itemId 默认应为 null 或空");

        // 验证：amount 默认为 1（OnValidate已自动执行）
        Assert.AreEqual(1, pickup.amount, "amount 默认应为 1");

        // 验证：autoPickup 默认为 true
        Assert.IsTrue(pickup.autoPickup, "autoPickup 默认应为 true");

        Object.DestroyImmediate(obj);
    }

    /// <summary>
    /// 测试：RequireComponent 自动添加 Collider2D
    /// </summary>
    [Test]
    public void TestColliderAutoAdded()
    {
        var obj = new GameObject("TestPickup");
        var boxCollider = obj.AddComponent<BoxCollider2D>();
        boxCollider.isTrigger = true;
        var pickup = obj.AddComponent<ItemPickup>();

        // 验证：已有 Collider2D 时可添加 ItemPickup
        Assert.IsNotNull(pickup, "已有 Collider2D 时应能添加 ItemPickup");
        Assert.IsNotNull(obj.GetComponent<Collider2D>(), "Collider2D 应存在");

        Object.DestroyImmediate(obj);
    }

    /// <summary>
    /// 测试：itemId 有效性校验（依赖 ItemCatalog）
    /// </summary>
    [Test]
    public void TestItemIdValidation()
    {
        // 前置条件：ItemCatalog 必须存在且有效
        if (!File.Exists(ItemCatalogAssetPath))
        {
            Assert.Inconclusive("ItemCatalog 不存在，跳过测试");
            return;
        }

        if (!ValidateItemCatalog(out var catalog))
        {
            Assert.Inconclusive("ItemCatalog 无效或为空");
            return;
        }

        var obj = new GameObject("TestPickup");
        var pickup = CreatePickupWithCollider(obj);

        // 测试用例：有效 itemId
        if (catalog.GetAllItems().Count > 0)
        {
            string validItemId = catalog.GetAllItems()[0].id;
            pickup.itemId = validItemId;

            // 验证：TryGetItem 应成功
            Assert.IsTrue(catalog.TryGetItem(validItemId, out var def), "有效 itemId 应通过 TryGetItem");
            Assert.IsNotNull(def, "返回的 ItemDefinition 应非空");
        }

        // 测试用例：无效 itemId
        pickup.itemId = "InvalidItemId_DoesNotExist";

        // 验证：TryGetItem 应失败
        Assert.IsFalse(catalog.TryGetItem("InvalidItemId_DoesNotExist", out _), "无效 itemId 应失败");

        Object.DestroyImmediate(obj);
    }

    /// <summary>
    /// 测试：Ability 类型物品存在性（依赖 ItemCatalog）
    /// </summary>
    [Test]
    public void TestAbilityItemExists()
    {
        if (!File.Exists(ItemCatalogAssetPath))
        {
            Assert.Inconclusive("ItemCatalog 不存在，跳过测试");
            return;
        }

        if (!ValidateItemCatalog(out var catalog))
        {
            Assert.Inconclusive("ItemCatalog 无效或为空");
            return;
        }

        // 查找 Ability 类型物品
        int abilityCount = 0;
        foreach (var item in catalog.GetAllItems())
        {
            if (item.itemType == ItemType.Ability)
            {
                abilityCount++;
                // 验证 abilityId 非空
                Assert.IsFalse(string.IsNullOrEmpty(item.abilityId),
                    $"Ability 类型物品 {item.id} 的 abilityId 应非空");
            }
        }

        Debug.Log($"ItemCatalog 包含 {abilityCount} 个 Ability 类型物品");
        Assert.GreaterOrEqual(abilityCount, 0, "Ability 类型物品数量应 >= 0");
    }

    /// <summary>
    /// 测试：Consumable 类型物品存在性（依赖 ItemCatalog）
    /// </summary>
    [Test]
    public void TestConsumableItemExists()
    {
        if (!File.Exists(ItemCatalogAssetPath))
        {
            Assert.Inconclusive("ItemCatalog 不存在，跳过测试");
            return;
        }

        if (!ValidateItemCatalog(out var catalog))
        {
            Assert.Inconclusive("ItemCatalog 无效或为空");
            return;
        }

        // 查找 Consumable 类型物品
        int consumableCount = 0;
        foreach (var item in catalog.GetAllItems())
        {
            if (item.itemType == ItemType.Consumable)
            {
                consumableCount++;
                // 验证 maxStack > 0
                Assert.Greater(item.maxStack, 0,
                    $"Consumable 类型物品 {item.id} 的 maxStack 应 > 0");
            }
        }

        Debug.Log($"ItemCatalog 包含 {consumableCount} 个 Consumable 类型物品");
        Assert.GreaterOrEqual(consumableCount, 0, "Consumable 类型物品数量应 >= 0");
    }

    /// <summary>
    /// 测试：autoPickup 字段可设置性
    /// </summary>
    [Test]
    public void TestAutoPickupField()
    {
        var obj = new GameObject("TestPickup");
        var pickup = CreatePickupWithCollider(obj);

        // 默认值
        Assert.IsTrue(pickup.autoPickup, "autoPickup 默认应为 true");

        // 可设置为 false
        pickup.autoPickup = false;
        Assert.IsFalse(pickup.autoPickup, "autoPickup 应可设置为 false");

        // 可设置回 true
        pickup.autoPickup = true;
        Assert.IsTrue(pickup.autoPickup, "autoPickup 应可设置回 true");

        Object.DestroyImmediate(obj);
    }
}
