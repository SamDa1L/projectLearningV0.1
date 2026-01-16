using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.DualShock;
using UnityEngine.InputSystem.Switch;
using UnityEngine.InputSystem.XInput;
using UnityEngine.UI;

public sealed class HudSlotKeyIconPresenter : MonoBehaviour
{
    private const int SlotCount = 4;

    private HudRefs _refs;
    private PlayerInput _playerInput;
    private InputModeSwitcher _switcher;
    private InputIconCatalog _catalog;
    private bool _initialized;
    private bool _listening;

    public void Initialize(HudRefs refs, PlayerInput playerInput, InputModeSwitcher switcher, InputIconCatalog catalog)
    {
        if (_initialized)
        {
            return;
        }

        _refs = refs;
        _playerInput = playerInput;
        _switcher = switcher;
        _catalog = catalog;
        _initialized = true;

        BindEvents();
        RefreshIcons();
    }

    private void OnEnable()
    {
        if (_initialized)
        {
            BindEvents();
            RefreshIcons();
        }
    }

    private void OnDisable()
    {
        UnbindEvents();
    }

    private void BindEvents()
    {
        if (_listening)
        {
            return;
        }

        if (_switcher != null)
        {
            _switcher.OnControlSchemeChanged += OnControlSchemeChanged;
        }

        InputSystem.onDeviceChange += OnDeviceChange;
        _listening = true;
    }

    private void UnbindEvents()
    {
        if (!_listening)
        {
            return;
        }

        if (_switcher != null)
        {
            _switcher.OnControlSchemeChanged -= OnControlSchemeChanged;
        }

        InputSystem.onDeviceChange -= OnDeviceChange;
        _listening = false;
    }

    private void OnControlSchemeChanged(InputModeSwitcher.ControlScheme oldScheme, InputModeSwitcher.ControlScheme newScheme)
    {
        RefreshIcons();
    }

    private void OnDeviceChange(InputDevice device, InputDeviceChange change)
    {
        if (!_initialized || _playerInput == null)
        {
            return;
        }

        if (!IsGamepadScheme())
        {
            return;
        }

        if (device is Gamepad)
        {
            RefreshIcons();
        }
    }

    private bool IsGamepadScheme()
    {
        if (_playerInput == null)
        {
            return false;
        }

        string scheme = _playerInput.currentControlScheme;
        if (string.IsNullOrWhiteSpace(scheme))
        {
            return false;
        }

        return string.Equals(scheme, InputModeSwitcher.SchemeNameGamepad, StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshIcons()
    {
        if (!_initialized)
        {
            return;
        }

        if (_refs == null)
        {
            Debug.LogWarning("[HudSlotKeyIconPresenter] 缺少 HudRefs，无法刷新按键图标");
            return;
        }

        if (_catalog == null)
        {
            Debug.LogWarning("[HudSlotKeyIconPresenter] 缺少 InputIconCatalog，无法刷新按键图标");
            return;
        }

        Image[] images = _refs.abilitySlotKeyIcons;
        if (images == null || images.Length != SlotCount)
        {
            Debug.LogWarning("[HudSlotKeyIconPresenter] abilitySlotKeyIcons 未配置或长度不为 4");
            return;
        }

        InputIconDevice device = ResolveDeviceType();
        for (int i = 0; i < SlotCount; i++)
        {
            Image image = images[i];
            if (image == null)
            {
                continue;
            }

            Sprite sprite = _catalog.GetSprite(device, i);
            image.sprite = sprite;
            image.enabled = sprite != null;
        }
    }

    private InputIconDevice ResolveDeviceType()
    {
        if (_switcher != null && _switcher.CurrentScheme != InputModeSwitcher.ControlScheme.Unknown)
        {
            if (_switcher.CurrentScheme == InputModeSwitcher.ControlScheme.KeyboardMouse)
            {
                return InputIconDevice.Keyboard;
            }

            if (_switcher.CurrentScheme == InputModeSwitcher.ControlScheme.Gamepad)
            {
                return ResolveGamepadType();
            }
        }

        if (_playerInput == null)
        {
            return InputIconDevice.Keyboard;
        }

        string scheme = _playerInput.currentControlScheme;
        if (string.Equals(scheme, InputModeSwitcher.SchemeNameKeyboardMouse, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(scheme, "KeyboardMouse", StringComparison.OrdinalIgnoreCase))
        {
            return InputIconDevice.Keyboard;
        }

        if (string.Equals(scheme, InputModeSwitcher.SchemeNameGamepad, StringComparison.OrdinalIgnoreCase))
        {
            return ResolveGamepadType();
        }

        return InputIconDevice.Keyboard;
    }

    private InputIconDevice ResolveGamepadType()
    {
        Gamepad gamepad = GetPlayerGamepad() ?? Gamepad.current;
        if (gamepad == null)
        {
            return InputIconDevice.Xbox;
        }

        if (gamepad is DualShockGamepad)
        {
            return InputIconDevice.PlayStation;
        }

        if (gamepad is SwitchProControllerHID)
        {
            return InputIconDevice.Switch;
        }

        if (gamepad is XInputController)
        {
            return InputIconDevice.Xbox;
        }

        return InputIconDevice.Xbox;
    }

    private Gamepad GetPlayerGamepad()
    {
        if (_playerInput == null)
        {
            return null;
        }

        var devices = _playerInput.devices;
        for (int i = 0; i < devices.Count; i++)
        {
            if (devices[i] is Gamepad gamepad)
            {
                return gamepad;
            }
        }

        return null;
    }
}
