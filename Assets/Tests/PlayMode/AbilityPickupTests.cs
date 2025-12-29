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
    /// 测试：槽满时拾取应返回 RequireReplace
    /// 契约 [C-Test-1]：使用 4 个不同的 Ability 填满槽位，拾取第 5 个时返回 RequireReplace
    /// </summary>
    [Test]
    public void TestFullSlotsRequireReplace()
    {
        // Arrange: 填满 4 个槽位（使用不同的 itemId）
        _inventory.EquipAbilityItemToSlot(0, "ability_arrow");
        _inventory.EquipAbilityItemToSlot(1, "ability_walk");
        _inventory.EquipAbilityItemToSlot(2, "ability_attack");
        _inventory.EquipAbilityItemToSlot(3, "ability_run");

        // 验证：4 个槽位已填满
        Assert.AreEqual("ability_arrow", _inventory.GetAbilityItemId(0), "槽位 0 应填充");
        Assert.AreEqual("ability_walk", _inventory.GetAbilityItemId(1), "槽位 1 应填充");
        Assert.AreEqual("ability_attack", _inventory.GetAbilityItemId(2), "槽位 2 应填充");
        Assert.AreEqual("ability_run", _inventory.GetAbilityItemId(3), "槽位 3 应填充");

        // 创建第 5 个 Ability 的 pickup（模拟场景拾取物）
        var pickupObj = new GameObject("TestPickup");
        var collider = pickupObj.AddComponent<BoxCollider2D>();
        collider.isTrigger = true;
        var pickup = pickupObj.AddComponent<ItemPickup>();
        pickup.itemId = "ability_jump"; // 第 5 个不同的 Ability
        pickup.amount = 1;

        // Act: 尝试拾取第 5 个 Ability（槽满）
        var request = new PickupRequest(pickup.itemId, pickup.amount, pickup);
        var result = _inventory.TryPickup(request, out var ctx);

        // Assert: 应返回 RequireReplace
        Assert.AreEqual(PickupResult.RequireReplace, result, "槽满拾取应返回 RequireReplace");

        // 验证：PendingReplaceContext 应包含正确信息
        Assert.AreEqual("ability_jump", ctx.pendingItemId, "context.pendingItemId 应为 ability_jump");
        Assert.AreEqual(1, ctx.pendingAmount, "context.pendingAmount 应为 1");
        Assert.AreEqual(pickup, ctx.sourcePickup, "context.sourcePickup 应为 pickup");

        // 验证：槽位未变化（RequireReplace 不自动替换）
        Assert.AreEqual("ability_arrow", _inventory.GetAbilityItemId(0), "槽位 0 不应变化");
        Assert.AreEqual("ability_walk", _inventory.GetAbilityItemId(1), "槽位 1 不应变化");
        Assert.AreEqual("ability_attack", _inventory.GetAbilityItemId(2), "槽位 2 不应变化");
        Assert.AreEqual("ability_run", _inventory.GetAbilityItemId(3), "槽位 3 不应变化");

        // Cleanup
        Object.DestroyImmediate(pickupObj);
    }
}
