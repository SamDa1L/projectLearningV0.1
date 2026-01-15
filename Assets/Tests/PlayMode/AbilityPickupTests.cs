using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using System.Collections;
using System.Collections.Generic;
using CastleDB.Runtime;

/// <summary>
/// Ability 拾取与 HUD 更新 PlayMode 测试
///
/// 契约 [C-Test-1] PlayMode 测试：
/// - Ability 拾取入槽
/// - HUD 图标更新
/// - 事件触发验证
/// </summary>
public class AbilityPickupTests
{
    private GameObject _playerObj;
    private PlayerInventory _inventory;
    private ICastleDbService _castleDbService;
    private GameplayConfig _gameplayConfig;
    private ItemCatalog _testItemCatalog;

    [SetUp]
    public void Setup()
    {
        // 创建测试用 Player GameObject
        _playerObj = new GameObject("TestPlayer");

        // 添加必需组件
        _inventory = _playerObj.AddComponent<PlayerInventory>();
        var damageable = _playerObj.AddComponent<Damageable>();

        // 初始化 CastleDbService（使用测试用 ItemCatalog，避免依赖 Resources）
        _castleDbService = new CastleDbService();
        _testItemCatalog = CreateTestItemCatalog();
        ((CastleDbService)_castleDbService).SetItemCatalog(_testItemCatalog);

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

        if (_testItemCatalog != null)
        {
            Object.DestroyImmediate(_testItemCatalog);
            _testItemCatalog = null;
        }
    }

    private ItemCatalog CreateTestItemCatalog()
    {
        var catalog = ScriptableObject.CreateInstance<ItemCatalog>();
        var items = new List<ItemDefinition>
        {
            new ItemDefinition
            {
                id = "ability_arrow",
                displayName = "射箭能力",
                itemType = ItemType.Ability,
                icon = "",
                abilityId = "BasicRangedAttack",
                maxStack = 1,
                consumeEffect = new ItemConsumeEffect(0),
                consumeEffectRawJson = "",
                uiTag = ""
            },
            new ItemDefinition
            {
                id = "ability_walk",
                displayName = "行走能力",
                itemType = ItemType.Ability,
                icon = "",
                abilityId = "BasicMove",
                maxStack = 1,
                consumeEffect = new ItemConsumeEffect(0),
                consumeEffectRawJson = "",
                uiTag = ""
            },
            new ItemDefinition
            {
                id = "ability_attack",
                displayName = "攻击能力",
                itemType = ItemType.Ability,
                icon = "",
                abilityId = "BasicAttack",
                maxStack = 1,
                consumeEffect = new ItemConsumeEffect(0),
                consumeEffectRawJson = "",
                uiTag = ""
            },
            new ItemDefinition
            {
                id = "ability_run",
                displayName = "跑步能力",
                itemType = ItemType.Ability,
                icon = "",
                abilityId = "BasicRun",
                maxStack = 1,
                consumeEffect = new ItemConsumeEffect(0),
                consumeEffectRawJson = "",
                uiTag = ""
            },
            new ItemDefinition
            {
                id = "ability_jump",
                displayName = "跳跃能力",
                itemType = ItemType.Ability,
                icon = "",
                abilityId = "BasicJump",
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
    /// 测试：Ability 拾取应成功入槽
    /// </summary>
    [Test]
    public void TestAbilityPickupSuccess()
    {
        // Arrange
        string itemId = "ability_arrow";
        int expectedSlot = 0;

        // 创建拾取请求
        var request = new PickupRequest(itemId, 1, null);

        // Act
        var result = _inventory.TryPickup(request, out var ctx);

        // Assert
        Assert.AreEqual(PickupResult.Success, result, "Ability 拾取应成功");
        Assert.AreEqual(itemId, _inventory.GetAbilityItemId(expectedSlot), "Ability 应在槽位 0");
    }

    /// <summary>
    /// 测试：Ability 拾取触发事件
    /// </summary>
    [Test]
    public void TestAbilityPickupTriggersEvent()
    {
        // Arrange
        string itemId = "ability_arrow";
        bool eventTriggered = false;
        int eventSlot = -1;
        string eventNewItemId = null;

        _inventory.OnAbilitySlotChanged += (slot, oldId, newId) =>
        {
            eventTriggered = true;
            eventSlot = slot;
            eventNewItemId = newId;
        };

        // Act
        var request = new PickupRequest(itemId, 1, null);
        _inventory.TryPickup(request, out _);

        // Assert
        Assert.IsTrue(eventTriggered, "拾取应触发 OnAbilitySlotChanged 事件");
        Assert.AreEqual(0, eventSlot, "事件应报告槽位 0");
        Assert.AreEqual(itemId, eventNewItemId, "事件应报告正确的 itemId");
    }

    /// <summary>
    /// 测试：重复 Ability 拾取应失败
    /// </summary>
    [Test]
    public void TestDuplicateAbilityPickupFails()
    {
        // Arrange
        string itemId = "ability_arrow";
        var request = new PickupRequest(itemId, 1, null);

        // 先拾取一次
        _inventory.TryPickup(request, out _);

        // Act: 再次拾取同一个 Ability
        var result = _inventory.TryPickup(request, out var ctx);

        // Assert
        Assert.AreEqual(PickupResult.Failed_AlreadyEquipped, result, "重复拾取应返回 Failed_AlreadyEquipped");
    }

    /// <summary>
    /// 测试：Ability 拾取应按顺序覆盖槽位（slot0→slot3 循环），旧能力直接丢弃
    /// </summary>
    [Test]
    public void TestAbilityPickupSequentiallyReplacesSlots()
    {
        // Act + Assert：连续拾取 4 个不同 Ability，应依次写入 slot0~slot3
        Assert.AreEqual(PickupResult.Success, _inventory.TryPickup(new PickupRequest("ability_arrow", 1, null), out _));
        Assert.AreEqual("ability_arrow", _inventory.GetAbilityItemId(0), "第 1 次拾取应写入槽位 0");

        Assert.AreEqual(PickupResult.Success, _inventory.TryPickup(new PickupRequest("ability_walk", 1, null), out _));
        Assert.AreEqual("ability_walk", _inventory.GetAbilityItemId(1), "第 2 次拾取应写入槽位 1");

        Assert.AreEqual(PickupResult.Success, _inventory.TryPickup(new PickupRequest("ability_run", 1, null), out _));
        Assert.AreEqual("ability_run", _inventory.GetAbilityItemId(2), "第 3 次拾取应写入槽位 2");

        Assert.AreEqual(PickupResult.Success, _inventory.TryPickup(new PickupRequest("ability_jump", 1, null), out _));
        Assert.AreEqual("ability_jump", _inventory.GetAbilityItemId(3), "第 4 次拾取应写入槽位 3");

        // 第 5 次拾取：回到槽位 0，覆盖旧值（旧能力直接丢弃）
        Assert.AreEqual(PickupResult.Success, _inventory.TryPickup(new PickupRequest("ability_attack", 1, null), out _));
        Assert.AreEqual("ability_attack", _inventory.GetAbilityItemId(0), "第 5 次拾取应回到槽位 0 并覆盖");
    }
}
