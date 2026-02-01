using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.DualShock;
using UnityEngine.TestTools;
using UnityEngine.UI;

/// <summary>
/// Phase 9：验证 HUD 四个槽位按键图标会跟随 InputModeSwitcher 的方案切换刷新。
/// </summary>
public class HudSlotKeyIconPresenterPlayModeTests : InputTestFixture
{
    private const string PlayerPrefabPath = "Prefabs/Player/Player";

    private GameObject _playerInstance;
    private PlayerInput _playerInput;
    private InputModeSwitcher _switcher;

    private GameObject _hudRoot;
    private HudRefs _refs;
    private HudSlotKeyIconPresenter _presenter;
    private InputIconCatalog _catalog;

    private Keyboard _keyboard;
    private Mouse _mouse;
    private DualShockGamepad _gamepad;

    private IEnumerator Init()
    {
        PlayerPrefs.DeleteKey(InputModeSwitcher.PlayerPrefsKey_LastControlScheme);
        PlayerPrefs.SetString(InputModeSwitcher.PlayerPrefsKey_LastControlScheme, InputModeSwitcher.SchemeNameKeyboardMouse);

        _keyboard = InputSystem.AddDevice<Keyboard>();
        _mouse = InputSystem.AddDevice<Mouse>();
        _gamepad = InputSystem.AddDevice<DualShockGamepad>();

        var playerPrefab = Resources.Load<GameObject>(PlayerPrefabPath);
        Assert.IsNotNull(playerPrefab, $"Player Prefab not found at Resources/{PlayerPrefabPath}.prefab");

        _playerInstance = Object.Instantiate(playerPrefab);
        yield return null; // 等待 Awake/OnEnable
        yield return null; // 等待 Start

        _playerInput = _playerInstance.GetComponent<PlayerInput>();
        _switcher = _playerInstance.GetComponent<InputModeSwitcher>();
        Assert.IsNotNull(_playerInput, "PlayerInput component missing");
        Assert.IsNotNull(_switcher, "InputModeSwitcher component missing");
        Assert.IsTrue(_playerInput.user.valid, "PlayerInput.user 无效（常见原因：InputTestFixture.Reset 在 PlayerInput 初始化之后发生）");

        _catalog = Resources.Load<InputIconCatalog>("Config/InputIconCatalog");
        Assert.IsNotNull(_catalog, "InputIconCatalog not found at Resources/Config/InputIconCatalog.asset");

        BuildTestHud();
        _presenter.Initialize(_refs, _playerInput, _switcher, _catalog);
        yield return null;
    }

    [UnityTearDown]
    public IEnumerator UnityTearDown()
    {
        if (_playerInstance != null)
        {
            Object.Destroy(_playerInstance);
            _playerInstance = null;
        }

        if (_hudRoot != null)
        {
            Object.Destroy(_hudRoot);
            _hudRoot = null;
        }

        yield return null;
    }

    [UnityTest]
    public IEnumerator IconsFollowControlScheme()
    {
        yield return Init();

        // 初始方案通过 Setup 中的 PlayerPrefs 强制为键鼠。
        Assert.AreEqual(InputModeSwitcher.ControlScheme.KeyboardMouse, _switcher.CurrentScheme);
        AssertAreIcons(InputIconDevice.Keyboard);

        // 使用手柄输入 -> 应切到手柄方案，并刷新为 PlayStation 图标集（DualShock）。
        Press(_gamepad.buttonSouth);
        yield return null;

        Assert.AreEqual(InputModeSwitcher.ControlScheme.Gamepad, _switcher.CurrentScheme);
        AssertAreIcons(InputIconDevice.PlayStation);

        // 等待防抖窗口过去。
        yield return new WaitForSecondsRealtime(0.25f);

        // 使用键盘输入 -> 应切回键鼠方案，并刷新为键鼠图标集。
        Press(_keyboard.spaceKey);
        yield return null;

        Assert.AreEqual(InputModeSwitcher.ControlScheme.KeyboardMouse, _switcher.CurrentScheme);
        AssertAreIcons(InputIconDevice.Keyboard);
    }

    private void BuildTestHud()
    {
        _hudRoot = new GameObject("TestHudRoot");
        _refs = _hudRoot.AddComponent<HudRefs>();
        _presenter = _hudRoot.AddComponent<HudSlotKeyIconPresenter>();

        _refs.abilitySlotKeyIcons = new Image[4];
        for (int i = 0; i < _refs.abilitySlotKeyIcons.Length; i++)
        {
            var iconGo = new GameObject($"KeyIcon_{i}", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(_hudRoot.transform, worldPositionStays: false);
            _refs.abilitySlotKeyIcons[i] = iconGo.GetComponent<Image>();
        }
    }

    private void AssertAreIcons(InputIconDevice device)
    {
        Assert.IsNotNull(_refs);
        Assert.IsNotNull(_refs.abilitySlotKeyIcons);
        Assert.AreEqual(4, _refs.abilitySlotKeyIcons.Length);

        for (int i = 0; i < 4; i++)
        {
            Sprite expected = _catalog.GetSprite(device, i);
            Image image = _refs.abilitySlotKeyIcons[i];
            Assert.IsNotNull(image, $"abilitySlotKeyIcons[{i}] is null");

            Assert.AreSame(expected, image.sprite, $"Slot {i} sprite mismatch for device={device}");
            Assert.AreEqual(expected != null, image.enabled, $"Slot {i} enabled mismatch for device={device}");
        }
    }
}
