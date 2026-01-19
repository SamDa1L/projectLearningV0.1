using System.Collections.Generic;
using UnityEngine;
using CastleDB.Runtime;

/// <summary>
/// Summon ability (0.5 Phase 6).
/// - Uses paramsJson: { "prefabPath":"Prefabs/Enemy/GoblinEnemy/GoblinEnemy", "lifetime":10, "maxCount":2, "spawnRule":"ReplaceOldest" }.
/// - Cooldown uses AbilityCatalogEntry.cooldown.
/// </summary>
public class SummonAbility : IPlayerAbility
{
    private const string PrefabPathKey = "prefabPath";
    private const string LifetimeKey = "lifetime";
    private const string MaxCountKey = "maxCount";
    private const string SpawnRuleKey = "spawnRule";
    private const string AnimTriggerKey = "animTrigger";

    private enum SpawnRule
    {
        ReplaceOldest = 0,
        Reject = 1
    }

    private readonly PlayerController _playerController;
    private readonly Animator _animator;
    private readonly float _cooldownSeconds;

    private readonly string _prefabPath;
    private readonly float _lifetimeSeconds;
    private readonly int _maxCount;
    private readonly SpawnRule _spawnRule;
    private readonly string _animTrigger;

    private float _nextReadyTime;
    private GameObject _cachedPrefab;
    private bool _loggedMissingPrefab;

    private readonly List<GameObject> _instances = new List<GameObject>();

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
                DestroyAllSummons();
            }
        }
    }

    public float CooldownSeconds => _cooldownSeconds;
    public float CooldownRemaining => _cooldownSeconds > 0f ? Mathf.Max(0f, _nextReadyTime - Time.time) : 0f;

    public SummonAbility(
        PlayerController playerController,
        string abilityId,
        int priority,
        bool enabled,
        float cooldownSeconds,
        string paramsJson)
    {
        _playerController = playerController;
        _animator = playerController != null ? playerController.GetComponent<Animator>() : null;
        _cooldownSeconds = Mathf.Max(0f, cooldownSeconds);

        AbilityId = abilityId ?? "";
        Priority = priority;

        ParseParams(paramsJson, out _prefabPath, out _lifetimeSeconds, out _maxCount, out _spawnRule, out _animTrigger);

        _enabled = false;
        Enabled = enabled;
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
            return true;
        }

        if (!TrySpawn())
        {
            return false;
        }

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

    private bool TrySpawn()
    {
        if (string.IsNullOrWhiteSpace(_prefabPath))
        {
            Debug.LogError($"[SummonAbility] prefabPath is empty (abilityId='{AbilityId}')");
            return false;
        }

        CleanupDestroyedSummons();

        int maxCount = Mathf.Max(1, _maxCount);
        if (_instances.Count >= maxCount)
        {
            if (_spawnRule == SpawnRule.Reject)
            {
                return true;
            }

            // Replace oldest
            if (_instances.Count > 0)
            {
                var oldest = _instances[0];
                _instances.RemoveAt(0);
                if (oldest != null)
                {
                    Object.Destroy(oldest);
                }
            }
        }

        if (_cachedPrefab == null)
        {
            _cachedPrefab = Resources.Load<GameObject>(_prefabPath);
            if (_cachedPrefab == null)
            {
                if (!_loggedMissingPrefab)
                {
                    Debug.LogError($"[SummonAbility] Resources.Load failed: '{_prefabPath}' (abilityId='{AbilityId}')");
                    _loggedMissingPrefab = true;
                }
                return false;
            }
        }

        Transform spawnPoint = _playerController != null ? _playerController.AbilityFirePoint : null;
        Vector3 spawnPosition = spawnPoint != null ? spawnPoint.position : Vector3.zero;
        if (_playerController != null && spawnPoint == null)
        {
            spawnPosition = _playerController.transform.position;
        }

        GameObject instance = Object.Instantiate(_cachedPrefab, spawnPosition, _cachedPrefab.transform.rotation);

        if (_playerController != null)
        {
            float dirSign = _playerController.IsFacingRight ? 1f : -1f;
            Vector3 scale = instance.transform.localScale;
            scale.x = Mathf.Abs(scale.x) * dirSign;
            instance.transform.localScale = scale;
        }

        var marker = instance.GetComponent<SummonedByAbility>();
        if (marker == null)
        {
            marker = instance.AddComponent<SummonedByAbility>();
        }
        marker.abilityId = AbilityId;

        if (_lifetimeSeconds > 0f)
        {
            Object.Destroy(instance, _lifetimeSeconds);
        }

        _instances.Add(instance);
        return true;
    }

    private void CleanupDestroyedSummons()
    {
        for (int i = _instances.Count - 1; i >= 0; i--)
        {
            if (_instances[i] == null)
            {
                _instances.RemoveAt(i);
            }
        }
    }

    private void DestroyAllSummons()
    {
        for (int i = 0; i < _instances.Count; i++)
        {
            if (_instances[i] != null)
            {
                Object.Destroy(_instances[i]);
            }
        }

        _instances.Clear();
    }

    private static void ParseParams(
        string paramsJson,
        out string prefabPath,
        out float lifetimeSeconds,
        out int maxCount,
        out SpawnRule spawnRule,
        out string animTrigger)
    {
        prefabPath = "";
        lifetimeSeconds = 0f;
        maxCount = 1;
        spawnRule = SpawnRule.ReplaceOldest;
        animTrigger = "";

        if (string.IsNullOrWhiteSpace(paramsJson))
        {
            return;
        }

        Dictionary<string, object> obj = CastleDbJsonUtil.TryParseJsonObject(paramsJson);
        if (obj == null)
        {
            return;
        }

        if (obj.TryGetValue(PrefabPathKey, out var p) && p != null)
        {
            prefabPath = p.ToString()?.Trim() ?? "";
        }

        if (TryReadFloat(obj, LifetimeKey, out float lifetime) && lifetime >= 0f)
        {
            lifetimeSeconds = lifetime;
        }

        if (TryReadInt(obj, MaxCountKey, out int count) && count > 0)
        {
            maxCount = count;
        }

        if (obj.TryGetValue(SpawnRuleKey, out var r) && r != null)
        {
            string raw = r.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                if (string.Equals(raw, "Reject", System.StringComparison.OrdinalIgnoreCase))
                {
                    spawnRule = SpawnRule.Reject;
                }
                else if (string.Equals(raw, "ReplaceOldest", System.StringComparison.OrdinalIgnoreCase))
                {
                    spawnRule = SpawnRule.ReplaceOldest;
                }
            }
        }

        if (obj.TryGetValue(AnimTriggerKey, out var t) && t != null)
        {
            animTrigger = t.ToString()?.Trim() ?? "";
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

    private static bool TryReadInt(Dictionary<string, object> obj, string key, out int value)
    {
        value = 0;

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
            case int i:
                value = i;
                return true;
            case long l:
                value = (int)l;
                return true;
            case float f:
                value = Mathf.RoundToInt(f);
                return true;
            case double d:
                value = Mathf.RoundToInt((float)d);
                return true;
            case string s:
                return int.TryParse(s, out value);
        }

        return false;
    }

    public bool OnMove(AbilityInput input) => TryCast(input);
    public bool OnRun(AbilityInput input) => TryCast(input);
    public bool OnJump(AbilityInput input) => TryCast(input);
    public bool OnAttack(AbilityInput input) => TryCast(input);
    public bool OnRangedAttack(AbilityInput input) => TryCast(input);
}
