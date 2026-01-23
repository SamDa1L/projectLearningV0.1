using System;
using System.Collections.Generic;
using UnityEngine;
using CastleDB.Runtime;

/// <summary>
/// 状态效果控制器（Phase 1-4 最小落地）
///
/// 职责：
/// - Apply/Refresh/Remove/Expire
/// - stackRule（Refresh/Add/Ignore/Replace）+ maxStacks
/// - 将状态 modifiers 应用到 StatModifierLayer（目前仅 MoveSpeedMultiplier）
/// </summary>
[RequireComponent(typeof(StatModifierLayer))]
public class StatusEffectController : MonoBehaviour
{
    // 兜底资源提供器：把资源加载调用收口到一个位置。
    // 注意：正常情况下应由 GameBootstrap 注入真实的 StatusCatalog，避免到处散落字符串路径与重复加载。
    private static readonly IGameAssetProvider _fallbackAssets = ResourcesGameAssetProvider.Shared;

    [Header("可选覆盖")]
    [SerializeField]
    [Tooltip("可选：覆盖 StatusCatalog（为空则运行时通过 IGameAssetProvider 加载默认 Config/StatusCatalog）")]
    private StatusCatalog statusCatalogOverride;

    private readonly Dictionary<string, StatusRuntimeState> _active = new Dictionary<string, StatusRuntimeState>();
    private readonly List<string> _activeList = new List<string>(); // 保持稳定输出顺序（便于调试/测试）
    private readonly List<string> _expireBuffer = new List<string>();
    private readonly Dictionary<string, StatusDefinition> _fallbackDefinitions = new Dictionary<string, StatusDefinition>();

    private StatusCatalog _catalog;
    private StatModifierLayer _stats;

    private bool _loggedMissingCatalog = false;
    private bool _loggedMissingStats = false;

    public event Action<string, int> OnStatusApplied;
    public event Action<string> OnStatusRemoved;
    public event Action<string> OnStatusExpired;

    /// <summary>
    /// 当前已激活的状态 ID 列表（调试/测试用）
    /// </summary>
    public IReadOnlyList<string> ActiveStatusIds => _activeList;

    private void Awake()
    {
        _stats = GetComponent<StatModifierLayer>();
        _catalog = statusCatalogOverride;
    }

    private bool EnsureStats()
    {
        if (_stats != null)
        {
            return true;
        }

        // 编辑模式测试中 Awake 不一定按预期执行；这里做一次懒缓存兜底。
        _stats = GetComponent<StatModifierLayer>();
        if (_stats != null)
        {
            return true;
        }

        if (!_loggedMissingStats)
        {
            Debug.LogError("[StatusEffectController] Missing required StatModifierLayer. Status modifiers will be skipped.", this);
            _loggedMissingStats = true;
        }

        return false;
    }

    /// <summary>
    /// 显式注入 StatusCatalog（可选；不注入则运行时懒加载）
    /// </summary>
    public void Initialize(StatusCatalog catalog)
    {
        // 尊重检视面板覆盖（便于测试/特殊场景定制）。
        if (statusCatalogOverride != null)
        {
            _catalog = statusCatalogOverride;
            return;
        }

        _catalog = catalog;
    }

    /// <summary>
    /// 应用状态（外部入口）
    /// 参数 durationOverride：
    /// - >0：覆盖默认持续时间
    /// - <=0：使用默认持续时间（<=0 表示永久）
    /// </summary>
    public bool Apply(string statusId, float durationOverride = -1f)
    {
        if (string.IsNullOrWhiteSpace(statusId))
        {
            Debug.LogError("[StatusEffectController] Apply 失败：statusId 为空", this);
            return false;
        }

        StatusDefinition def = ResolveDefinition(statusId);
        float duration = ResolveDurationSeconds(def, durationOverride);

        // 已存在：按 stackRule 处理
        if (_active.TryGetValue(statusId, out StatusRuntimeState state))
        {
            switch (def.stackRule)
            {
                case StatusStackRule.Ignore:
                    return true;

                case StatusStackRule.Refresh:
                    state.remainingSeconds = duration;
                    break;

                case StatusStackRule.Add:
                    state.stacks = Mathf.Min(state.stacks + 1, Mathf.Max(1, def.maxStacks));
                    state.remainingSeconds = duration;
                    break;

                case StatusStackRule.Replace:
                    state.stacks = 1;
                    state.remainingSeconds = duration;
                    break;

                default:
                    Debug.LogError($"[StatusEffectController] 未支持的 stackRule: {def.stackRule} (statusId={statusId})", this);
                    return false;
            }

            state.definition = def;
            _active[statusId] = state;
        }
        else
        {
            // 新增
            state = new StatusRuntimeState
            {
                definition = def,
                stacks = 1,
                remainingSeconds = duration
            };

            _active.Add(statusId, state);
            _activeList.Add(statusId);
        }

        ApplyToStats(statusId, state);
        OnStatusApplied?.Invoke(statusId, state.stacks);
        return true;
    }

