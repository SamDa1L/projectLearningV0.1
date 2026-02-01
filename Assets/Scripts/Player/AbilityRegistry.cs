using System;
using System.Collections.Generic;
using UnityEngine;
using CastleDB.Runtime;

/// <summary>
/// 能力注册表和工厂（阶段 3B）
///
/// 职责：
/// - 基于 AbilityCatalogEntry.kind 创建能力实例（Factory）
/// - 提供"已注册能力 kind"的权威列表（Import/Runtime 共用）
///
/// 兼容：
/// - 默认仅使用结构化字段（kind/projectileId/summonId 等），避免运行时隐式走旧链路。
/// - 如需兼容旧产物（从 paramsJson 读取 kind/projectile/summon 等），可在 PlayerSettings 的 Scripting Define Symbols 中添加 `LEGACY_CDB_PARAMS`。
/// - Projectile 的 legacy 路径：paramsJson.projectile.prefabPath → 仅实例化 prefab（由 prefab 自身 Projectile 脚本结算）。
/// </summary>
public static class AbilityRegistry
{
    private const string KindKey = "kind";

    private const string ProjectileKey = "projectile";
    private const string ProjectilePrefabPathKey = "prefabPath";

    public const string KindBuiltinDefault = "BuiltinDefault";
    public const string KindProjectile = "Projectile";
    public const string KindStatModifier = "StatModifier";
    public const string KindBuff = "Buff";
    public const string KindDash = "Dash";
    public const string KindSummon = "Summon";
    public const string KindAttackOverride = "AttackOverride";

    private static readonly Dictionary<string, Func<AbilityCatalogEntry, PlayerController, AbilityCatalog, Dictionary<string, object>, IPlayerAbility>> Factories
        = new Dictionary<string, Func<AbilityCatalogEntry, PlayerController, AbilityCatalog, Dictionary<string, object>, IPlayerAbility>>(StringComparer.OrdinalIgnoreCase)
        {
            { KindBuiltinDefault, CreateBuiltinDefaultAbility },
            { KindProjectile, CreateProjectileAbility },
            { KindStatModifier, CreateStatModifierAbility },
            { KindBuff, CreateBuffAbility },
            { KindDash, CreateDashAbility },
            { KindSummon, CreateSummonAbility },
            { KindAttackOverride, CreateAttackOverrideAbility }
        };

    public static bool IsKindRegistered(string kind)
    {
        string normalized = NormalizeKind(kind);
        return Factories.ContainsKey(normalized);
    }

    public static bool IsKindRegistered(AbilityKind kind)
    {
        return IsKindRegistered(kind.ToString());
    }

    public static IEnumerable<string> GetAllRegisteredKinds()
    {
        return Factories.Keys;
    }

#if LEGACY_CDB_PARAMS
    /// <summary>
    /// Legacy：从 paramsJson 解析 kind（用于兼容旧产物）
    /// 规则：
    /// - paramsJson 为空：返回 BuiltinDefault
    /// - paramsJson 非空：必须是 JSON 对象且包含非空 kind 字段
    /// </summary>
    public static bool TryGetKindFromParamsJson(string paramsJson, out string kind, out string error)
    {
        kind = null;
        error = null;

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
#endif

    public static IPlayerAbility CreateAbility(AbilityCatalogEntry entry, PlayerController playerController, AbilityCatalog catalog)
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

        Dictionary<string, object> paramsObj = null;
        if (!string.IsNullOrWhiteSpace(entry.paramsJson))
        {
            paramsObj = CastleDbJsonUtil.TryParseJsonObject(entry.paramsJson);
        }

        // 0.5：优先使用结构化 kind（默认不从 paramsJson 推断 kind，避免旧产物掩盖导入错误）
        string kind = NormalizeKind(entry.kind.ToString());
#if LEGACY_CDB_PARAMS
        // 兼容旧产物：如果 kind=BuiltinDefault 但 paramsJson 内含 kind，则以 paramsJson 为准
        if (string.Equals(kind, KindBuiltinDefault, StringComparison.OrdinalIgnoreCase)
            && paramsObj != null
            && paramsObj.TryGetValue(KindKey, out object legacyKindObj)
            && legacyKindObj != null)
        {
            string legacyKind = legacyKindObj.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(legacyKind))
            {
                kind = NormalizeKind(legacyKind);
            }
        }
#endif

        if (!IsKindRegistered(kind))
        {
            Debug.LogError($"[AbilityRegistry] CreateAbility failed: kind '{kind}' not registered for id='{entry.id}'. " +
                $"Registered kinds=[{string.Join(", ", GetAllRegisteredKinds())}]");
            kind = KindBuiltinDefault; // 兜底：避免整个 AbilitySystem 构建硬失败
        }

