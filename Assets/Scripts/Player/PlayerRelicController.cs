using System;
using System.Collections.Generic;
using UnityEngine;
using CastleDB.Runtime;

/// <summary>
/// 遗物控制器（Phase 7）
/// - 负责：拾取后自动装备、死亡/丢弃清理、护盾伤害拦截与冷却重生状态机
/// - 约束：不使用 Tag/Find/单例；由 GameBootstrap -> PlayerContext 注入依赖
/// </summary>
public sealed class PlayerRelicController : MonoBehaviour, IDamageInterceptor
{
    public event Action<string, string> OnRelicChanged;

    public string EquippedRelicItemId => _equippedRelicItemId;

    public int ShieldHp => _shieldHp;
    public int ShieldMaxHp => _shieldParams.shieldMaxHp;

    private ICastleDbService _items;
    private RelicCatalog _relicCatalog;
    private Damageable _damageable;

    private bool _initialized;

    private string _equippedRelicItemId;
    private RelicDefinition _equippedRelic;

    private enum ShieldState
    {
        None = 0,
        Active = 1,
        Cooldown = 2,
        RegenDelay = 3
    }

    private struct ShieldParams
    {
        public int shieldMaxHp;
        public float regenCooldown;
        public float regenDelay;
        public string breakVfxPath;
        public float breakVfxDuration;
        public string regenVfxPath;
        public float regenVfxDuration;
    }

    private ShieldParams _shieldParams;
    private ShieldState _shieldState = ShieldState.None;
    private int _shieldHp = 0;
    private float _nextStateTime = 0f;

    public void Initialize(ICastleDbService items, RelicCatalog relicCatalog, Damageable damageable)
    {
        if (_initialized)
        {
            Debug.LogWarning("[PlayerRelicController] 已初始化，忽略重复 Initialize", this);
            return;
        }

        if (items == null)
        {
            Debug.LogError("[PlayerRelicController] Initialize 失败：items 为空", this);
            return;
        }

        if (relicCatalog == null)
        {
            Debug.LogWarning("[PlayerRelicController] Initialize：RelicCatalog 为空，将禁用遗物功能", this);
        }

        if (damageable == null)
        {
            Debug.LogError("[PlayerRelicController] Initialize 失败：damageable 为空", this);
            return;
        }

        _items = items;
        _relicCatalog = relicCatalog;
        _damageable = damageable;
        _initialized = true;

        // 订阅死亡事件：死亡后清理遗物效果
        _damageable.damageableDeath.AddListener(OnPlayerDeath);
    }

    private void OnDestroy()
    {
        if (_damageable != null)
        {
            _damageable.damageableDeath.RemoveListener(OnPlayerDeath);
        }
    }

    private void Update()
    {
        TickShieldStateMachine();
    }

    private void OnPlayerDeath()
    {
        ClearEquippedRelic();
    }

    public PickupResult TryPickupRelic(PickupRequest req)
    {
        if (!_initialized)
        {
            Debug.LogError("[PlayerRelicController] 未初始化，禁止拾取遗物", this);
            return PickupResult.Failed_NotSupported;
        }

        if (string.IsNullOrWhiteSpace(req.itemId))
        {
            return PickupResult.Failed_InvalidItemId;
        }

        if (req.amount != 1)
        {
            return PickupResult.Failed_NotSupported;
        }

        if (!_items.TryGetItem(req.itemId, out ItemDefinition itemDef) || itemDef == null)
        {
            return PickupResult.Failed_InvalidItemId;
        }

        if (itemDef.itemType != ItemType.Relic)
        {
            return PickupResult.Failed_NotSupported;
        }

        if (string.IsNullOrWhiteSpace(itemDef.relicId))
        {
            Debug.LogError($"[PlayerRelicController] 物品缺少 relicId：itemId={req.itemId}", this);
            return PickupResult.Failed_NotSupported;
        }

        if (_relicCatalog == null)
        {
            Debug.LogWarning("[PlayerRelicController] 未注入 RelicCatalog，无法拾取遗物", this);
            return PickupResult.Failed_NotSupported;
        }

        if (!_relicCatalog.TryGetRelic(itemDef.relicId, out RelicDefinition relicDef) || relicDef == null)
        {
            Debug.LogError($"[PlayerRelicController] relicId 在 RelicCatalog 中不存在：relicId={itemDef.relicId}, itemId={req.itemId}", this);
            return PickupResult.Failed_NotSupported;
        }

        EquipRelic(req.itemId, relicDef);
        return PickupResult.Success;
    }

    public void ClearEquippedRelic()
    {
        if (string.IsNullOrEmpty(_equippedRelicItemId))
        {
            return;
        }

        string oldItemId = _equippedRelicItemId;
        _equippedRelicItemId = null;
        _equippedRelic = null;

        _shieldParams = default;
        _shieldHp = 0;
        _shieldState = ShieldState.None;
        _nextStateTime = 0f;

        OnRelicChanged?.Invoke(oldItemId, null);
    }

