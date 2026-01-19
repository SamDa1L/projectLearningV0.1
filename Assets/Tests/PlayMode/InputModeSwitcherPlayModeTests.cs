using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

/// <summary>
/// Phase 9：InputModeSwitcher（最后输入设备优先）PlayMode 覆盖。
/// 重点断言：切换器状态 + PlayerInput 的控制方案变化（不做 UI 像素断言）。
/// </summary>
public class InputModeSwitcherPlayModeTests : InputTestFixture
{
    private GameObject _playerInstance;
    private PlayerInput _playerInput;
    private InputModeSwitcher _switcher;

    private Keyboard _keyboard;
    private Mouse _mouse;
    private Gamepad _gamepad;

    [UnitySetUp]
    public IEnumerator UnitySetup()
    {
        PlayerPrefs.DeleteKey(InputModeSwitcher.PlayerPrefsKey_LastControlScheme);
        PlayerPrefs.SetString(InputModeSwitcher.PlayerPrefsKey_LastControlScheme, InputModeSwitcher.SchemeNameKeyboardMouse);

        _keyboard = InputSystem.AddDevice<Keyboard>();
        _mouse = InputSystem.AddDevice<Mouse>();
        _gamepad = InputSystem.AddDevice<Gamepad>();

        var playerPrefab = Resources.Load<GameObject>("Prefabs/Player/Player");
        Assert.IsNotNull(playerPrefab, "Player Prefab not found at Resources/Prefabs/Player/Player");

        _playerInstance = Object.Instantiate(playerPrefab);
        yield return null; // 等待 Awake/OnEnable
        yield return null; // 等待 Start

        _playerInput = _playerInstance.GetComponent<PlayerInput>();
        Assert.IsNotNull(_playerInput, "PlayerInput component missing");

        _switcher = _playerInstance.GetComponent<InputModeSwitcher>();
        Assert.IsNotNull(_switcher, "InputModeSwitcher component missing (PlayerController should add it)");
    }

    [UnityTearDown]
    public IEnumerator UnityTearDown()
    {
        if (_playerInstance != null)
        {
            Object.Destroy(_playerInstance);
            _playerInstance = null;
        }

        yield return null;
    }

    [UnityTest]
    public IEnumerator SwitchesBetweenKeyboardMouseAndGamepad()
    {
        // 初始方案应为键鼠（由 Setup 中的 PlayerPrefs 强制指定）。
        Assert.AreEqual(InputModeSwitcher.ControlScheme.KeyboardMouse, _switcher.CurrentScheme);
        Assert.AreEqual(InputModeSwitcher.SchemeNameKeyboardMouse, _playerInput.currentControlScheme);

        // 使用手柄输入 -> 应切到手柄方案。
        Press(_gamepad.buttonSouth);
        yield return null;

        Assert.AreEqual(InputModeSwitcher.ControlScheme.Gamepad, _switcher.CurrentScheme);
        Assert.AreEqual(InputModeSwitcher.SchemeNameGamepad, _playerInput.currentControlScheme);

        // 等待防抖窗口过去。
        yield return new WaitForSecondsRealtime(0.25f);

        // 使用键盘输入 -> 应切回键鼠方案。
        Press(_keyboard.spaceKey);
        yield return null;

        Assert.AreEqual(InputModeSwitcher.ControlScheme.KeyboardMouse, _switcher.CurrentScheme);
        Assert.AreEqual(InputModeSwitcher.SchemeNameKeyboardMouse, _playerInput.currentControlScheme);
    }

    [UnityTest]
    public IEnumerator GamepadAnalogBelowThresholdDoesNotSwitch()
    {
        Assert.AreEqual(InputModeSwitcher.ControlScheme.KeyboardMouse, _switcher.CurrentScheme);

        // 轻微漂移：低于默认阈值 0.2，不应触发切换。
        Set(_gamepad.leftStick, new Vector2(0.1f, 0f));
        yield return null;

        Assert.AreEqual(InputModeSwitcher.ControlScheme.KeyboardMouse, _switcher.CurrentScheme);

        // 明显输入：高于阈值，应触发切换。
        Set(_gamepad.leftStick, new Vector2(0.5f, 0f));
        yield return null;

        Assert.AreEqual(InputModeSwitcher.ControlScheme.Gamepad, _switcher.CurrentScheme);
    }
}
