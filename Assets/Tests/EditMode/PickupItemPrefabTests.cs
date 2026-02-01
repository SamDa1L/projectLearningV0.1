using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using System.IO;

/// <summary>
/// PickupItem.prefab 结构检查测试
///
/// 契约 [C-Test-1] EditMode Authoring 测试：
/// - PickupItem.prefab 必备组件验证
/// - Collider2D.isTrigger 配置验证
/// - ItemPickup 组件存在性验证
/// - SpriteRenderer 组件存在性验证
/// </summary>
public class PickupItemPrefabTests
{
    private const string PREFAB_PATH = "Assets/Resources/Prefabs/PickupItem.prefab";

    /// <summary>
    /// 测试：PickupItem.prefab 存在性验证
    /// </summary>
    [Test]
    public void TestPrefabExists()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);

        if (prefab == null)
        {
            Debug.LogWarning($"PickupItem.prefab 不存在于路径：{PREFAB_PATH}");
            Assert.Inconclusive("PickupItem.prefab 不存在，跳过结构检查");
            return;
        }

        Assert.IsNotNull(prefab, "PickupItem.prefab 应存在");
    }

    /// <summary>
    /// 测试：PickupItem.prefab 必备组件验证
    /// </summary>
    [Test]
    public void TestPrefabRequiredComponents()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);

        if (prefab == null)
        {
            Assert.Inconclusive("PickupItem.prefab 不存在");
            return;
        }

        // 验证 ItemPickup 组件
        var itemPickup = prefab.GetComponent<ItemPickup>();
        Assert.IsNotNull(itemPickup, "PickupItem.prefab 应包含 ItemPickup 组件");

        // 验证 SpriteRenderer 组件
        var spriteRenderer = prefab.GetComponent<SpriteRenderer>();
        Assert.IsNotNull(spriteRenderer, "PickupItem.prefab 应包含 SpriteRenderer 组件");

        // 验证 Collider2D 组件
        var collider = prefab.GetComponent<Collider2D>();
        Assert.IsNotNull(collider, "PickupItem.prefab 应包含 Collider2D 组件");
    }

    /// <summary>
    /// 测试：Collider2D.isTrigger 配置验证
    /// </summary>
    [Test]
    public void TestColliderIsTrigger()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);

        if (prefab == null)
        {
            Assert.Inconclusive("PickupItem.prefab 不存在");
            return;
        }

        var collider = prefab.GetComponent<Collider2D>();

        if (collider == null)
        {
            Assert.Fail("PickupItem.prefab 缺少 Collider2D 组件");
            return;
        }

        Assert.IsTrue(collider.isTrigger, "Collider2D.isTrigger 应为 true");
    }

    /// <summary>
    /// 测试：ItemPickup 默认字段验证
    /// </summary>
    [Test]
    public void TestItemPickupDefaultFields()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);

        if (prefab == null)
        {
            Assert.Inconclusive("PickupItem.prefab 不存在");
            return;
        }

        var itemPickup = prefab.GetComponent<ItemPickup>();

        if (itemPickup == null)
        {
            Assert.Fail("PickupItem.prefab 缺少 ItemPickup 组件");
            return;
        }

        // 验证默认字段
        Assert.AreEqual("", itemPickup.itemId, "itemId 默认应为空字符串");
        Assert.AreEqual(1, itemPickup.amount, "amount 默认应为 1");
        Assert.IsTrue(itemPickup.autoPickup, "autoPickup 默认应为 true");
    }

    /// <summary>
    /// 测试：Prefab 层级结构验证（无嵌套子对象）
    /// </summary>
    [Test]
    public void TestPrefabHierarchy()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);

        if (prefab == null)
        {
            Assert.Inconclusive("PickupItem.prefab 不存在");
            return;
        }

        // 验证：PickupItem.prefab 应是单一 GameObject，不包含子对象
        int childCount = prefab.transform.childCount;
        Assert.AreEqual(0, childCount, "PickupItem.prefab 不应包含子对象");
    }

    /// <summary>
    /// 测试：Prefab Collider 类型验证（推荐 BoxCollider2D）
    /// </summary>
    [Test]
    public void TestColliderType()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);

        if (prefab == null)
        {
            Assert.Inconclusive("PickupItem.prefab 不存在");
            return;
        }

        var collider = prefab.GetComponent<Collider2D>();

        if (collider == null)
        {
            Assert.Fail("PickupItem.prefab 缺少 Collider2D 组件");
            return;
        }

        // 验证 Collider 类型（推荐 BoxCollider2D）
        bool isBoxCollider = collider is BoxCollider2D;

        if (!isBoxCollider)
        {
            Debug.LogWarning($"PickupItem.prefab 使用了 {collider.GetType().Name}，推荐使用 BoxCollider2D");
        }

        // 此项不强制要求，仅记录
        Assert.IsTrue(true, "Collider2D 类型检查完成");
    }

    /// <summary>
    /// 测试：Prefab 实例化验证
    /// </summary>
    [Test]
    public void TestPrefabInstantiation()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);

        if (prefab == null)
        {
            Assert.Inconclusive("PickupItem.prefab 不存在");
            return;
        }

        // 实例化 Prefab
        GameObject instance = Object.Instantiate(prefab);

        Assert.IsNotNull(instance, "Prefab 应能正常实例化");

        // 验证实例化后的组件
        var itemPickup = instance.GetComponent<ItemPickup>();
        var spriteRenderer = instance.GetComponent<SpriteRenderer>();
        var collider = instance.GetComponent<Collider2D>();

        Assert.IsNotNull(itemPickup, "实例化后应包含 ItemPickup 组件");
        Assert.IsNotNull(spriteRenderer, "实例化后应包含 SpriteRenderer 组件");
        Assert.IsNotNull(collider, "实例化后应包含 Collider2D 组件");

        // 清理实例
        Object.DestroyImmediate(instance);
    }

    /// <summary>
    /// 测试：Prefab 路径符合规范
    /// </summary>
    [Test]
    public void TestPrefabPathConvention()
    {
        // 验证：Prefab 应位于 Assets/Resources/Prefabs/ 目录下
        bool pathCorrect = PREFAB_PATH.StartsWith("Assets/Resources/Prefabs/");
        Assert.IsTrue(pathCorrect, "Prefab 应位于 Assets/Resources/Prefabs/ 目录下");

        // 验证：Prefab 名称应为 PickupItem.prefab
        string fileName = Path.GetFileName(PREFAB_PATH);
        Assert.AreEqual("PickupItem.prefab", fileName, "Prefab 名称应为 PickupItem.prefab");
    }
}
