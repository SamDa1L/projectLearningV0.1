using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Users;

/// <summary>
/// Phase 9："最后输入设备优先（Last Input Wins）"控制方案切换器。
/// - 监听未配对设备的输入，并切换 PlayerInput 控制方案。
/// - 通过阈值过滤手柄摇杆/扳机漂移，并做切换防抖。
/// - 将最后一次控制方案写入 PlayerPrefs（key="lastControlScheme"）。
/// </summary>
[DisallowMultipleComponent]
public sealed class InputModeSwitcher : MonoBehaviour
{
    public enum ControlScheme
    {
        Unknown = 0,
        KeyboardMouse = 1,
        Gamepad = 2,
    }

    public const string PlayerPrefsKey_LastControlScheme = "lastControlScheme";
    public const string SchemeNameKeyboardMouse = "Keyboard&Mouse";
    public const string SchemeNameGamepad = "Gamepad";

    [Header("Filtering")]
    [Tooltip("Ignore gamepad stick/trigger input below this threshold (prevents drift/noise from switching schemes).")]
    [Range(0f, 1f)]
    [SerializeField]
    private float gamepadAnalogThreshold = 0.2f;

    [Tooltip("Minimum time between two scheme switches (prevents rapid flip-flopping).")]
    [Min(0f)]
    [SerializeField]
    private float switchDebounceSeconds = 0.2f;

    [Header("Debug")]
    [SerializeField] private bool debugLog = false;

    public ControlScheme CurrentScheme { get; private set; } = ControlScheme.Unknown;

    public event Action<ControlScheme, ControlScheme> OnControlSchemeChanged;

    private PlayerInput _playerInput;
    private bool _listening;
    private float _lastSwitchTime = -999f;

    private void Awake()
    {
        _playerInput = GetComponent<PlayerInput>();
        if (_playerInput == null)
        {
            Debug.LogWarning("[InputModeSwitcher] Missing PlayerInput; disabling.", this);
            enabled = false;
            return;
        }

        // 关闭 InputSystem 内置的自动切换，本组件实现带过滤的版本。
        _playerInput.neverAutoSwitchControlSchemes = true;
    }

    private void OnEnable()
    {
        if (_listening)
        {
            return;
        }

        InputUser.listenForUnpairedDeviceActivity++;
        InputUser.onUnpairedDeviceUsed += OnUnpairedDeviceUsed;
        _listening = true;
    }

    private void Start()
    {
        if (_playerInput == null)
        {
            return;
        }

        ApplyInitialScheme();
    }

    private void OnDisable()
    {
        if (!_listening)
        {
            return;
        }

        InputUser.onUnpairedDeviceUsed -= OnUnpairedDeviceUsed;
        if (InputUser.listenForUnpairedDeviceActivity > 0)
        {
            InputUser.listenForUnpairedDeviceActivity--;
        }

        _listening = false;
    }

    private void ApplyInitialScheme()
    {
        // 优先级：PlayerPrefs -> PlayerInput 当前方案 -> 默认键鼠。
        ControlScheme desired = ParseScheme(PlayerPrefs.GetString(PlayerPrefsKey_LastControlScheme, string.Empty));
        if (desired == ControlScheme.Unknown)
        {
            desired = ParseScheme(_playerInput.currentControlScheme);
        }

        if (desired == ControlScheme.Unknown)
        {
            desired = ControlScheme.KeyboardMouse;
        }

        TrySetScheme(desired, triggeringDevice: null, savePrefs: false);
    }

    private void OnUnpairedDeviceUsed(InputControl control, InputEventPtr eventPtr)
    {
        if (!isActiveAndEnabled || _playerInput == null || control == null)
        {
            return;
        }

        InputDevice device = control.device;
        if (device == null)
        {
            return;
        }

        ControlScheme desired = ClassifyDevice(device);
        if (desired == ControlScheme.Unknown)
        {
            return;
        }

        if (desired == ControlScheme.Gamepad && !IsMeaningfulGamepadInput(control))
        {
            return;
        }

        float now = Time.unscaledTime;
        if (switchDebounceSeconds > 0f && now - _lastSwitchTime < switchDebounceSeconds)
        {
            return;
        }

        if (desired == CurrentScheme)
        {
            return;
        }

        if (TrySetScheme(desired, triggeringDevice: device, savePrefs: true))
        {
            _lastSwitchTime = now;
        }
    }

