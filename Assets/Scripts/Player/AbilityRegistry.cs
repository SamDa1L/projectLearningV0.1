using System;
using System.Collections.Generic;
using UnityEngine;
using CastleDB.Runtime;

/// <summary>
/// 能力注册表和工厂（阶段 3B）
///
/// 职责：
/// - 基于 AbilityCatalogEntry + paramsJson.kind 创建能力实例（Factory）
/// - 提供"已注册能力 kind"的权威列表（Import/Runtime 共用）
///
/// 设计约束（0.2）：
/// - Import 期 kind 校验与 Runtime Factory 必须共用同一份列表
/// - 新增“能力条目”（Ability.id）应尽量只通过数据扩展；新增“能力类型”（kind）才需要扩展工厂
/// </summary>
public static class AbilityRegistry
{
    /// <summary>
    /// paramsJson 中的能力类型字段名
    /// </summary>
    private const string KindKey = "kind";

    /// <summary>
    /// 内建默认能力类型（兼容 0.4：paramsJson 为空时使用）
    ///
    /// 语义：根据 hookType 创建对应的 Default*Ability（Move/Run/Jump/Attack/RangedAttack）
    /// </summary>
    public const string KindBuiltinDefault = "BuiltinDefault";

    /// <summary>
    /// ability kind → factory 映射（权威来源，Import/Runtime 共用）
    /// </summary>
    private static readonly Dictionary<string, Func<AbilityCatalogEntry, PlayerController, Dictionary<string, object>, IPlayerAbility>> _factories
        = new Dictionary<string, Func<AbilityCatalogEntry, PlayerController, Dictionary<string, object>, IPlayerAbility>>(StringComparer.OrdinalIgnoreCase)
        {
            { KindBuiltinDefault, CreateBuiltinDefaultAbility }
        };

    /// <summary>
    /// 检查 kind 是否已注册（用于 Import 校验）
    /// 说明：null/空白视为内建默认 kind（兼容旧数据）。
    /// </summary>
    public static bool IsKindRegistered(string kind)
    {
        string normalized = NormalizeKind(kind);
        return _factories.ContainsKey(normalized);
    }

    /// <summary>
    /// 获取所有已注册的 kind（用于 Import 校验日志）
    /// </summary>
    public static IEnumerable<string> GetAllRegisteredKinds()
    {
        return _factories.Keys;
    }

    /// <summary>
    /// 从 paramsJson 解析 kind（用于 Import 校验与 Runtime Factory 共用）
    /// 规则：
    /// - paramsJson 为空：返回 BuiltinDefault（兼容 0.4 旧数据）
    /// - paramsJson 非空：必须是 JSON 对象，且必须包含非空 kind 字段
    /// </summary>
    public static bool TryGetKindFromParamsJson(string paramsJson, out string kind, out string error)
    {
        kind = null;
        error = null;

        // 兼容旧数据：空 paramsJson 视为内建默认能力
        if (string.IsNullOrWhiteSpace(paramsJson))
        {
            kind = KindBuiltinDefault;
            return true;
        }

        Dictionary<string, object> obj = CastleDbJsonUtil.TryParseJsonObject(paramsJson);
        if (obj == null)
        {
            error = "paramsJson 必须是 JSON 对象 ({...})";
            return false;
        }

        if (!obj.TryGetValue(KindKey, out object kindObj) || kindObj == null)
        {
            error = $"paramsJson 缺少必填字段 '{KindKey}'";
            return false;
        }

        kind = kindObj.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(kind))
        {
            error = $"paramsJson 字段 '{KindKey}' 不能为空";
            return false;
        }

        kind = NormalizeKind(kind);
        return true;
    }

    /// <summary>
    /// 根据 AbilityCatalogEntry 创建能力实例（Factory）
    ///
    /// 规则：
    /// - id 仍是唯一键（AbilitySystem 以 AbilityId 作为控制入口）
    /// - 未识别的 kind：Import 阶段应报错；Runtime 侧也会输出 Error 并返回 null
    /// </summary>
    public static IPlayerAbility CreateAbility(AbilityCatalogEntry entry, PlayerController playerController)
    {
        if (entry == null)
        {
            Debug.LogError("[AbilityRegistry] CreateAbility failed: entry is null");
            return null;
        }

        if (playerController == null)
        {
            Debug.LogError($"[AbilityRegistry] CreateAbility failed: playerController is null for id '{entry.id}'");
            return null;
        }

        if (string.IsNullOrWhiteSpace(entry.id))
        {
            Debug.LogError("[AbilityRegistry] CreateAbility failed: entry.id is null/empty");
            return null;
        }

        // 解析 kind（空 paramsJson 走内建默认能力）
        string kind;
        string parseError;
        if (!TryGetKindFromParamsJson(entry.paramsJson, out kind, out parseError))
        {
            Debug.LogError($"[AbilityRegistry] CreateAbility failed: id='{entry.id}', paramsJson 解析失败: {parseError}");
            return null;
        }

        // 如果 paramsJson 非空，但 kind 未注册：直接失败（不允许静默降级）
        if (!IsKindRegistered(kind))
        {
            Debug.LogError($"[AbilityRegistry] CreateAbility failed: id='{entry.id}', kind='{kind}' 未注册。已注册 kinds: {string.Join(", ", _factories.Keys)}");
            return null;
        }

        try
        {
            // 只有当 paramsJson 非空时，才传入对象（避免重复解析/避免为旧数据制造空对象）
            Dictionary<string, object> paramsObj = null;
            if (!string.IsNullOrWhiteSpace(entry.paramsJson))
            {
                paramsObj = CastleDbJsonUtil.TryParseJsonObject(entry.paramsJson);
            }

            return _factories[kind].Invoke(entry, playerController, paramsObj);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[AbilityRegistry] CreateAbility exception for id='{entry.id}', kind='{kind}': {ex.Message}\n{ex.StackTrace}");
            return null;
        }
    }

    private static string NormalizeKind(string kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            return KindBuiltinDefault;
        }

        return kind.Trim();
    }

    private static IPlayerAbility CreateBuiltinDefaultAbility(AbilityCatalogEntry entry, PlayerController playerController, Dictionary<string, object> _)
    {
        // 兼容旧能力：按 hookType 创建 Default*Ability
        switch (entry.hookType)
        {
            case AbilityHookType.Move:
                return new DefaultMoveAbility(playerController, entry.id, entry.priority, entry.enabled);
            case AbilityHookType.Run:
                return new DefaultRunAbility(playerController, entry.id, entry.priority, entry.enabled);
            case AbilityHookType.Jump:
                return new DefaultJumpAbility(playerController, entry.id, entry.priority, entry.enabled);
            case AbilityHookType.Attack:
                return new DefaultAttackAbility(playerController, entry.id, entry.priority, entry.enabled);
            case AbilityHookType.RangedAttack:
                return new DefaultRangedAttackAbility(playerController, entry.id, entry.priority, entry.enabled);
            default:
                Debug.LogError($"[AbilityRegistry] BuiltinDefault 不支持的 hookType: {entry.hookType} (id={entry.id})");
                return null;
        }
    }
}
