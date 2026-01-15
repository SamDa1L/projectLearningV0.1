using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

/// <summary>
/// Phase 9: PlayMode coverage for InputModeSwitcher (Last Input Wins).
/// The test focuses on the scheme state + PlayerInput scheme changes (not UI pixels).
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
        yield return null; // Awake/OnEnable
        yield return null; // Start

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
        // Initial scheme should be keyboard&mouse (forced via PlayerPrefs in setup).
        Assert.AreEqual(InputModeSwitcher.ControlScheme.KeyboardMouse, _switcher.CurrentScheme);
        Assert.AreEqual(InputModeSwitcher.SchemeNameKeyboardMouse, _playerInput.currentControlScheme);

        // Use gamepad -> should switch.
        Press(_gamepad.buttonSouth);
        yield return null;

        Assert.AreEqual(InputModeSwitcher.ControlScheme.Gamepad, _switcher.CurrentScheme);
        Assert.AreEqual(InputModeSwitcher.SchemeNameGamepad, _playerInput.currentControlScheme);

        // Wait for debounce window to pass.
        yield return new WaitForSecondsRealtime(0.25f);

        // Use keyboard -> should switch back.
        Press(_keyboard.spaceKey);
        yield return null;

        Assert.AreEqual(InputModeSwitcher.ControlScheme.KeyboardMouse, _switcher.CurrentScheme);
        Assert.AreEqual(InputModeSwitcher.SchemeNameKeyboardMouse, _playerInput.currentControlScheme);
    }

    [UnityTest]
    public IEnumerator GamepadAnalogBelowThresholdDoesNotSwitch()
    {
        Assert.AreEqual(InputModeSwitcher.ControlScheme.KeyboardMouse, _switcher.CurrentScheme);

        // Small drift: below default threshold 0.2.
        Set(_gamepad.leftStick, new Vector2(0.1f, 0f));
        yield return null;

        Assert.AreEqual(InputModeSwitcher.ControlScheme.KeyboardMouse, _switcher.CurrentScheme);

        // Above threshold.
        Set(_gamepad.leftStick, new Vector2(0.5f, 0f));
        yield return null;

        Assert.AreEqual(InputModeSwitcher.ControlScheme.Gamepad, _switcher.CurrentScheme);
    }
}

