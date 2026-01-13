using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using CastleDB.Runtime;

/// <summary>
/// Passive StatModifier ability (0.5 Phase 5).
/// - Reads modifiersJson from AbilityBuffDefinition referenced by AbilityCatalogEntry.buffId.
/// - Applies/removes modifiers via StatModifierLayer using AbilityId as sourceId.
/// </summary>
public class StatModifierPassiveAbility : IPlayerAbility
{
    private const string MoveSpeedMultiplierKey = "moveSpeedMultiplier";
    private const string AttackMultiplierKey = "attackMultiplier";

    private readonly PlayerController _playerController;
    private readonly AbilityCatalog _catalog;
    private readonly string _buffId;
    private readonly GameObject _owner;

    private StatModifierLayer _stats;
    private bool _enabled;
    private bool _loggedMissingStats;

    private AbilityBuffDefinition _buffDef;
    private float _moveSpeedMultiplier = 1f;
    private float _attackMultiplier = 1f;
    private GameObject _loopVfxInstance;

    public string AbilityId { get; }
    public int Priority { get; }

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

            if (_enabled)
            {
                Apply();
            }
            else
            {
                Rollback();
            }
        }
    }

    public StatModifierPassiveAbility(
        PlayerController playerController,
        string abilityId,
        int priority,
        bool enabled,
        AbilityCatalog catalog,
        string buffId)
    {
        _playerController = playerController;
        _catalog = catalog;
        _buffId = buffId ?? "";
        _owner = playerController != null ? playerController.gameObject : null;

        AbilityId = abilityId ?? "";
        Priority = priority;

        ResolveDefinitions();

        _enabled = false;
        Enabled = enabled;
    }

    private void ResolveDefinitions()
    {
        _stats = _playerController != null ? _playerController.GetComponent<StatModifierLayer>() : null;
        _moveSpeedMultiplier = 1f;
        _attackMultiplier = 1f;
        _buffDef = null;

        if (_catalog == null)
        {
            Debug.LogWarning($"[StatModifierPassiveAbility] AbilityCatalog is null (abilityId='{AbilityId}')");
            return;
        }

        if (string.IsNullOrWhiteSpace(_buffId))
        {
            Debug.LogError($"[StatModifierPassiveAbility] buffId is empty (abilityId='{AbilityId}')");
            return;
        }

        if (!_catalog.TryGetBuff(_buffId, out var buff) || buff == null)
        {
            Debug.LogError($"[StatModifierPassiveAbility] Buff not found: buffId='{_buffId}' (abilityId='{AbilityId}')");
            return;
        }

        _buffDef = buff;
        ParseModifiers(buff.modifiersJson, abilityId: AbilityId, buffId: _buffId, out _moveSpeedMultiplier, out _attackMultiplier);
    }

    private void Apply()
    {
        if (!EnsureStats())
        {
            return;
        }

        _stats.SetMoveSpeedMultiplier(AbilityId, _moveSpeedMultiplier);
        _stats.SetAttackMultiplier(AbilityId, _attackMultiplier);

        AbilityBuffVfx.DestroyLoop(_loopVfxInstance);
        _loopVfxInstance = null;

        if (_owner != null && _buffDef != null)
        {
            _loopVfxInstance = AbilityBuffVfx.SpawnLoop(_buffDef, _owner.transform);
        }
    }

    private void Rollback()
    {
        if (!EnsureStats())
        {
            return;
        }

        _stats.ClearMoveSpeedMultiplier(AbilityId);
        _stats.ClearAttackMultiplier(AbilityId);

        Vector3 expirePos = _owner != null ? _owner.transform.position : Vector3.zero;
        if (_loopVfxInstance != null)
        {
            expirePos = _loopVfxInstance.transform.position;
        }

        AbilityBuffVfx.DestroyLoop(_loopVfxInstance);
        _loopVfxInstance = null;

        if (_buffDef != null)
        {
            AbilityBuffVfx.SpawnExpire(_buffDef, expirePos);
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

        if (!_loggedMissingStats)
        {
            Debug.LogError($"[StatModifierPassiveAbility] StatModifierLayer missing (abilityId='{AbilityId}')");
            _loggedMissingStats = true;
        }

        return false;
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
            Debug.LogError($"[StatModifierPassiveAbility] modifiersJson must be a JSON object (abilityId='{abilityId}', buffId='{buffId}')");
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
                return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        try
        {
            value = Convert.ToSingle(raw, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool OnMove(AbilityInput input) => false;
    public bool OnRun(AbilityInput input) => false;
    public bool OnJump(AbilityInput input) => false;
    public bool OnAttack(AbilityInput input) => false;
    public bool OnRangedAttack(AbilityInput input) => false;
}
