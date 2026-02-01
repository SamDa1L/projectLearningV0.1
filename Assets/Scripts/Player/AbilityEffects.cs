using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Ability 事件点（Phase 1-3）
/// </summary>
public enum AbilityEventPoint
{
    OnCast,
    OnRelease,
    OnHit,
    OnExpire
}

/// <summary>
/// Ability Effect 类型（Phase 1-3 最小集）
/// </summary>
public enum AbilityEffectType
{
    Damage,
    ApplyStatus
}

/// <summary>
/// Ability Effect 规格（Phase 1-3）
/// 注意：此结构是运行时执行用的“结构化数据”，通常由 paramsJson 解析而来。
/// </summary>
public struct AbilityEffectSpec
{
    public AbilityEffectType type;

    // Damage
    public int damage;
    public Vector2 knockback;

    // ApplyStatus
    public string statusId;
    public float durationOverride;
}

/// <summary>
/// Ability Effect 执行器（Phase 1-3：事件 → Effect）
/// 约束：
/// - Runtime 不依赖 UnityEditor
/// - 错误日志必须可定位 abilityId/effectIndex
/// </summary>
public static class AbilityEffectExecutor
{
    public static void ExecuteOnHit(
        string abilityId,
        IReadOnlyList<AbilityEffectSpec> effects,
        GameObject caster,
        GameObject target,
        Vector2? hitPoint = null)
    {
        if (effects == null || effects.Count == 0)
        {
            return;
        }

        if (target == null)
        {
            Debug.LogError($"[AbilityEffectExecutor] ExecuteOnHit 失败：target 为空 (abilityId={abilityId})");
            return;
        }

        for (int i = 0; i < effects.Count; i++)
        {
            ExecuteSingle(AbilityEventPoint.OnHit, abilityId, i, effects[i], caster, target, hitPoint);
        }
    }

    private static void ExecuteSingle(
        AbilityEventPoint eventPoint,
        string abilityId,
        int effectIndex,
        AbilityEffectSpec spec,
        GameObject caster,
        GameObject target,
        Vector2? hitPoint)
    {
        try
        {
            switch (spec.type)
            {
                case AbilityEffectType.Damage:
                    ExecuteDamage(eventPoint, abilityId, effectIndex, spec, caster, target, hitPoint);
                    break;
                case AbilityEffectType.ApplyStatus:
                    ExecuteApplyStatus(eventPoint, abilityId, effectIndex, spec, caster, target);
                    break;
                default:
                    Debug.LogError($"[AbilityEffectExecutor] 未支持的 effect type: {spec.type} (abilityId={abilityId}, event={eventPoint}, effectIndex={effectIndex})");
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[AbilityEffectExecutor] 执行异常 (abilityId={abilityId}, event={eventPoint}, effectIndex={effectIndex}): {ex.Message}\n{ex.StackTrace}");
        }
    }

    private static void ExecuteDamage(
        AbilityEventPoint eventPoint,
        string abilityId,
        int effectIndex,
        AbilityEffectSpec spec,
        GameObject caster,
        GameObject target,
        Vector2? hitPoint)
    {
        if (spec.damage <= 0)
        {
            Debug.LogError($"[AbilityEffectExecutor] Damage 参数非法：damage={spec.damage} (abilityId={abilityId}, event={eventPoint}, effectIndex={effectIndex})");
            return;
        }

        Damageable damageable = target.GetComponent<Damageable>();
        if (damageable == null)
        {
            Debug.LogWarning($"[AbilityEffectExecutor] Damage 目标无 Damageable，跳过 (abilityId={abilityId}, event={eventPoint}, effectIndex={effectIndex}, target={target.name})");
            return;
        }

        bool gotHit = damageable.Hit(spec.damage, spec.knockback, hitPoint);
        if (!gotHit)
        {
            // 不作为 Error：目标可能无敌/已死亡，属于运行时可预期分支
            #if ABILITY_SYSTEM_DEBUG
            Debug.Log($"[AbilityEffectExecutor] Damage 未生效（目标可能无敌/已死亡） (abilityId={abilityId}, event={eventPoint}, effectIndex={effectIndex}, target={target.name})");
            #endif
        }
    }

    private static void ExecuteApplyStatus(
        AbilityEventPoint eventPoint,
        string abilityId,
        int effectIndex,
        AbilityEffectSpec spec,
        GameObject caster,
        GameObject target)
    {
        if (string.IsNullOrWhiteSpace(spec.statusId))
        {
            Debug.LogError($"[AbilityEffectExecutor] ApplyStatus 参数非法：statusId 为空 (abilityId={abilityId}, event={eventPoint}, effectIndex={effectIndex})");
            return;
        }

        StatusEffectController controller = target.GetComponent<StatusEffectController>();
        if (controller == null)
        {
            Debug.LogWarning($"[AbilityEffectExecutor] ApplyStatus 目标无 StatusEffectController，跳过 (abilityId={abilityId}, event={eventPoint}, effectIndex={effectIndex}, target={target.name})");
            return;
        }

        controller.Apply(spec.statusId, spec.durationOverride);
    }
}

/// <summary>
/// Ability paramsJson 解析辅助（Phase 1-3）
/// 解析 events.onHit[] → AbilityEffectSpec 列表。
/// </summary>
public static class AbilityEffectSpecParser
{
    private const string EventsKey = "events";
    private const string OnHitKey = "onHit";

