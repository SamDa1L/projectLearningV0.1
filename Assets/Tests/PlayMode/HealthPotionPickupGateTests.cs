using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using System.Collections;
using System.Reflection;
using CastleDB.Runtime;

public class HealthPotionPickupGateTests
{
    private GameObject _playerObj;
    private PlayerInventory _inventory;
    private Damageable _damageable;
    private PlayerContext _playerContext;
    private Collider2D _playerCollider;
    private ICastleDbService _castleDbService;
    private GameplayConfig _gameplayConfig;

    [SetUp]
    public void Setup()
    {
        _playerObj = new GameObject("TestPlayer");

        _inventory = _playerObj.AddComponent<PlayerInventory>();
        _damageable = _playerObj.AddComponent<Damageable>();

        // PlayerContext 会在 Awake 校验 ReplaceController 是否存在，这里提供一个最小依赖以避免 Error 日志。
        _playerObj.AddComponent<ReplaceController>();

        _playerCollider = _playerObj.AddComponent<BoxCollider2D>();

        _playerContext = _playerObj.AddComponent<PlayerContext>();
        _playerContext.SetAbilitySystem(new AbilitySystem());

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
    }

    [TearDown]
    public void TearDown()
    {
        if (_playerObj != null)
        {
            Object.DestroyImmediate(_playerObj);
        }
    }

    private static GameObject CreatePotionPickup(string itemId, int amount = 1)
    {
        var pickupObj = new GameObject($"Pickup_{itemId}");
        pickupObj.AddComponent<BoxCollider2D>();
        var pickup = pickupObj.AddComponent<ItemPickup>();
        pickup.itemId = itemId;
        pickup.amount = amount;
        pickup.autoPickup = true;
        return pickupObj;
    }

    private static void InvokeTryPickupInternal(ItemPickup pickup, Collider2D other)
    {
        var method = typeof(ItemPickup).GetMethod("TryPickupInternal", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(method, "TryPickupInternal method not found");
        method.Invoke(pickup, new object[] { other });
    }

    [UnityTest]
    public IEnumerator FullHealth_DoesNotPickupHealingPotion()
    {
        var pickupObj = CreatePotionPickup("potion_red");
        var pickup = pickupObj.GetComponent<ItemPickup>();

        int initialCount = _inventory.PotionCount;
        int initialHealth = _damageable.CurrentHealth;

        InvokeTryPickupInternal(pickup, _playerCollider);
        yield return null;

        Assert.AreEqual(initialCount, _inventory.PotionCount, "满血时不应拾取血瓶（计数不变）");
        Assert.AreEqual(initialHealth, _damageable.CurrentHealth, "满血时不应触发回血");
        Assert.IsFalse(pickupObj == null, "满血时拾取物不应被销毁");

        Object.Destroy(pickupObj);
        yield return null;
    }

    [UnityTest]
    public IEnumerator PartialHealth_PicksUpAndHealsActualAmount()
    {
        // 模拟掉血 5 点
        _damageable.Health = 95f;

        int healedAmount = -1;
        UnityEngine.Events.UnityAction<Damageable, int, Vector2> handler = (target, amount, _) =>
        {
            if (target == _damageable)
                healedAmount = amount;
        };

        CharacterEvents.characterHealed += handler;
        GameObject pickupObj = null;

        try
        {
            pickupObj = CreatePotionPickup("potion_red");
            var pickup = pickupObj.GetComponent<ItemPickup>();

            InvokeTryPickupInternal(pickup, _playerCollider);
            yield return null;

            Assert.AreEqual(100, _damageable.CurrentHealth, "拾取后应只回满血");
            Assert.AreEqual(1, _inventory.PotionCount, "拾取成功应累计血瓶计数");
            Assert.AreEqual(5, healedAmount, "浮动数字/事件应报告实际回血量（缺 5 → 回 5）");
            Assert.IsTrue(pickupObj == null, "非满血拾取回血成功后拾取物应被销毁");
        }
        finally
        {
            CharacterEvents.characterHealed -= handler;

            if (pickupObj != null)
            {
                Object.Destroy(pickupObj);
            }
        }

        yield return null;
    }
}
