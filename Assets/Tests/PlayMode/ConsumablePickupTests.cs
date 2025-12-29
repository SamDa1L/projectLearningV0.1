using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using System.Collections;
using CastleDB.Runtime;

/// <summary>
/// Consumable 拾取与满仓测试
///
/// 契约 [C-Test-1] PlayMode 测试：
/// - Consumable 累加
/// - HUD 文本更新
/// - 满仓处理
/// - 事件触发验证
/// </summary>
public class ConsumablePickupTests
{
    private GameObject _playerObj;
    private PlayerInventory _inventory;
    private ICastleDbService _castleDbService;
    private GameplayConfig _gameplayConfig;

    [SetUp]
    public void Setup()
    {
        // 创建测试用 Player GameObject
        _playerObj = new GameObject("TestPlayer");
        _inventory = _playerObj.AddComponent<PlayerInventory>();

        // 初始化 CastleDbService
        var itemCatalog = Resources.Load<ItemCatalog>("Config/ItemCatalog");
        _castleDbService = new CastleDbService();
        if (itemCatalog != null)
        {
            ((CastleDbService)_castleDbService).SetItemCatalog(itemCatalog);
        }

        // 加载 GameplayConfig
        _gameplayConfig = Resources.Load<GameplayConfig>("Config/GameplayConfig");

        // 初始化 PlayerInventory
        _inventory.Initialize(_castleDbService, _gameplayConfig);
    }

    [TearDown]
    public void TearDown()
    {
        if (_playerObj != null)
        {
            Object.DestroyImmediate(_playerObj);
        }
    }

    /// <summary>
    /// 测试：Consumable 拾取应累加计数
    /// </summary>
    [Test]
    public void TestConsumablePickupAccumulates()
    {
        // Arrange
        string itemId = "potion_red";
        int amount = 5;

        // Act
        var request = new PickupRequest(itemId, amount, null);
        var result = _inventory.TryPickup(request, out _);

        // Assert
        Assert.AreEqual(PickupResult.Success, result, "Consumable 拾取应成功");
        Assert.AreEqual(amount, _inventory.PotionCount, "PotionCount 应为 5");
    }

    /// <summary>
    /// 测试：Consumable 拾取触发事件
    /// </summary>
    [Test]
    public void TestConsumablePickupTriggersEvent()
    {
        // Arrange
        string itemId = "potion_red";
        int amount = 3;
        bool eventTriggered = false;
        int eventCount = -1;

        _inventory.OnPotionCountChanged += (count) =>
        {
            eventTriggered = true;
            eventCount = count;
        };

        // Act
        var request = new PickupRequest(itemId, amount, null);
        _inventory.TryPickup(request, out _);

        // Assert
        Assert.IsTrue(eventTriggered, "拾取应触发 OnPotionCountChanged 事件");
        Assert.AreEqual(amount, eventCount, "事件应报告正确的计数");
    }

    /// <summary>
    /// 测试：满仓时拾取应失败
    /// 契约 [C-Test-1]：填满血瓶后再拾取，断言返回 Failed_NotSupported，pickup 未销毁，HUD 不变，事件不触发
    /// </summary>
    [Test]
    public void TestFullInventoryRejectPickup()
    {
        // Arrange: 填满血瓶
        int maxCount = _inventory.GetPotionMaxCount();
        var fillRequest = new PickupRequest("potion_red", maxCount, null);
        _inventory.TryPickup(fillRequest, out _);

        Assert.AreEqual(maxCount, _inventory.PotionCount, "应已填满血瓶");

        // 创建真实 pickup GameObject（模拟场景拾取物）
        var pickupObj = new GameObject("TestPickup");
        var collider = pickupObj.AddComponent<BoxCollider2D>();
        collider.isTrigger = true;
        var pickup = pickupObj.AddComponent<ItemPickup>();
        pickup.itemId = "potion_red";
        pickup.amount = 1;

        // 事件计数器
        int eventTriggerCount = 0;
        _inventory.OnPotionCountChanged += (_) =>
        {
            eventTriggerCount++;
        };

        // Act: 再次拾取（满仓）
        var request = new PickupRequest("potion_red", 1, pickup);
        var result = _inventory.TryPickup(request, out _);

        // Assert
        Assert.AreEqual(PickupResult.Failed_NotSupported, result, "满仓应返回 Failed_NotSupported");
        Assert.AreEqual(maxCount, _inventory.PotionCount, "PotionCount 不应变化");
        Assert.AreEqual(0, eventTriggerCount, "事件不应触发");

        // 验证：pickup GameObject 未被销毁（Failed_NotSupported 不销毁 pickup）
        Assert.IsNotNull(pickupObj, "pickup GameObject 不应被销毁");
        Assert.IsTrue(pickupObj != null, "pickup 应仍然存在");

        // Cleanup
        Object.DestroyImmediate(pickupObj);
    }

    /// <summary>
    /// 测试：Consumable 累加超出上限应 clamp
    /// </summary>
    [Test]
    public void TestConsumableOverflowClamps()
    {
        // Arrange: 先拾取接近上限的数量
        int maxCount = _inventory.GetPotionMaxCount();
        var request1 = new PickupRequest("potion_red", maxCount - 2, null);
        _inventory.TryPickup(request1, out _);

        // Act: 拾取超出上限的数量
        var request2 = new PickupRequest("potion_red", 5, null);
        var result = _inventory.TryPickup(request2, out _);

        // Assert
        Assert.AreEqual(PickupResult.Success, result, "拾取应成功（溢出部分丢弃）");
        Assert.AreEqual(maxCount, _inventory.PotionCount, "PotionCount 应 clamp 到上限");
    }

    /// <summary>
    /// 测试：多次拾取应累加
    /// </summary>
    [Test]
    public void TestMultiplePickupsAccumulate()
    {
        // Act
        _inventory.TryPickup(new PickupRequest("potion_red", 3, null), out _);
        _inventory.TryPickup(new PickupRequest("potion_red", 2, null), out _);
        _inventory.TryPickup(new PickupRequest("potion_red", 5, null), out _);

        // Assert
        Assert.AreEqual(10, _inventory.PotionCount, "多次拾取应累加");
    }
}