    public bool HasStatus(string statusId)
    {
        if (string.IsNullOrWhiteSpace(statusId))
        {
            return false;
        }

        return _active.ContainsKey(statusId);
    }

    public int GetStacks(string statusId)
    {
        if (!_active.TryGetValue(statusId, out var state))
        {
            return 0;
        }

        return state.stacks;
    }

    public float GetRemainingSeconds(string statusId)
    {
        if (!_active.TryGetValue(statusId, out var state))
        {
            return 0f;
        }

        if (float.IsPositiveInfinity(state.remainingSeconds))
        {
            return float.PositiveInfinity;
        }

        return Mathf.Max(0f, state.remainingSeconds);
    }

    public bool Remove(string statusId)
    {
        if (string.IsNullOrWhiteSpace(statusId))
        {
            return false;
        }

        if (!_active.Remove(statusId))
        {
            return false;
        }

        _activeList.Remove(statusId);
        if (EnsureStats())
        {
            _stats.ClearMoveSpeedMultiplier(statusId);
        }
        OnStatusRemoved?.Invoke(statusId);
        return true;
    }

    private void Update()
    {
        Tick(Time.deltaTime);
    }

    /// <summary>
    /// 手动推进状态计时（便于测试/回放）
    /// </summary>
    public void Tick(float deltaTime)
    {
        if (_active.Count == 0 || deltaTime <= 0f)
        {
            return;
        }

        _expireBuffer.Clear();

        for (int i = 0; i < _activeList.Count; i++)
        {
            string statusId = _activeList[i];
            if (!_active.TryGetValue(statusId, out StatusRuntimeState state))
            {
                continue;
            }

            if (float.IsPositiveInfinity(state.remainingSeconds))
            {
                continue; // 永久状态
            }

            state.remainingSeconds -= deltaTime;
            if (state.remainingSeconds <= 0f)
            {
                _expireBuffer.Add(statusId);
            }
            else
            {
                _active[statusId] = state;
            }
        }

        // 统一移除（避免修改字典的同时枚举）
        for (int i = 0; i < _expireBuffer.Count; i++)
        {
            string statusId = _expireBuffer[i];
            if (_active.Remove(statusId))
            {
                _activeList.Remove(statusId);
                if (EnsureStats())
                {
                    _stats.ClearMoveSpeedMultiplier(statusId);
                }
                OnStatusExpired?.Invoke(statusId);
            }
        }
    }

    private StatusDefinition ResolveDefinition(string statusId)
    {
        StatusCatalog catalog = GetCatalog();
        if (catalog != null && catalog.IsValid && catalog.TryGetStatus(statusId, out StatusDefinition def) && def != null)
        {
            return def;
        }

        // 兜底：用于容错/测试（正常情况下应由 Import 阶段校验保证存在）
        if (_fallbackDefinitions.TryGetValue(statusId, out StatusDefinition cached))
        {
            return cached;
        }

        var fallback = new StatusDefinition
        {
            id = statusId,
            displayName = statusId,
            defaultDuration = 0f,
            stackRule = StatusStackRule.Replace,
            maxStacks = 1,
            modifiers = StatusModifiers.Default
        };

        _fallbackDefinitions[statusId] = fallback;
        return fallback;
    }

    private StatusCatalog GetCatalog()
    {
        if (_catalog != null)
        {
            return _catalog;
        }

        if (statusCatalogOverride != null)
        {
            _catalog = statusCatalogOverride;
            return _catalog;
        }

        // 兜底：把资源加载使用点限制在 Provider 内（P1-3）。
        _catalog = _fallbackAssets != null ? _fallbackAssets.StatusCatalog : null;
        if (_catalog == null && !_loggedMissingCatalog)
        {
            Debug.LogWarning("[StatusEffectController] StatusCatalog 未找到（Resources/Config/StatusCatalog.asset）。ApplyStatus 将以 fallback 定义执行（无 modifiers）。", this);
            _loggedMissingCatalog = true;
        }

        return _catalog;
    }

    private static float ResolveDurationSeconds(StatusDefinition def, float durationOverride)
    {
        float duration = durationOverride > 0f ? durationOverride : (def != null ? def.defaultDuration : 0f);

        // <=0：视为永久
        if (duration <= 0f)
        {
            return float.PositiveInfinity;
        }

        return duration;
    }

    private void ApplyToStats(string statusId, StatusRuntimeState state)
    {
        if (!EnsureStats())
        {
            return;
        }

        float perStack = state.definition != null ? state.definition.modifiers.moveSpeedMultiplier : 1f;
        perStack = Mathf.Max(0f, perStack);

        float total = Mathf.Pow(perStack, Mathf.Max(1, state.stacks));
        _stats.SetMoveSpeedMultiplier(statusId, total);
    }

    private struct StatusRuntimeState
    {
        public StatusDefinition definition;
        public int stacks;
        public float remainingSeconds; // Infinity 表示永久
    }
}