        try
        {
            return Factories[kind].Invoke(entry, playerController, catalog, paramsObj);
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

    private static IPlayerAbility CreateBuiltinDefaultAbility(
        AbilityCatalogEntry entry,
        PlayerController playerController,
        AbilityCatalog _,
        Dictionary<string, object> __)
    {
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

    private static IPlayerAbility CreateProjectileAbility(
        AbilityCatalogEntry entry,
        PlayerController playerController,
        AbilityCatalog catalog,
        Dictionary<string, object> paramsObj)
    {
        if (entry.hookType != AbilityHookType.RangedAttack)
        {
            Debug.LogWarning($"[AbilityRegistry] Projectile kind used with hookType={entry.hookType} (id='{entry.id}'), fallback to BuiltinDefault");
            return CreateBuiltinDefaultAbility(entry, playerController, catalog, paramsObj);
        }

        // 0.5 结构化路径：projectileId → AbilityProjectileDefinition
        if (catalog != null && !string.IsNullOrWhiteSpace(entry.projectileId))
        {
            if (catalog.TryGetProjectile(entry.projectileId, out var def) && def != null)
            {
                AbilityOnHitSequenceDefinition onHitSeq = null;
                if (!string.IsNullOrWhiteSpace(entry.onHitSequenceId))
                {
                    catalog.TryGetOnHitSequence(entry.onHitSequenceId, out onHitSeq);
                }

                return new ProjectileRangedAttackAbility(
                    playerController,
                    entry.id,
                    entry.priority,
                    entry.enabled,
                    def,
                    entry.cooldown,
                    onHitSeq);
            }
        }

#if LEGACY_CDB_PARAMS
        // legacy：paramsJson.projectile.prefabPath
        if (paramsObj == null)
        {
            Debug.LogError($"[AbilityRegistry] Projectile kind requires paramsJson object (id='{entry.id}'), fallback to BuiltinDefault");
            return CreateBuiltinDefaultAbility(entry, playerController, catalog, paramsObj);
        }

        if (!paramsObj.TryGetValue(ProjectileKey, out object projectileObj) || projectileObj == null)
        {
            Debug.LogError($"[AbilityRegistry] Projectile kind missing '{ProjectileKey}' object (id='{entry.id}'), fallback to BuiltinDefault");
            return CreateBuiltinDefaultAbility(entry, playerController, catalog, paramsObj);
        }

        if (!(projectileObj is Dictionary<string, object> projectileDict))
        {
            Debug.LogError($"[AbilityRegistry] Projectile kind '{ProjectileKey}' must be a JSON object (id='{entry.id}'), fallback to BuiltinDefault");
            return CreateBuiltinDefaultAbility(entry, playerController, catalog, paramsObj);
        }

        if (!projectileDict.TryGetValue(ProjectilePrefabPathKey, out object prefabPathObj) || prefabPathObj == null)
        {
            Debug.LogError($"[AbilityRegistry] Projectile kind missing '{ProjectilePrefabPathKey}' (id='{entry.id}'), fallback to BuiltinDefault");
            return CreateBuiltinDefaultAbility(entry, playerController, catalog, paramsObj);
        }

        string prefabPath = prefabPathObj.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(prefabPath))
        {
            Debug.LogError($"[AbilityRegistry] Projectile kind '{ProjectilePrefabPathKey}' is empty (id='{entry.id}'), fallback to BuiltinDefault");
            return CreateBuiltinDefaultAbility(entry, playerController, catalog, paramsObj);
        }

        return new ProjectileRangedAttackAbility(playerController, entry.id, entry.priority, entry.enabled, prefabPath);
#else
        Debug.LogError($"[AbilityRegistry] Projectile kind 需要 projectileId + 定义（已关闭 LEGACY_CDB_PARAMS），回退 BuiltinDefault。abilityId='{entry.id}', projectileId='{entry.projectileId}'");
        return CreateBuiltinDefaultAbility(entry, playerController, catalog, paramsObj);
#endif
    }

    private static IPlayerAbility CreateStatModifierAbility(
        AbilityCatalogEntry entry,
        PlayerController playerController,
        AbilityCatalog catalog,
        Dictionary<string, object> _)
    {
        return new StatModifierPassiveAbility(
            playerController,
            entry.id,
            entry.priority,
            entry.enabled,
            catalog,
            entry.buffId);
    }

    private static IPlayerAbility CreateBuffAbility(
        AbilityCatalogEntry entry,
        PlayerController playerController,
        AbilityCatalog catalog,
        Dictionary<string, object> _)
    {
        return new ActiveBuffAbility(
            playerController,
            entry.id,
            entry.priority,
            entry.enabled,
            catalog,
            entry.buffId,
            entry.cooldown,
            entry.paramsJson);
    }

    private static IPlayerAbility CreateDashAbility(
        AbilityCatalogEntry entry,
        PlayerController playerController,
        AbilityCatalog _,
        Dictionary<string, object> __)
    {
        if (entry.hookType != AbilityHookType.Run)
        {
            Debug.LogWarning($"[AbilityRegistry] Dash kind used with hookType={entry.hookType} (id='{entry.id}'). Recommended hookType is Run.");
        }

        return new DashAbility(
            playerController,
            entry.id,
            entry.priority,
            entry.enabled,
            entry.cooldown,
            entry.paramsJson);
    }

    private static IPlayerAbility CreateSummonAbility(
        AbilityCatalogEntry entry,
        PlayerController playerController,
        AbilityCatalog catalog,
        Dictionary<string, object> __)
    {
        AbilitySummonDefinition def = null;
        bool hasStructuredDef = false;
        if (catalog != null && !string.IsNullOrWhiteSpace(entry.summonId))
        {
            hasStructuredDef = catalog.TryGetSummon(entry.summonId, out def) && def != null;
        }

#if LEGACY_CDB_PARAMS
        if (!hasStructuredDef)
        {
            if (string.IsNullOrWhiteSpace(entry.summonId))
            {
                Debug.LogError($"[AbilityRegistry] Summon kind missing summonId (abilityId='{entry.id}'), fallback to legacy paramsJson");
            }
            else if (catalog == null)
            {
                Debug.LogError($"[AbilityRegistry] Summon kind requires AbilityCatalog to resolve summonId='{entry.summonId}' (abilityId='{entry.id}'), fallback to legacy paramsJson");
            }
            else
            {
                Debug.LogError($"[AbilityRegistry] Summon kind missing summon definition for summonId='{entry.summonId}' (abilityId='{entry.id}'), fallback to legacy paramsJson");
            }
        }

        return new SummonAbility(
            playerController,
            entry.id,
            entry.priority,
            entry.enabled,
            def,
            entry.cooldown,
            entry.paramsJson);
#else
        if (!hasStructuredDef)
        {
            Debug.LogError($"[AbilityRegistry] Summon kind 需要 summonId + 定义（已关闭 LEGACY_CDB_PARAMS），回退 BuiltinDefault。abilityId='{entry.id}', summonId='{entry.summonId}'");
            return CreateBuiltinDefaultAbility(entry, playerController, catalog, null);
        }

        return new SummonAbility(
            playerController,
            entry.id,
            entry.priority,
            entry.enabled,
            def,
            entry.cooldown,
            entry.paramsJson);
#endif
    }

    private static IPlayerAbility CreateAttackOverrideAbility(
        AbilityCatalogEntry entry,
        PlayerController playerController,
        AbilityCatalog catalog,
        Dictionary<string, object> _)
    {
        if (catalog == null)
        {
            Debug.LogError($"[AbilityRegistry] AttackOverride requires AbilityCatalog (abilityId='{entry.id}'), fallback to BuiltinDefault");
            return CreateBuiltinDefaultAbility(entry, playerController, catalog, null);
        }

        if (string.IsNullOrWhiteSpace(entry.projectileId))
        {
            Debug.LogError($"[AbilityRegistry] AttackOverride requires projectileId (abilityId='{entry.id}'), fallback to BuiltinDefault");
            return CreateBuiltinDefaultAbility(entry, playerController, catalog, null);
        }

        if (!catalog.TryGetProjectile(entry.projectileId, out var def) || def == null)
        {
            Debug.LogError($"[AbilityRegistry] AttackOverride missing projectile definition for projectileId='{entry.projectileId}' (abilityId='{entry.id}'), fallback to BuiltinDefault");
            return CreateBuiltinDefaultAbility(entry, playerController, catalog, null);
        }

        AbilityOnHitSequenceDefinition onHitSeq = null;
        if (!string.IsNullOrWhiteSpace(entry.onHitSequenceId))
        {
            catalog.TryGetOnHitSequence(entry.onHitSequenceId, out onHitSeq);
        }

        return new AttackOverrideAbility(
            playerController,
            entry.id,
            entry.priority,
            entry.enabled,
            def,
            entry.cooldown,
            onHitSeq,
            entry.paramsJson);
    }
}
