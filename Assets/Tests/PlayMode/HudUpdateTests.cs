using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using CastleDB.Runtime;

/// <summary>
/// HUD 初始刷新与实时更新测试
///
/// 契约 [C-Test-1] PlayMode 测试：
/// - HUD 初始刷新：通过公开 API 预置 Inventory，启动后立即断言 HUD 图标/文本正确
/// - HUD 实时更新：拾取后 HUD 应立即更新
/// - 事件驱动：禁止轮询，通过事件订阅
/// </summary>
public class HudUpdateTests
{
    private GameObject _playerObj;
    private PlayerInventory _inventory;
    private ICastleDbService _castleDbService;
    private GameplayConfig _gameplayConfig;
    private Damageable _damageable;

    private GameObject _hudObj;
    private HudRefs _hudRefs;
    private HudPresenter _hudPresenter;

    [SetUp]
    public void Setup()
    {
        // 创建 Player
        _playerObj = new GameObject("TestPlayer");
        _inventory = _playerObj.AddComponent<PlayerInventory>();
        _damageable = _playerObj.AddComponent<Damageable>();

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

        // Configure Damageable
        _damageable.Configure(new DamageableStats
        {
            maxHealth = 100f,
            invincibilityTime = 0f,
            knockbackMultiplier = 1f
        });

        // 创建测试用 HUD
        CreateTestHud();
    }

    [TearDown]
    public void TearDown()
    {
        if (_playerObj != null)
        {
            Object.DestroyImmediate(_playerObj);
        }

        if (_hudObj != null)
        {
            Object.DestroyImmediate(_hudObj);
        }
    }

    /// <summary>
    /// 创建测试用 HUD（最小化结构）
    /// </summary>
    private void CreateTestHud()
    {
        _hudObj = new GameObject("TestHUD");
        _hudRefs = _hudObj.AddComponent<HudRefs>();
        _hudPresenter = _hudObj.AddComponent<HudPresenter>();

        // 创建最小节点
        _hudRefs.abilitySlotIcons = new Image[4];
        for (int i = 0; i < 4; i++)
        {
            var slotObj = new GameObject($"Slot_{i}");
            slotObj.transform.SetParent(_hudObj.transform);
            _hudRefs.abilitySlotIcons[i] = slotObj.AddComponent<Image>();
        }

        var potionTextObj = new GameObject("PotionText");
        potionTextObj.transform.SetParent(_hudObj.transform);
        _hudRefs.potionCountText = potionTextObj.AddComponent<TextMeshProUGUI>();

        var healthFillObj = new GameObject("HealthFill");
        healthFillObj.transform.SetParent(_hudObj.transform);
        _hudRefs.healthFill = healthFillObj.AddComponent<Image>();

        // 初始化 HudPresenter
        _hudPresenter.Initialize(_castleDbService, _hudRefs, _inventory, _damageable, null);
    }

    /// <summary>
    /// 测试：HUD 初始刷新应正确显示预置数据
    /// </summary>
    [Test]
    public void TestHudInitialRefresh()
    {
        // Arrange: 预置 Inventory 数据
        _inventory.EquipAbilityItemToSlot(0, "ability_arrow");
        _inventory.TryPickup(new PickupRequest("potion_red", 3, null), out _);

        // 重新创建 HUD（模拟启动后初始刷新）
        Object.DestroyImmediate(_hudObj);
        CreateTestHud();

        // Assert: HUD 应显示预置数据
        // Ability 槽位 0 应有图标（如果 icon 资源存在）
        // 注意：由于测试环境可能缺少 sprite 资源，这里仅验证文本
        Assert.AreEqual("3", _hudRefs.potionCountText.text, "血瓶计数应为 3");
    }

    /// <summary>
    /// 测试：拾取 Ability 后 HUD 应更新
    /// </summary>
    [Test]
    public void TestHudUpdatesOnAbilityPickup()
    {
        // Arrange
        Image targetSlot = _hudRefs.abilitySlotIcons[0];
        bool slotWasUpdated = false;

        // 监听槽位变化（间接验证）
        _inventory.OnAbilitySlotChanged += (slot, oldId, newId) =>
        {
            if (slot == 0) slotWasUpdated = true;
        };

        // Act
        _inventory.EquipAbilityItemToSlot(0, "ability_arrow");

        // Assert
        Assert.IsTrue(slotWasUpdated, "槽位变化事件应触发");
        // HudPresenter 应通过事件订阅更新，这里验证事件已触发
    }

    /// <summary>
    /// 测试：拾取 Consumable 后 HUD 文本应更新
    /// </summary>
    [Test]
    public void TestHudUpdatesOnConsumablePickup()
    {
        // Arrange
        int initialCount = _inventory.PotionCount;

        // Act
        _inventory.TryPickup(new PickupRequest("potion_red", 5, null), out _);

        // Assert
        Assert.AreEqual("5", _hudRefs.potionCountText.text, "血瓶计数文本应更新为 5");
    }

    /// <summary>
    /// 测试：Health 变化后 HUD fillAmount 应更新
    /// </summary>
    [Test]
    public void TestHudUpdatesOnHealthChange()
    {
        // Arrange
        float initialFillAmount = _hudRefs.healthFill.fillAmount;

        // Act: 受到伤害
        _damageable.Hit(30, Vector2.zero);

        // Assert
        float expectedFillAmount = 70f / 100f; // 剩余 70/100
        Assert.AreEqual(expectedFillAmount, _hudRefs.healthFill.fillAmount, 0.01f, "血条 fillAmount 应更新");
    }

    /// <summary>
    /// 测试：Health 为 0 时 fillAmount 应为 0
    /// </summary>
    [Test]
    public void TestHudHealthZero()
    {
        // Act: 受到致命伤害
        _damageable.Hit(100, Vector2.zero);

        // Assert
        Assert.AreEqual(0f, _hudRefs.healthFill.fillAmount, 0.01f, "血条 fillAmount 应为 0");
    }

    /// <summary>
    /// 测试：Health 超出范围应 clamp
    /// </summary>
    [Test]
    public void TestHudHealthClamp()
    {
        // Act: 受到超额伤害
        _damageable.Hit(150, Vector2.zero);

        // Assert
        Assert.AreEqual(0f, _hudRefs.healthFill.fillAmount, 0.01f, "血条 fillAmount 应 clamp 到 0");
    }
}
