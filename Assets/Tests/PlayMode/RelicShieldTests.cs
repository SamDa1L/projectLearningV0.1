using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using CastleDB.Runtime;

/// <summary>
/// Phase 7：遗物（护盾）最小闭环测试
/// - 拾取遗物后自动装备
/// - 受击优先扣护盾，护盾耗尽后才扣血
/// - 冷却结束后护盾重生
/// - HUD 左上角遗物图标随装备/清理变化
/// </summary>
public class RelicShieldTests
{
    private GameObject _playerObj;
    private Damageable _damageable;
    private PlayerRelicController _relicCtrl;

    private CastleDbService _items;
    private ItemCatalog _itemCatalog;
    private RelicCatalog _relicCatalog;

    private GameObject _hudObj;
    private HudRefs _hudRefs;
    private HudPresenter _hudPresenter;

    private const string ShieldRelicId = "relic_shield_test";
    private const string ShieldRelicItemId = "relic_shield_test_item";
    private const string ShieldRelicIconPath = "Icons/Items/Potions/Icons/icon4";

    [SetUp]
    public void Setup()
    {
        // 玩家（最小组件）
        _playerObj = new GameObject("TestPlayer");
        _damageable = _playerObj.AddComponent<Damageable>();

        // ===== 构造最小 ItemCatalog（避免依赖 Import 产物）=====
        _itemCatalog = ScriptableObject.CreateInstance<ItemCatalog>();
        _itemCatalog.ApplyFromCastleDb(new List<ItemDefinition>
        {
            new ItemDefinition
            {
                id = ShieldRelicItemId,
                displayName = "Shield Relic Test Item",
                itemType = ItemType.Relic,
                icon = ShieldRelicIconPath,
                abilityId = "",
                relicId = ShieldRelicId,
                maxStack = 1,
                consumeEffect = new ItemConsumeEffect(0),
                consumeEffectRawJson = "",
                uiTag = ""
            }
        });

        _items = new CastleDbService();
        _items.SetItemCatalog(_itemCatalog);

        // ===== 构造最小 RelicCatalog =====
        _relicCatalog = ScriptableObject.CreateInstance<RelicCatalog>();
        _relicCatalog.ApplyFromCastleDb(new List<RelicDefinition>
        {
            new RelicDefinition
            {
                id = ShieldRelicId,
                kind = RelicKind.Shield,
                // 使用较短冷却，便于 PlayMode 测试验证重生
                paramsJson = "{\"shieldMaxHp\":10,\"regenCooldown\":0.1,\"regenDelay\":0}"
            }
        });

        // 遗物控制器必须与 Damageable 同一个 GameObject（Damageable.Hit 通过 GetComponents<IDamageInterceptor> 查找）
        _relicCtrl = _damageable.gameObject.AddComponent<PlayerRelicController>();
        _relicCtrl.Initialize(_items, _relicCatalog, _damageable);

        // 配置 Damageable（禁用受击无敌，便于断言）
        _damageable.Configure(new DamageableStats
        {
            maxHealth = 100f,
            invincibilityTime = 0f,
            knockbackMultiplier = 1f
        });

        // ===== 构造最小 HUD（只覆盖 HudPresenter 需要的字段）=====
        _hudObj = new GameObject("TestHUD");
        _hudRefs = _hudObj.AddComponent<HudRefs>();
        _hudPresenter = _hudObj.AddComponent<HudPresenter>();

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

        var relicIconObj = new GameObject("RelicIcon");
        relicIconObj.transform.SetParent(_hudObj.transform);
        _hudRefs.relicIcon = relicIconObj.AddComponent<Image>();
        _hudRefs.relicIcon.enabled = false;

        // HudPresenter 依赖 Inventory，但此测试只关注遗物图标，不需要初始化 Inventory（GetAbilityItemId 默认全空即可）
        var inv = _playerObj.AddComponent<PlayerInventory>();
        _hudPresenter.Initialize(_items, _hudRefs, inv, _damageable, _relicCtrl);
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

        if (_itemCatalog != null)
        {
            Object.DestroyImmediate(_itemCatalog);
        }

        if (_relicCatalog != null)
        {
            Object.DestroyImmediate(_relicCatalog);
        }
    }

    [UnityTest]
    public IEnumerator ShieldRelic_AbsorbsDamage_Breaks_AndRegenerates()
    {
        int damagedEventCount = 0;
        int lastDamageAmount = -1;

        UnityEngine.Events.UnityAction<Damageable, int, Vector2> handler = (target, amount, _) =>
        {
            if (target == _damageable)
            {
                damagedEventCount++;
                lastDamageAmount = amount;
            }
        };

        CharacterEvents.characterDamaged += handler;

        try
        {
            // 1) 拾取并装备遗物
            var pickupResult = _relicCtrl.TryPickupRelic(new PickupRequest(ShieldRelicItemId, 1, null));
            Assert.AreEqual(PickupResult.Success, pickupResult, "拾取遗物应成功");
            Assert.AreEqual(ShieldRelicItemId, _relicCtrl.EquippedRelicItemId, "装备的遗物 itemId 应一致");

            // HUD 图标应显示（Sprite 必须存在）
            yield return null;
            Assert.IsTrue(_hudRefs.relicIcon.enabled, "拾取遗物后 HUD 图标应显示");
            Assert.IsNotNull(_hudRefs.relicIcon.sprite, "拾取遗物后 HUD 图标 sprite 不应为空");

            // 2) 先被打 6：护盾吸收，血量不变
            _damageable.Hit(6, Vector2.zero, Vector2.zero);
            yield return null;

            Assert.AreEqual(4, _relicCtrl.ShieldHp, "护盾应优先扣除（10→4）");
            Assert.AreEqual(100, _damageable.CurrentHealth, "护盾吸收伤害时血量不应下降");
            Assert.AreEqual(0, damagedEventCount, "护盾完全吸收时不应触发 characterDamaged 事件");

            // 3) 再被打 10：先耗尽护盾 4，再扣血 6
            _damageable.Hit(10, Vector2.zero, Vector2.zero);
            yield return null;

            Assert.AreEqual(0, _relicCtrl.ShieldHp, "护盾耗尽后应为 0");
            Assert.AreEqual(94, _damageable.CurrentHealth, "剩余伤害应扣到血量（100→94）");
            Assert.AreEqual(1, damagedEventCount, "只在真实扣血时触发 characterDamaged 事件");
            Assert.AreEqual(6, lastDamageAmount, "事件只上报真实扣血量（剩余 6）");

            // 4) 等待冷却，护盾重生
            yield return new WaitForSeconds(0.12f);
            yield return null; // 等一帧让状态机 Update 处理

            Assert.AreEqual(10, _relicCtrl.ShieldHp, "冷却结束后护盾应重生（恢复到 10）");

            // 5) 清理遗物：HUD 图标应隐藏
            _relicCtrl.ClearEquippedRelic();
            yield return null;

            Assert.IsFalse(_hudRefs.relicIcon.enabled, "清理遗物后 HUD 图标应隐藏");
        }
        finally
        {
            CharacterEvents.characterDamaged -= handler;
        }
    }
}

