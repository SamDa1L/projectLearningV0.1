using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using CastleDB.Runtime;

/// <summary>
/// Active Buff ability (0.5 Phase 6).
/// - kind=Buff, buffId -> AbilityCatalog.buffs
/// - Cooldown uses AbilityCatalogEntry.cooldown.
/// - Applies StatModifierLayer modifiers for a duration, then rolls back.
/// - Re-cast obeys AbilityBuffDefinition.stackRule/maxStacks.
/// </summary>
public class ActiveBuffAbility : IPlayerAbility
{
    private const string MoveSpeedMultiplierKey = "moveSpeedMultiplier";
    private const string AttackMultiplierKey = "attackMultiplier";
    private const string AnimTriggerKey = "animTrigger";

    private readonly PlayerController _playerController;
    private readonly Animator _animator;
    private readonly AbilityCatalog _catalog;
    private readonly string _buffId;
    private readonly float _cooldownSeconds;

    private StatModifierLayer _stats;
    private AbilityBuffDefinition _buffDef;
    private float _baseMoveSpeedMultiplier = 1f;
    private float _baseAttackMultiplier = 1f;
    private string _animTrigger = "";

    private int _stacks;
    private float _expiresAtTime;
    private Coroutine _expireRoutine;
    private GameObject _loopVfxInstance;
    private float _nextReadyTime;

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

