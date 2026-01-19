using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CastleDB.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

/// <summary>
/// Phase 9：4 槽固定键位映射回归（键鼠 + 手柄）。
/// 断言“按键触发哪个槽位（Inventory slotIndex）”，不依赖动画时序。
/// </summary>
public class AbilitySlotInputMappingPlayModeTests : InputTestFixture
{
    private const string PlayerPrefabPath = "Prefabs/Player/Player";

    private GameObject _playerInstance;
    private PlayerInput _playerInput;
    private PlayerController _playerController;
    private PlayerInventory _inventory;

    private Keyboard _keyboard;
    private Mouse _mouse;
    private Gamepad _gamepad;

    private AbilitySystem _abilitySystem;
    private CountingAbility _slot0;
    private CountingAbility _slot1;
    private CountingAbility _slot2;
    private CountingAbility _slot3;

    private ScriptableObject _itemCatalog;

    [UnitySetUp]
    public IEnumerator UnitySetUp()
    {
        _keyboard = InputSystem.AddDevice<Keyboard>();
        _mouse = InputSystem.AddDevice<Mouse>();
        _gamepad = InputSystem.AddDevice<Gamepad>();

        var playerPrefab = Resources.Load<GameObject>(PlayerPrefabPath);
        Assert.IsNotNull(playerPrefab, $"未找到 Player 预制体：Resources/{PlayerPrefabPath}.prefab");

        _playerInstance = Object.Instantiate(playerPrefab);
        yield return null; // 等待 Awake/OnEnable
        yield return null; // 等待 Start

        _playerInput = _playerInstance.GetComponent<PlayerInput>();
        _playerController = _playerInstance.GetComponent<PlayerController>();
        _inventory = _playerInstance.GetComponent<PlayerInventory>();

        Assert.IsNotNull(_playerInput, "缺少 PlayerInput 组件");
        Assert.IsNotNull(_playerController, "缺少 PlayerController 组件");
        Assert.IsNotNull(_inventory, "缺少 PlayerInventory 组件");

        SetupInventoryAndAbilitySystem();
    }

    [UnityTearDown]
    public IEnumerator UnityTearDown()
    {
        if (_playerInstance != null)
        {
            Object.Destroy(_playerInstance);
            _playerInstance = null;
        }

        if (_itemCatalog != null)
        {
            Object.Destroy(_itemCatalog);
            _itemCatalog = null;
        }

        yield return null;
    }

    [UnityTest]
    public IEnumerator KeyboardMouse_TriggersSlots1To4()
    {
        _playerInput.SwitchCurrentControlScheme(InputModeSwitcher.SchemeNameKeyboardMouse, _keyboard, _mouse);
        yield return null;

        Press(_mouse.leftButton);
        yield return null;
        Release(_mouse.leftButton);
        yield return null;

        Assert.AreEqual(1, _slot0.AttackCalls, "鼠标左键应触发 slot0 的能力（Attack）");
        Assert.AreEqual(0, _slot1.AttackCalls);
        Assert.AreEqual(0, _slot2.AttackCalls);
        Assert.AreEqual(0, _slot3.AttackCalls);

        Press(_mouse.rightButton);
        yield return null;
        Release(_mouse.rightButton);
        yield return null;

        Assert.AreEqual(1, _slot1.AttackCalls, "鼠标右键应触发 slot1 的能力（Ability2）");

        Press(_keyboard.fKey);
        yield return null;
        Release(_keyboard.fKey);
        yield return null;

        Assert.AreEqual(1, _slot2.AttackCalls, "F 键应触发 slot2 的能力（Ability3）");

        Press(_keyboard.rKey);
        yield return null;
        Release(_keyboard.rKey);
        yield return null;

        Assert.AreEqual(1, _slot3.AttackCalls, "R 键应触发 slot3 的能力（Ability4）");
    }

    [UnityTest]
    public IEnumerator KeyboardMouse_Slot1Empty_RightButton_DoesNotFallbackToRangedHook()
    {
        _playerInput.SwitchCurrentControlScheme(InputModeSwitcher.SchemeNameKeyboardMouse, _keyboard, _mouse);
        yield return null;

        // 常见场景：slot1 为空，但当前存在可用的 RangedAttack 能力（例如第一次拾取把火球放到了 slot0）。
        // 期望：右键（Ability2）不应回退到 RangedAttack hook。
        _abilitySystem.ClearAll();
        _abilitySystem.RegisterAbility(AbilityHookType.RangedAttack, _slot0);

        _inventory.ClearAbilitySlot(1);

        Press(_mouse.rightButton);
        yield return null;
        Release(_mouse.rightButton);
        yield return null;

        Assert.AreEqual(0, _slot0.RangedCalls, "slot1 为空时，右键不应触发 RangedAttack 回退");
        Assert.AreEqual(0, _slot0.AttackCalls, "slot1 为空时，右键不应触发 Attack");
    }