    public static bool TryParseOnHitEffects(
        string abilityId,
        Dictionary<string, object> paramsObj,
        out List<AbilityEffectSpec> effects,
        out List<string> errors)
    {
        effects = new List<AbilityEffectSpec>();
        errors = new List<string>();

        if (paramsObj == null)
        {
            // paramsJson 为空或未解析：视为无事件配置（不报错）
            return true;
        }

        if (!paramsObj.TryGetValue(EventsKey, out object eventsObj))
        {
            return true; // 无 events 配置
        }

        if (!(eventsObj is Dictionary<string, object> eventsDict))
        {
            errors.Add($"Ability '{abilityId}' 的 paramsJson.events 必须是对象 ({{...}})");
            return false;
        }

        if (!eventsDict.TryGetValue(OnHitKey, out object onHitObj) || onHitObj == null)
        {
            return true; // 无 onHit 配置
        }

        if (!(onHitObj is List<object> onHitList))
        {
            errors.Add($"Ability '{abilityId}' 的 paramsJson.events.onHit 必须是数组 ([...])");
            return false;
        }

        for (int i = 0; i < onHitList.Count; i++)
        {
            if (!(onHitList[i] is Dictionary<string, object> effectObj))
            {
                errors.Add($"Ability '{abilityId}' 的 onHit[{i}] 必须是对象 ({{...}})");
                continue;
            }

            if (!TryParseEffect(abilityId, i, effectObj, out AbilityEffectSpec spec, out string error))
            {
                errors.Add(error);
                continue;
            }

            effects.Add(spec);
        }

        return errors.Count == 0;
    }

    private static bool TryParseEffect(
        string abilityId,
        int effectIndex,
        Dictionary<string, object> obj,
        out AbilityEffectSpec spec,
        out string error)
    {
        spec = default;
        error = null;

        if (!obj.TryGetValue("type", out object typeObj) || typeObj == null)
        {
            error = $"Ability '{abilityId}' effect[{effectIndex}] 缺少必填字段 'type'";
            return false;
        }

        string typeStr = typeObj.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(typeStr))
        {
            error = $"Ability '{abilityId}' effect[{effectIndex}] 字段 'type' 不能为空";
            return false;
        }

        if (!TryParseEffectType(typeStr, out AbilityEffectType type))
        {
            error = $"Ability '{abilityId}' effect[{effectIndex}] 未支持的 type='{typeStr}'";
            return false;
        }

