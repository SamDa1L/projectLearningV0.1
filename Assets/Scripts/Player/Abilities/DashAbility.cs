using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dash ability (0.5 Phase 6).
/// - Triggered by Run input (Started).
/// - Uses paramsJson: { "distance":3, "speed":12, "invincibleWindow":0.2, "animTrigger":"dash" }.
/// - Cooldown uses AbilityCatalogEntry.cooldown.
/// </summary>
public class DashAbility : IPlayerAbility
{
    private const string DistanceKey = "distance";
    private const string SpeedKey = "speed";
    private const string InvincibleWindowKey = "invincibleWindow";
    private const string AnimTriggerKey = "animTrigger";

    private readonly PlayerController _playerController;
    private readonly Animator _animator;
    private readonly Rigidbody2D _rb;
    private readonly Damageable _damageable;

    private readonly float _distance;
    private readonly float _speed;
    private readonly float _invincibleWindowSeconds;
    private readonly string _animTrigger;
    private readonly float _cooldownSeconds;

    private float _nextReadyTime;

    private Coroutine _dashRoutine;
    private bool _dashPrevLockVelocity;
    private bool _dashLockVelocityCaptured;

    public string AbilityId { get; }
    public int Priority { get; }

    private bool _enabled;
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            _enabled = value;

            // If the ability gets disabled mid-dash (e.g. replacement), stop and restore state.
            if (!_enabled)
            {
                StopDash(restoreVelocity: true);
            }
        }
    }

    public DashAbility(
        PlayerController playerController,
        string abilityId,
        int priority,
        bool enabled,
        float cooldownSeconds,
        string paramsJson)
    {
        _playerController = playerController;
        _animator = playerController != null ? playerController.GetComponent<Animator>() : null;
        _rb = playerController != null ? playerController.GetComponent<Rigidbody2D>() : null;
        _damageable = playerController != null ? playerController.GetComponent<Damageable>() : null;

        AbilityId = abilityId ?? "";
        Priority = priority;

        _cooldownSeconds = Mathf.Max(0f, cooldownSeconds);

        ParseParams(
            paramsJson,
            out _distance,
            out _speed,
            out _invincibleWindowSeconds,
            out _animTrigger);

        _enabled = false;
        Enabled = enabled;
    }

    public bool OnRun(AbilityInput input)
    {
        // Preserve default Run behavior (SetRunning true/false) while optionally adding dash.
        if (input.Phase == AbilityInputPhase.Started)
        {
            _playerController?.SetRunning(true);
            TryStartDash();
            return true;
        }

        if (input.Phase == AbilityInputPhase.Canceled)
        {
            _playerController?.SetRunning(false);
            return true;
        }

        return false;
    }

    private bool TryStartDash()
    {
        if (_playerController == null || _rb == null || _damageable == null)
        {
            return false;
        }

        if (!_playerController.IsAlive)
        {
            return false;
        }

        // Do not fight with hit-stun / attack lock, and do not dash while climbing.
        if (_damageable.LockVelocity || !_playerController.CanMove || _playerController.IsClimbing)
        {
            return false;
        }

        if (_cooldownSeconds > 0f && Time.time < _nextReadyTime)
        {
            return false;
        }

        if (_distance <= 0f || _speed <= 0f)
        {
            return false;
        }

        StopDash(restoreVelocity: false); // ensure single dash routine

        if (_animator != null && !string.IsNullOrWhiteSpace(_animTrigger))
        {
            _animator.SetTrigger(_animTrigger);
        }

        if (_invincibleWindowSeconds > 0f)
        {
            _damageable.GrantExternalInvulnerability(_invincibleWindowSeconds);
        }

        float durationSeconds = _distance / _speed;
        durationSeconds = Mathf.Max(0.01f, durationSeconds);

        _dashRoutine = _playerController.StartCoroutine(DashRoutine(durationSeconds));

        if (_cooldownSeconds > 0f)
        {
            _nextReadyTime = Time.time + _cooldownSeconds;
        }

        return true;
    }

    private IEnumerator DashRoutine(float durationSeconds)
    {
        CaptureAndLockVelocity();

        float dirSign = _playerController != null && _playerController.IsFacingRight ? 1f : -1f;
        float endTime = Time.time + durationSeconds;

        while (Time.time < endTime)
        {
            if (_rb != null)
            {
                _rb.velocity = new Vector2(_speed * dirSign, _rb.velocity.y);
            }

            yield return new WaitForFixedUpdate();
        }

        RestoreVelocityLock();
        _dashRoutine = null;
    }

    private void CaptureAndLockVelocity()
    {
        if (_damageable == null)
        {
            return;
        }

        _dashPrevLockVelocity = _damageable.LockVelocity;
        _dashLockVelocityCaptured = true;
        _damageable.LockVelocity = true;
    }

    private void RestoreVelocityLock()
    {
        if (_rb != null)
        {
            _rb.velocity = new Vector2(0f, _rb.velocity.y);
        }

        if (_damageable != null && _dashLockVelocityCaptured)
        {
            _damageable.LockVelocity = _dashPrevLockVelocity;
        }

        _dashLockVelocityCaptured = false;
    }

    private void StopDash(bool restoreVelocity)
    {
        if (_dashRoutine != null && _playerController != null)
        {
            _playerController.StopCoroutine(_dashRoutine);
        }

        _dashRoutine = null;

        if (restoreVelocity)
        {
            RestoreVelocityLock();
        }
    }

    private static void ParseParams(
        string paramsJson,
        out float distance,
        out float speed,
        out float invincibleWindowSeconds,
        out string animTrigger)
    {
        distance = 3f;
        speed = 12f;
        invincibleWindowSeconds = 0f;
        animTrigger = "";

        if (string.IsNullOrWhiteSpace(paramsJson))
        {
            return;
        }

        Dictionary<string, object> obj = CastleDB.Runtime.CastleDbJsonUtil.TryParseJsonObject(paramsJson);
        if (obj == null)
        {
            return;
        }

        if (TryReadFloat(obj, DistanceKey, out float d) && d > 0f)
        {
            distance = d;
        }

        if (TryReadFloat(obj, SpeedKey, out float s) && s > 0f)
        {
            speed = s;
        }

        if (TryReadFloat(obj, InvincibleWindowKey, out float w) && w > 0f)
        {
            invincibleWindowSeconds = w;
        }

        if (TryReadString(obj, AnimTriggerKey, out string trigger))
        {
            animTrigger = trigger;
        }
    }

    private static bool TryReadString(Dictionary<string, object> obj, string key, out string value)
    {
        value = null;

        if (obj == null || string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        if (!obj.TryGetValue(key, out object raw) || raw == null)
        {
            return false;
        }

        value = raw.ToString()?.Trim();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryReadFloat(Dictionary<string, object> obj, string key, out float value)
    {
        value = 0f;

        if (obj == null || string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        if (!obj.TryGetValue(key, out object raw) || raw == null)
        {
            return false;
        }

        switch (raw)
        {
            case float f:
                value = f;
                return true;
            case double d:
                value = (float)d;
                return true;
            case int i:
                value = i;
                return true;
            case long l:
                value = l;
                return true;
            case string s:
                return float.TryParse(s, out value);
        }

        return false;
    }

    public bool OnMove(AbilityInput input) => false;
    public bool OnJump(AbilityInput input) => false;
    public bool OnAttack(AbilityInput input) => false;
    public bool OnRangedAttack(AbilityInput input) => false;
}