            if (!_enabled)
            {
                ClearActive(spawnExpireVfx: true);
            }
        }
    }

    public float CooldownSeconds => _cooldownSeconds;
    public float CooldownRemaining => _cooldownSeconds > 0f ? Mathf.Max(0f, _nextReadyTime - Time.time) : 0f;

    public ActiveBuffAbility(
        PlayerController playerController,
        string abilityId,
        int priority,
        bool enabled,
        AbilityCatalog catalog,
        string buffId,
        float cooldownSeconds,
        string paramsJson)
    {
        _playerController = playerController;
        _animator = playerController != null ? playerController.GetComponent<Animator>() : null;
        _catalog = catalog;
        _buffId = buffId ?? "";
        _cooldownSeconds = Mathf.Max(0f, cooldownSeconds);

        AbilityId = abilityId ?? "";
        Priority = priority;

        ResolveDefinitions(paramsJson);

        _enabled = false;
        Enabled = enabled;
    }

    private void ResolveDefinitions(string paramsJson)
    {
        _stats = _playerController != null ? _playerController.GetComponent<StatModifierLayer>() : null;
        _buffDef = null;
        _baseMoveSpeedMultiplier = 1f;
        _baseAttackMultiplier = 1f;
        _animTrigger = "";

        if (_catalog == null)
        {
            Debug.LogWarning($"[ActiveBuffAbility] AbilityCatalog is null (abilityId='{AbilityId}')");
            return;
        }

        if (string.IsNullOrWhiteSpace(_buffId))
        {
            Debug.LogError($"[ActiveBuffAbility] buffId is empty (abilityId='{AbilityId}')");
            return;
        }

        if (!_catalog.TryGetBuff(_buffId, out var buff) || buff == null)
        {
            Debug.LogError($"[ActiveBuffAbility] Buff not found: buffId='{_buffId}' (abilityId='{AbilityId}')");
            return;
        }

        _buffDef = buff;
        ParseModifiers(buff.modifiersJson, abilityId: AbilityId, buffId: _buffId, out _baseMoveSpeedMultiplier, out _baseAttackMultiplier);

        if (!string.IsNullOrWhiteSpace(paramsJson))
        {
            var obj = CastleDbJsonUtil.TryParseJsonObject(paramsJson);
            if (obj != null && obj.TryGetValue(AnimTriggerKey, out var raw) && raw != null)
            {
                _animTrigger = raw.ToString()?.Trim() ?? "";
            }
        }
    }

    private bool EnsureStats()
    {
        if (_stats != null)
        {
            return true;
        }

        _stats = _playerController != null ? _playerController.GetComponent<StatModifierLayer>() : null;
        if (_stats != null)
        {
            return true;
        }

        Debug.LogError($"[ActiveBuffAbility] StatModifierLayer missing (abilityId='{AbilityId}')");
        return false;
    }

    private bool TryCast(AbilityInput input)
    {
        if (input.Phase != AbilityInputPhase.Started)
        {
            return false;
        }

        if (_playerController != null && !_playerController.IsAlive)
        {
            return true;
        }

        if (_cooldownSeconds > 0f && Time.time < _nextReadyTime)
        {
            return true; // consume input (avoid fallback ability mis-trigger)
        }

        if (_buffDef == null)
        {
            Debug.LogError($"[ActiveBuffAbility] buffDef is null (abilityId='{AbilityId}', buffId='{_buffId}')");
            return false;
        }

        if (!EnsureStats())
        {
            return false;
        }

        ApplyOrRefresh(now: Time.time);

        if (_cooldownSeconds > 0f)
        {
            _nextReadyTime = Time.time + _cooldownSeconds;
        }

        if (_animator != null && !string.IsNullOrWhiteSpace(_animTrigger))
        {
            _animator.SetTrigger(_animTrigger);
        }

        return true;
    }

    private void ApplyOrRefresh(float now)
    {
        if (_stacks <= 0)
        {
            _stacks = 1;
            ApplyToStats();
            AbilityBuffVfx.DestroyLoop(_loopVfxInstance);
            _loopVfxInstance = AbilityBuffVfx.SpawnLoop(_buffDef, _playerController.transform);
            RefreshExpire(now);
            return;
        }

        switch (_buffDef.stackRule)
        {
            case StatusStackRule.Ignore:
                return;
            case StatusStackRule.Refresh:
                // no stack change
                RefreshExpire(now);
                return;
            case StatusStackRule.Add:
                _stacks = Mathf.Min(_stacks + 1, Mathf.Max(1, _buffDef.maxStacks));
                ApplyToStats();
                RefreshExpire(now);
                return;
            case StatusStackRule.Replace:
                _stacks = 1;
                ApplyToStats();
                RefreshExpire(now);
                return;
            default:
                Debug.LogError($"[ActiveBuffAbility] Unsupported stackRule: {_buffDef.stackRule} (abilityId='{AbilityId}', buffId='{_buffId}')");
                return;
        }
    }

    private void RefreshExpire(float now)
    {
        float duration = _buffDef != null ? _buffDef.duration : 0f;
        if (duration <= 0f)
        {
            _expiresAtTime = 0f;
            StopExpireRoutine();
            return;
        }

        _expiresAtTime = now + duration;

        StopExpireRoutine();
        if (_playerController != null)
        {
            _expireRoutine = _playerController.StartCoroutine(ExpireAfterSeconds(duration));
        }
    }

    private IEnumerator ExpireAfterSeconds(float seconds)
    {
        yield return new WaitForSeconds(seconds);

        // If the buff was refreshed while waiting, ignore this expire.
        if (_expiresAtTime > 0f && Time.time + 0.001f < _expiresAtTime)
        {
            yield break;
        }

        ClearActive(spawnExpireVfx: true);
    }

    private void StopExpireRoutine()
    {
        if (_expireRoutine != null && _playerController != null)
        {
            _playerController.StopCoroutine(_expireRoutine);
        }

        _expireRoutine = null;
    }

    private void ApplyToStats()
    {
        if (_stats == null)
        {
            return;
        }

        int stacks = Mathf.Max(1, _stacks);
        float move = Mathf.Pow(Mathf.Max(0f, _baseMoveSpeedMultiplier), stacks);
        float atk = Mathf.Pow(Mathf.Max(0f, _baseAttackMultiplier), stacks);

        _stats.SetMoveSpeedMultiplier(AbilityId, move);
        _stats.SetAttackMultiplier(AbilityId, atk);
    }

    private void ClearActive(bool spawnExpireVfx)
    {
        StopExpireRoutine();

        if (_stats != null)
        {
            _stats.ClearMoveSpeedMultiplier(AbilityId);
            _stats.ClearAttackMultiplier(AbilityId);
        }

        Vector3 expirePos = _playerController != null ? _playerController.transform.position : Vector3.zero;
        if (_loopVfxInstance != null)
        {
            expirePos = _loopVfxInstance.transform.position;
        }

        AbilityBuffVfx.DestroyLoop(_loopVfxInstance);
        _loopVfxInstance = null;

        if (spawnExpireVfx && _buffDef != null)
        {
            AbilityBuffVfx.SpawnExpire(_buffDef, expirePos);
        }

        _stacks = 0;
        _expiresAtTime = 0f;
    }

    private static void ParseModifiers(
        string modifiersJson,
        string abilityId,
        string buffId,
        out float moveSpeedMultiplier,
        out float attackMultiplier)
    {
        moveSpeedMultiplier = 1f;
        attackMultiplier = 1f;

        if (string.IsNullOrWhiteSpace(modifiersJson))
        {
            return;
        }

        Dictionary<string, object> obj = CastleDbJsonUtil.TryParseJsonObject(modifiersJson);
        if (obj == null)
        {
            Debug.LogError($"[ActiveBuffAbility] modifiersJson must be a JSON object (abilityId='{abilityId}', buffId='{buffId}')");
            return;
        }

        if (TryReadFloat(obj, MoveSpeedMultiplierKey, out float rawMoveSpeedMultiplier))
        {
            moveSpeedMultiplier = Mathf.Max(0f, rawMoveSpeedMultiplier);
        }

        if (TryReadFloat(obj, AttackMultiplierKey, out float rawAttackMultiplier))
        {
            attackMultiplier = Mathf.Max(0f, rawAttackMultiplier);
        }
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

    public bool OnMove(AbilityInput input) => TryCast(input);
    public bool OnRun(AbilityInput input) => TryCast(input);
    public bool OnJump(AbilityInput input) => TryCast(input);
    public bool OnAttack(AbilityInput input) => TryCast(input);
    public bool OnRangedAttack(AbilityInput input) => TryCast(input);
}