        spec.type = type;

        switch (type)
        {
            case AbilityEffectType.Damage:
                if (!TryGetInt(obj, "amount", out int amount))
                {
                    // 兼容字段名：damage
                    if (!TryGetInt(obj, "damage", out amount))
                    {
                        error = $"Ability '{abilityId}' Damage effect[{effectIndex}] 缺少必填字段 'amount'";
                        return false;
                    }
                }

                spec.damage = amount;

                // 可选字段：knockback
                spec.knockback = Vector2.zero;
                if (obj.TryGetValue("knockback", out object knockbackObj) && knockbackObj != null)
                {
                    if (TryParseVector2(knockbackObj, out Vector2 kb))
                    {
                        spec.knockback = kb;
                    }
                }
                else
                {
                    // 兼容字段名：knockbackX/knockbackY
                    if (TryGetFloat(obj, "knockbackX", out float kx) && TryGetFloat(obj, "knockbackY", out float ky))
                    {
                        spec.knockback = new Vector2(kx, ky);
                    }
                }

                return true;

            case AbilityEffectType.ApplyStatus:
                if (!TryGetString(obj, "statusId", out string statusId) || string.IsNullOrWhiteSpace(statusId))
                {
                    error = $"Ability '{abilityId}' ApplyStatus effect[{effectIndex}] 缺少必填字段 'statusId'";
                    return false;
                }

                spec.statusId = statusId;
                spec.durationOverride = -1f;

                // 可选字段：durationOverride / duration
                if (TryGetFloat(obj, "durationOverride", out float durationOverride))
                {
                    spec.durationOverride = durationOverride;
                }
                else if (TryGetFloat(obj, "duration", out float duration))
                {
                    spec.durationOverride = duration;
                }

                return true;

            default:
                error = $"Ability '{abilityId}' effect[{effectIndex}] 未支持的 type='{type}'";
                return false;
        }
    }

    private static bool TryParseEffectType(string typeStr, out AbilityEffectType type)
    {
        type = default;

        if (string.Equals(typeStr, "Damage", StringComparison.OrdinalIgnoreCase))
        {
            type = AbilityEffectType.Damage;
            return true;
        }

        if (string.Equals(typeStr, "ApplyStatus", StringComparison.OrdinalIgnoreCase))
        {
            type = AbilityEffectType.ApplyStatus;
            return true;
        }

        return false;
    }

    private static bool TryGetString(Dictionary<string, object> obj, string key, out string value)
    {
        value = null;
        if (!obj.TryGetValue(key, out object raw) || raw == null)
        {
            return false;
        }

        value = raw.ToString();
        return true;
    }

    private static bool TryGetInt(Dictionary<string, object> obj, string key, out int value)
    {
        value = 0;
        if (!obj.TryGetValue(key, out object raw) || raw == null)
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

    private static bool TryGetFloat(Dictionary<string, object> obj, string key, out float value)
    {
        value = 0f;
        if (!obj.TryGetValue(key, out object raw) || raw == null)
        {
            return false;
        }

        try
        {
            value = Convert.ToSingle(raw);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryParseVector2(object raw, out Vector2 value)
    {
        value = Vector2.zero;

        // 支持 {x:.., y:..}
        if (raw is Dictionary<string, object> dict)
        {
            if (TryGetFloat(dict, "x", out float x) && TryGetFloat(dict, "y", out float y))
            {
                value = new Vector2(x, y);
                return true;
            }
            return false;
        }

        // 支持 [x, y]
        if (raw is List<object> list && list.Count >= 2)
        {
            try
            {
                float x = Convert.ToSingle(list[0]);
                float y = Convert.ToSingle(list[1]);
                value = new Vector2(x, y);
                return true;
            }
            catch
            {
                return false;
            }
        }

        return false;
    }
}