    private bool TrySetScheme(ControlScheme scheme, InputDevice triggeringDevice, bool savePrefs)
    {
        if (_playerInput == null)
        {
            return false;
        }

        string schemeName = scheme == ControlScheme.Gamepad ? SchemeNameGamepad : SchemeNameKeyboardMouse;
        List<InputDevice> devices = new List<InputDevice>(2);

        if (scheme == ControlScheme.Gamepad)
        {
            Gamepad gamepad = triggeringDevice as Gamepad ?? Gamepad.current;
            if (gamepad == null)
            {
                if (debugLog)
                {
                    Debug.Log("[InputModeSwitcher] No Gamepad available; ignoring scheme switch.", this);
                }
                return false;
            }

            devices.Add(gamepad);
        }
        else if (scheme == ControlScheme.KeyboardMouse)
        {
            if (Keyboard.current != null)
            {
                devices.Add(Keyboard.current);
            }

            if (Mouse.current != null)
            {
                devices.Add(Mouse.current);
            }

            if (devices.Count == 0)
            {
                if (debugLog)
                {
                    Debug.Log("[InputModeSwitcher] No Keyboard/Mouse available; ignoring scheme switch.", this);
                }
                return false;
            }
        }

        ControlScheme old = CurrentScheme;
        try
        {
            _playerInput.SwitchCurrentControlScheme(schemeName, devices.ToArray());
        }
        catch (Exception ex)
        {
            Debug.LogError($"[InputModeSwitcher] SwitchCurrentControlScheme failed: {ex.Message}", this);
            return false;
        }

        CurrentScheme = scheme;

        if (savePrefs)
        {
            PlayerPrefs.SetString(PlayerPrefsKey_LastControlScheme, schemeName);
            PlayerPrefs.Save();
        }

        if (debugLog)
        {
            Debug.Log(
                $"[InputModeSwitcher] Control scheme: {old} -> {CurrentScheme} (PlayerInput.currentControlScheme='{_playerInput.currentControlScheme}')",
                this);
        }

        OnControlSchemeChanged?.Invoke(old, CurrentScheme);
        return true;
    }

    private ControlScheme ClassifyDevice(InputDevice device)
    {
        if (device is Gamepad)
        {
            return ControlScheme.Gamepad;
        }

        if (device is Keyboard || device is Mouse)
        {
            return ControlScheme.KeyboardMouse;
        }

        return ControlScheme.Unknown;
    }

    private bool IsMeaningfulGamepadInput(InputControl control)
    {
        if (control is ButtonControl button)
        {
            return button.isPressed;
        }

        float threshold = Mathf.Max(0f, gamepadAnalogThreshold);

        if (control is StickControl stick)
        {
            Vector2 v = stick.ReadValue();
            return v.sqrMagnitude >= threshold * threshold;
        }

        if (control is AxisControl axis)
        {
            float v = axis.ReadValue();
            return Mathf.Abs(v) >= threshold;
        }

        // 兜底：处理 Vector2Control/DpadControl 等类型。
        try
        {
            if (control.valueType == typeof(Vector2))
            {
                Vector2 v = (Vector2)control.ReadValueAsObject();
                return v.sqrMagnitude >= threshold * threshold;
            }

            if (control.valueType == typeof(float))
            {
                float v = (float)control.ReadValueAsObject();
                return Mathf.Abs(v) >= threshold;
            }
        }
        catch
        {
            // 兜底策略：读取失败则视为“有效输入”（避免误判导致无法切换）。
        }

        return true;
    }

    private ControlScheme ParseScheme(string schemeName)
    {
        if (string.IsNullOrWhiteSpace(schemeName))
        {
            return ControlScheme.Unknown;
        }

        if (string.Equals(schemeName, SchemeNameKeyboardMouse, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(schemeName, "KeyboardMouse", StringComparison.OrdinalIgnoreCase))
        {
            return ControlScheme.KeyboardMouse;
        }

        if (string.Equals(schemeName, SchemeNameGamepad, StringComparison.OrdinalIgnoreCase))
        {
            return ControlScheme.Gamepad;
        }

        return ControlScheme.Unknown;
    }
}