    private void EquipRelic(string itemId, RelicDefinition relicDef)
    {
        string oldItemId = _equippedRelicItemId;

        // 最小实现：单槽位，直接覆盖旧遗物（旧效果立即移除）
        _equippedRelicItemId = itemId;
        _equippedRelic = relicDef;

        if (relicDef.kind == RelicKind.Shield)
        {
            if (!TryParseShieldParams(relicDef.paramsJson, out _shieldParams))
            {
                Debug.LogError($"[PlayerRelicController] 解析 Shield paramsJson 失败：relicId={relicDef.id}", this);
                _shieldParams = default;
                _shieldHp = 0;
                _shieldState = ShieldState.None;
            }
            else
            {
                _shieldHp = Mathf.Max(_shieldParams.shieldMaxHp, 0);
                _shieldState = _shieldHp > 0 ? ShieldState.Active : ShieldState.None;
                _nextStateTime = 0f;
            }
        }
        else
        {
            // 未来扩展其它 kind
            _shieldParams = default;
            _shieldHp = 0;
            _shieldState = ShieldState.None;
        }

        OnRelicChanged?.Invoke(oldItemId, itemId);
    }

    public void BeforeDamage(ref int damage, Vector2 hitPoint)
    {
        if (!_initialized || _equippedRelic == null)
        {
            return;
        }

        if (_equippedRelic.kind != RelicKind.Shield)
        {
            return;
        }

        if (_shieldState != ShieldState.Active || _shieldHp <= 0 || damage <= 0)
        {
            return;
        }

        int absorb = Mathf.Min(_shieldHp, damage);
        _shieldHp -= absorb;
        damage -= absorb;

        if (_shieldHp <= 0)
        {
            OnShieldBroken(hitPoint);
        }
    }

    private void OnShieldBroken(Vector2 hitPoint)
    {
        _shieldHp = 0;

        // 破碎特效（可选）
        SpawnVfx(_shieldParams.breakVfxPath, _shieldParams.breakVfxDuration, hitPoint);

        float cooldown = Mathf.Max(_shieldParams.regenCooldown, 0f);
        _nextStateTime = Time.time + cooldown;
        _shieldState = ShieldState.Cooldown;
    }

    private void TickShieldStateMachine()
    {
        if (!_initialized || _equippedRelic == null)
        {
            return;
        }

        if (_equippedRelic.kind != RelicKind.Shield)
        {
            return;
        }

        if (_shieldState == ShieldState.Cooldown)
        {
            if (Time.time >= _nextStateTime)
            {
                float delay = Mathf.Max(_shieldParams.regenDelay, 0f);
                if (delay > 0f)
                {
                    _nextStateTime = Time.time + delay;
                    _shieldState = ShieldState.RegenDelay;
                }
                else
                {
                    RegenShield();
                }
            }
        }
        else if (_shieldState == ShieldState.RegenDelay)
        {
            if (Time.time >= _nextStateTime)
            {
                RegenShield();
            }
        }
    }

    private void RegenShield()
    {
        _shieldHp = Mathf.Max(_shieldParams.shieldMaxHp, 0);
        _shieldState = _shieldHp > 0 ? ShieldState.Active : ShieldState.None;
        _nextStateTime = 0f;

        // 重生特效（可选）
        SpawnVfx(_shieldParams.regenVfxPath, _shieldParams.regenVfxDuration, transform.position);
    }

    private void SpawnVfx(string prefabPath, float duration, Vector3 worldPos)
    {
        if (string.IsNullOrWhiteSpace(prefabPath))
        {
            return;
        }

        GameObject prefab = Resources.Load<GameObject>(prefabPath);
        if (prefab == null)
        {
            return;
        }

        GameObject instance = Instantiate(prefab, worldPos, prefab.transform.rotation);
        if (duration > 0f)
        {
            Destroy(instance, duration);
        }
    }

    private static bool TryParseShieldParams(string json, out ShieldParams p)
    {
        p = default;

        Dictionary<string, object> obj = CastleDbJsonUtil.TryParseJsonObject(json);
        if (obj == null)
        {
            return false;
        }

        if (!TryGetInt(obj, "shieldMaxHp", out p.shieldMaxHp))
        {
            return false;
        }

        p.regenCooldown = TryGetFloat(obj, "regenCooldown", 0f);
        p.regenDelay = TryGetFloat(obj, "regenDelay", 0f);
        p.breakVfxPath = TryGetString(obj, "breakVfxPath", "");
        p.breakVfxDuration = TryGetFloat(obj, "breakVfxDuration", 0f);
        p.regenVfxPath = TryGetString(obj, "regenVfxPath", "");
        p.regenVfxDuration = TryGetFloat(obj, "regenVfxDuration", 0f);
        return true;
    }

    private static bool TryGetInt(Dictionary<string, object> obj, string key, out int value)
    {
        value = 0;
        if (obj == null || !obj.TryGetValue(key, out var raw))
        {
            return false;
        }

        try
        {
            value = Convert.ToInt32(raw);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static float TryGetFloat(Dictionary<string, object> obj, string key, float defaultValue)
    {
        if (obj == null || !obj.TryGetValue(key, out var raw))
        {
            return defaultValue;
        }

        try
        {
            return Convert.ToSingle(raw);
        }
        catch
        {
            return defaultValue;
        }
    }

    private static string TryGetString(Dictionary<string, object> obj, string key, string defaultValue)
    {
        if (obj == null || !obj.TryGetValue(key, out var raw))
        {
            return defaultValue;
        }

        return raw?.ToString() ?? defaultValue;
    }
}

