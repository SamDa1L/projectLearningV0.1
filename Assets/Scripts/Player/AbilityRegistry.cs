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
/// - 若 AbilityCatalogEntry.kind 为 BuiltinDefault，但 paramsJson 中包含 legacy kind，则以 legacy kind 为准（兼容旧产物）
/// - Projectile 支持两条路径：
///   1) 结构化：projectileId → AbilityCatalog.projectiles → AbilityProjectileController
///   2) legacy：paramsJson.projectile.prefabPath → 仅实例化 prefab（由 prefab 自身 Projectile 脚本结算）
/// </summary>
public static class AbilityRegistry
{
    private const string KindKey = "kind";

    private const string ProjectileKey = "projectile";
    private const string ProjectilePrefabPathKey = "prefabPath";

    public const string KindBuiltinDefault = "BuiltinDefault";
    public const string KindProjectile = "Projectile";
    public const string KindStatModifier = "StatModifier";

    private static readonly Dictionary<string, Func<AbilityCatalogEntry, PlayerController, AbilityCatalog, Dictionary<string, object>, IPlayerAbility>> Factories
        = new Dictionary<string, Func<AbilityCatalogEntry, PlayerController, AbilityCatalog, Dictionary<string, object>, IPlayerAbility>>(StringComparer.OrdinalIgnoreCase)
        {
            { KindBuiltinDefault, CreateBuiltinDefaultAbility },
            { KindProjectile, CreateProjectileAbility },
            { KindStatModifier, CreateStatModifierAbility }
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

        // 0.5：优先使用结构化 kind（兼容旧产物：如果 kind=BuiltinDefault 但 paramsJson 内含 kind，则以 paramsJson 为准）
        string kind = NormalizeKind(entry.kind.ToString());
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
        if (entry.kind == AbilityKind.Projectile && catalog != null && !string.IsNullOrWhiteSpace(entry.projectileId))
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

            Debug.LogError($"[AbilityRegistry] Projectile kind missing projectile definition for projectileId='{entry.projectileId}' (abilityId='{entry.id}'), fallback to legacy paramsJson or BuiltinDefault");
        }

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
}