    [UnityTest]
    public IEnumerator Gamepad_TriggersSlots1To4()
    {
        _playerInput.SwitchCurrentControlScheme(InputModeSwitcher.SchemeNameGamepad, _gamepad);
        yield return null;

        Press(_gamepad.buttonWest);
        yield return null;
        Release(_gamepad.buttonWest);
        yield return null;

        Assert.AreEqual(1, _slot0.AttackCalls, "手柄 buttonWest（PS 方块键）应触发 slot0 的能力（Attack）");

        Press(_gamepad.buttonEast);
        yield return null;
        Release(_gamepad.buttonEast);
        yield return null;

        Assert.AreEqual(1, _slot1.AttackCalls, "手柄 buttonEast（PS 圆圈键）应触发 slot1 的能力（Ability2）");

        Press(_gamepad.leftShoulder);
        yield return null;
        Release(_gamepad.leftShoulder);
        yield return null;

        Assert.AreEqual(1, _slot2.AttackCalls, "手柄 L1 应触发 slot2 的能力（Ability3）");

        // L2 是轴控件：拉满再归零即可触发一次。
        Set(_gamepad.leftTrigger, 1f);
        yield return null;
        Set(_gamepad.leftTrigger, 0f);
        yield return null;

        Assert.AreEqual(1, _slot3.AttackCalls, "手柄 L2 应触发 slot3 的能力（Ability4）");
    }

    private void SetupInventoryAndAbilitySystem()
    {
        _abilitySystem = new AbilitySystem();
        _slot0 = new CountingAbility("slot0_ability");
        _slot1 = new CountingAbility("slot1_ability");
        _slot2 = new CountingAbility("slot2_ability");
        _slot3 = new CountingAbility("slot3_ability");

        _abilitySystem.RegisterAbility(AbilityHookType.Attack, _slot0);
        _abilitySystem.RegisterAbility(AbilityHookType.Attack, _slot1);
        _abilitySystem.RegisterAbility(AbilityHookType.Attack, _slot2);
        _abilitySystem.RegisterAbility(AbilityHookType.Attack, _slot3);

        InjectPrivateField(_playerController, "abilitySystem", _abilitySystem);
        InjectPrivateField(_playerController, "usePlayerConfigFromCastleDb", true);

        // Inventory 存的是 itemId，不是 abilityId；itemId 需要通过 ItemCatalog 映射到 abilityId。
        var itemCatalog = ScriptableObject.CreateInstance<ItemCatalog>();
        itemCatalog.ApplyFromCastleDb(new List<ItemDefinition>
        {
            new ItemDefinition
            {
                id = "ability_attack",
                displayName = "Attack",
                itemType = ItemType.Ability,
                icon = "",
                abilityId = _slot0.AbilityId,
                maxStack = 1,
                consumeEffect = new ItemConsumeEffect(0),
                consumeEffectRawJson = "",
                uiTag = ""
            },
            new ItemDefinition
            {
                id = "test_slot1_item",
                displayName = "Slot1",
                itemType = ItemType.Ability,
                icon = "",
                abilityId = _slot1.AbilityId,
                maxStack = 1,
                consumeEffect = new ItemConsumeEffect(0),
                consumeEffectRawJson = "",
                uiTag = ""
            },
            new ItemDefinition
            {
                id = "test_slot2_item",
                displayName = "Slot2",
                itemType = ItemType.Ability,
                icon = "",
                abilityId = _slot2.AbilityId,
                maxStack = 1,
                consumeEffect = new ItemConsumeEffect(0),
                consumeEffectRawJson = "",
                uiTag = ""
            },
            new ItemDefinition
            {
                id = "test_slot3_item",
                displayName = "Slot3",
                itemType = ItemType.Ability,
                icon = "",
                abilityId = _slot3.AbilityId,
                maxStack = 1,
                consumeEffect = new ItemConsumeEffect(0),
                consumeEffectRawJson = "",
                uiTag = ""
            }
        });

        _itemCatalog = itemCatalog;

        var itemsService = new CastleDbService();
        itemsService.SetItemCatalog(itemCatalog);

        _inventory.Initialize(itemsService, cfg: null);

        Assert.IsTrue(_inventory.EquipAbilityItemToSlot(1, "test_slot1_item"));
        Assert.IsTrue(_inventory.EquipAbilityItemToSlot(2, "test_slot2_item"));
        Assert.IsTrue(_inventory.EquipAbilityItemToSlot(3, "test_slot3_item"));
    }

    private static void InjectPrivateField<TTarget>(TTarget target, string fieldName, object value)
    {
        Assert.IsNotNull(target, $"InjectPrivateField target is null for '{fieldName}'");

        FieldInfo field = typeof(TTarget).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, $"Field '{fieldName}' not found on {typeof(TTarget).Name}");
        field.SetValue(target, value);
    }

    private sealed class CountingAbility : IPlayerAbility
    {
        public string AbilityId { get; }
        public int Priority { get; } = 0;
        public bool Enabled { get; set; } = true;

        public float CooldownSeconds => 0f;
        public float CooldownRemaining => 0f;

        public int AttackCalls { get; private set; }
        public int RangedCalls { get; private set; }

        public CountingAbility(string abilityId)
        {
            AbilityId = abilityId;
        }

        public bool OnAttack(AbilityInput input)
        {
            AttackCalls++;
            return true;
        }

        public bool OnMove(AbilityInput input) => false;
        public bool OnRun(AbilityInput input) => false;
        public bool OnJump(AbilityInput input) => false;
        public bool OnRangedAttack(AbilityInput input)
        {
            RangedCalls++;
            return true;
        }
    }
}
