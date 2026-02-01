using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Phase 9：用于验收/排查的最小提示面板。
/// 显示当前控制方案 + 关键 Action 的绑定显示字符串。
/// </summary>
[DisallowMultipleComponent]
public sealed class InputModeHintOverlay : MonoBehaviour
{
    [SerializeField] private bool showOverlay = true;
    [SerializeField] private bool refreshContinuously = true;
    [SerializeField, Min(0.05f)] private float refreshIntervalSeconds = 0.5f;
    [SerializeField] private Vector2 margin = new Vector2(12f, 12f);

    private PlayerInput _playerInput;
    private InputModeSwitcher _switcher;

    private string _cached;
    private float _nextRefreshTime;

    private void Awake()
    {
        _playerInput = GetComponent<PlayerInput>();
        _switcher = GetComponent<InputModeSwitcher>();
    }

    private void OnEnable()
    {
        if (_switcher != null)
        {
            _switcher.OnControlSchemeChanged += OnSchemeChanged;
        }

        RefreshNow();
    }

    private void OnDisable()
    {
        if (_switcher != null)
        {
            _switcher.OnControlSchemeChanged -= OnSchemeChanged;
        }
    }

    private void Update()
    {
        if (!showOverlay || !refreshContinuously)
        {
            return;
        }

        if (Time.unscaledTime >= _nextRefreshTime)
        {
            RefreshNow();
        }
    }

    private void OnGUI()
    {
        if (!showOverlay || string.IsNullOrEmpty(_cached))
        {
            return;
        }

        Rect rect = new Rect(margin.x, margin.y, Screen.width - margin.x * 2f, Screen.height - margin.y * 2f);
        GUI.Label(rect, _cached);
    }

    private void OnSchemeChanged(InputModeSwitcher.ControlScheme oldScheme, InputModeSwitcher.ControlScheme newScheme)
    {
        RefreshNow();
    }

    private void RefreshNow()
    {
        _nextRefreshTime = Time.unscaledTime + refreshIntervalSeconds;

        if (_playerInput == null || _playerInput.actions == null)
        {
            _cached = "[Input] Missing PlayerInput/actions";
            return;
        }

        string group = _playerInput.currentControlScheme;
        var sb = new StringBuilder(256);
        sb.Append("Control Scheme: ").Append(string.IsNullOrEmpty(group) ? "<none>" : group).AppendLine();

        string mapName = _playerInput.currentActionMap != null ? _playerInput.currentActionMap.name : "<none>";
        sb.Append("Action Map: ").Append(mapName).AppendLine();
        sb.AppendLine();

        AppendAction(sb, "Player/Move", group);
        AppendAction(sb, "Player/Run", group);
        AppendAction(sb, "Player/Jump", group);
        AppendAction(sb, "Player/Attack", group);
        AppendAction(sb, "Player/Ability2", group);
        AppendAction(sb, "Player/Ability3", group);
        AppendAction(sb, "Player/Ability4", group);
        AppendAction(sb, "UI/Escape", group);

        _cached = sb.ToString();
    }

    private void AppendAction(StringBuilder sb, string actionPath, string group)
    {
        if (_playerInput == null || _playerInput.actions == null)
        {
            return;
        }

        InputAction action = _playerInput.actions.FindAction(actionPath, throwIfNotFound: false);
        if (action == null)
        {
            sb.Append(actionPath).Append(": <missing>").AppendLine();
            return;
        }

        string display = string.IsNullOrEmpty(group)
            ? action.GetBindingDisplayString()
            : action.GetBindingDisplayString(group: group);

        if (string.IsNullOrEmpty(display))
        {
            display = "<unbound>";
        }

        string label = actionPath;
        int slashIndex = actionPath.IndexOf('/');
        if (slashIndex >= 0 && slashIndex + 1 < actionPath.Length)
        {
            label = actionPath.Substring(slashIndex + 1);
        }

        sb.Append(label).Append(": ").Append(display).AppendLine();
    }
}
