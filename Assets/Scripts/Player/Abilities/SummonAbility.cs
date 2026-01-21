using System.Collections.Generic;
using UnityEngine;
using CastleDB.Runtime;

/// <summary>
/// 召唤能力（0.5 扩展）。
/// - 结构化：AbilityCatalogEntry.summonId -> AbilityCatalog.summons
///   （prefabPath / lifetime / isDead / factionOverride / maxCount / spawnRule）。
/// - 兼容旧版：当 summonId 缺失（或定义缺失）时，回退读取 paramsJson：
///   { "prefabPath":"...", "lifetime":10, "isDead":true, "factionOverride":"friend", "maxCount":2, "spawnRule":"ReplaceOldest" }。
/// - paramsJson 仍支持：{ "animTrigger":"..." }（施法动画 Trigger）。
/// - 冷却使用 AbilityCatalogEntry.cooldown。
/// </summary>
public class SummonAbility : IPlayerAbility
{
    private const string PrefabPathKey = "prefabPath";
    private const string LifetimeKey = "lifetime";
    private const string IsDeadKey = "isDead";
    private const string FactionOverrideKey = "factionOverride";
    private const string MaxCountKey = "maxCount";
    private const string SpawnRuleKey = "spawnRule";
    private const string AnimTriggerKey = "animTrigger";

    private readonly PlayerController _playerController;
    private readonly Animator _animator;
    private readonly float _cooldownSeconds;

    private readonly string _prefabPath;
    private readonly float _lifetimeSeconds;
    private readonly bool _isDead;
    private readonly FactionId _factionOverride;
    private readonly int _maxCount;
    private readonly AbilitySummonSpawnRule _spawnRule;
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
        AbilitySummonDefinition summonDef,
        float cooldownSeconds,
        string paramsJson)
    {
        _playerController = playerController;
        _animator = playerController != null ? playerController.GetComponent<Animator>() : null;
        _cooldownSeconds = Mathf.Max(0f, cooldownSeconds);

        AbilityId = abilityId ?? "";
        Priority = priority;

        string prefabPath = "";
        float lifetimeSeconds = 0f;
        bool isDead = false;
        FactionId factionOverride = FactionId.None;
        int maxCount = 1;
        AbilitySummonSpawnRule spawnRule = AbilitySummonSpawnRule.ReplaceOldest;
        string animTrigger = "";

        Dictionary<string, object> obj = null;
        if (!string.IsNullOrWhiteSpace(paramsJson))
        {
            obj = CastleDbJsonUtil.TryParseJsonObject(paramsJson);
        }

        if (summonDef != null)
        {
            prefabPath = summonDef.prefabPath ?? "";
            lifetimeSeconds = NormalizeLifetimeSeconds(summonDef.lifetime);
            isDead = summonDef.isDead;
            factionOverride = summonDef.factionOverride;
            maxCount = Mathf.Max(1, summonDef.maxCount);
            spawnRule = summonDef.spawnRule;
        }
        else if (obj != null)
        {
            ParseLegacyParams(obj, out prefabPath, out lifetimeSeconds, out isDead, out factionOverride, out maxCount, out spawnRule);
        }

        if (obj != null && obj.TryGetValue(AnimTriggerKey, out var t) && t != null)
        {
            animTrigger = t.ToString()?.Trim() ?? "";
        }

        _prefabPath = prefabPath;
        _lifetimeSeconds = lifetimeSeconds;
        _isDead = isDead;
        _factionOverride = factionOverride;
        _maxCount = maxCount;
        _spawnRule = spawnRule;
        _animTrigger = animTrigger;

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
            if (_spawnRule == AbilitySummonSpawnRule.Reject)
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
            // 注意：部分 NPC（如 NpcGroundController）有“移动方向状态机”，只改 scale 会导致“面朝左但向右走”。
            // 因此优先把“移动方向”设置到控制器上，再做一次 scale 兜底归一化。
            bool facingRight = _playerController.IsFacingRight;

            var groundController = instance.GetComponent<NpcGroundController>();
            if (groundController != null)
            {
                groundController.WalkDirection = facingRight
                    ? NpcGroundController.WalkableDirection.Right
                    : NpcGroundController.WalkableDirection.Left;
            }

            float dirSign = facingRight ? 1f : -1f;
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

        // 阵营覆写（可选）：None/null 表示不覆写；Enemy/Friend/Neutral 表示强制覆写
        FactionId desiredFaction = FactionUtility.GetFaction(instance);
        if (_factionOverride != FactionId.None)
        {
            desiredFaction = _factionOverride;
        }

        var factionMember = instance.GetComponent<FactionMember>();
        if (factionMember == null)
        {
            factionMember = instance.AddComponent<FactionMember>();
        }
        factionMember.Faction = desiredFaction;
        FactionLayerApplier.Apply(instance, desiredFaction);

        // 生命周期：支持 lifetime=-1 + isDead 的组合逻辑（由组件统一处理）
        var lifetimeController = instance.GetComponent<SummonLifetimeController>();
        if (lifetimeController == null)
        {
            lifetimeController = instance.AddComponent<SummonLifetimeController>();
        }
        lifetimeController.Configure(_lifetimeSeconds, _isDead);

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

    private static float NormalizeLifetimeSeconds(float lifetimeSeconds)
    {
        // -1 表示“无时间限制”，其余负数视为 0（不启用时间销毁）
        if (lifetimeSeconds > 0f)
        {
            return lifetimeSeconds;
        }

        if (Mathf.Approximately(lifetimeSeconds, -1f))
        {
            return -1f;
        }

        return 0f;
    }

    private static bool TryParseFactionOverrideString(string raw, out FactionId faction)
    {
        faction = FactionId.None;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        string trimmed = raw.Trim();

        if (string.Equals(trimmed, "null", System.StringComparison.OrdinalIgnoreCase))
        {
            faction = FactionId.None;
            return true;
        }

        if (string.Equals(trimmed, "enemy", System.StringComparison.OrdinalIgnoreCase))
        {
            faction = FactionId.Enemy;
            return true;
        }

        if (string.Equals(trimmed, "friend", System.StringComparison.OrdinalIgnoreCase))
        {
            faction = FactionId.Friend;
            return true;
        }

        if (string.Equals(trimmed, "neutral", System.StringComparison.OrdinalIgnoreCase))
        {
            faction = FactionId.Neutral;
            return true;
        }

        return false;
    }

    private static FactionId ParseFactionOverrideLegacyInt(int raw)
    {
        // 兼容旧版（0.4 及更早）数字映射：-1=不覆写，0=Enemy，1=Friend，2=Neutral
        // 注意：此映射与数据枚举（0=null，1=enemy，2=friend，3=Neutral）不同；如需无歧义，推荐在 JSON 中使用字符串枚举。
        switch (raw)
        {
            case 0:
                return FactionId.Enemy;
            case 1:
                return FactionId.Friend;
            case 2:
                return FactionId.Neutral;
            case 3:
                // 兼容有人按“数据枚举”误填 3=Neutral 的情况
                return FactionId.Neutral;
            case -1:
            default:
                return FactionId.None;
        }
    }

    private static void ParseLegacyParams(
        Dictionary<string, object> obj,
        out string prefabPath,
        out float lifetimeSeconds,
        out bool isDead,
        out FactionId factionOverride,
        out int maxCount,
        out AbilitySummonSpawnRule spawnRule)
    {
        prefabPath = "";
        lifetimeSeconds = 0f;
        isDead = false;
        factionOverride = FactionId.None;
        maxCount = 1;
        spawnRule = AbilitySummonSpawnRule.ReplaceOldest;

        if (obj.TryGetValue(PrefabPathKey, out var p) && p != null)
        {
            prefabPath = p.ToString()?.Trim() ?? "";
        }

        if (TryReadFloat(obj, LifetimeKey, out float lifetime))
        {
            lifetimeSeconds = NormalizeLifetimeSeconds(lifetime);
        }

        if (TryReadBool(obj, IsDeadKey, out bool dead))
        {
            isDead = dead;
        }

        if (obj.TryGetValue(FactionOverrideKey, out var rawFactionOverride) && rawFactionOverride != null)
        {
            // 字符串枚举优先，避免与旧版数字映射产生歧义
            if (rawFactionOverride is string s && TryParseFactionOverrideString(s, out var parsedFaction))
            {
                factionOverride = parsedFaction;
            }
            else if (TryReadInt(obj, FactionOverrideKey, out int factionRaw))
            {
                factionOverride = ParseFactionOverrideLegacyInt(factionRaw);
            }
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
                    spawnRule = AbilitySummonSpawnRule.Reject;
                }
                else if (string.Equals(raw, "ReplaceOldest", System.StringComparison.OrdinalIgnoreCase))
                {
                    spawnRule = AbilitySummonSpawnRule.ReplaceOldest;
                }
            }
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

    private static bool TryReadBool(Dictionary<string, object> obj, string key, out bool value)
    {
        value = false;

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
            case bool b:
                value = b;
                return true;
            case int i:
                value = i != 0;
                return true;
            case long l:
                value = l != 0;
                return true;
            case float f:
                value = !Mathf.Approximately(f, 0f);
                return true;
            case double d:
                value = !Mathf.Approximately((float)d, 0f);
                return true;
            case string s:
                return bool.TryParse(s, out value);
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
