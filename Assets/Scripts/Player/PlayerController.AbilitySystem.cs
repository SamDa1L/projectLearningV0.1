using System;
using System.Collections.Generic;
using UnityEngine;

public partial class PlayerController
{
    /// <summary>
    /// 构建能力系统（阶段 3B）
    /// - 从 Resources 加载 AbilityCatalog
    /// - 根据 catalog 构建 AbilitySystem 并注册所有能力（包含 disabled 条目）
    /// - 如果某个 hookType 没有任何启用的能力，输出警告（但允许运行）
    /// - 如果 usePlayerConfigFromCastleDb=true 但 AbilityCatalog 缺失，抛出异常（硬失败）
    /// </summary>
    private void BuildAbilitySystem()
    {
        // 加载 AbilityCatalog
        AbilityCatalog catalog = ResourcesGameAssetProvider.Shared.AbilityCatalog;
        if (catalog == null)
        {
            // 硬失败：能力系统是必需的，不允许回退到旧逻辑
            string errorMsg = "[PlayerController] usePlayerConfigFromCastleDb=true 但未找到 AbilityCatalog (Resources/Config/AbilityCatalog.asset)，" +
                "能力系统构建失败。请运行 Tools/CastleDB/Import All 生成 AbilityCatalog，或将 usePlayerConfigFromCastleDb 设为 false。";
            Debug.LogError(errorMsg, this);
            throw new InvalidOperationException(errorMsg);
        }

        Debug.Log($"[PlayerController] 开始构建能力系统，共 {catalog.entries.Count} 个能力配置");

        // 创建 AbilitySystem 实例
        abilitySystem = new AbilitySystem();

        // 统计每个 hookType 的能力数量（用于验证覆盖率）
        Dictionary<AbilityHookType, int> hookTypeTotalCount = new Dictionary<AbilityHookType, int>();
        Dictionary<AbilityHookType, int> hookTypeEnabledCount = new Dictionary<AbilityHookType, int>();
        foreach (AbilityHookType hookType in Enum.GetValues(typeof(AbilityHookType)))
        {
            hookTypeTotalCount[hookType] = 0;
            hookTypeEnabledCount[hookType] = 0;
        }

        // 遍历 catalog，为每个能力创建实例并注册（disabled 也注册，仅初始 Enabled=false）
        int registeredTotalCount = 0;
        int registeredEnabledCount = 0;
        int registeredDisabledCount = 0;
        foreach (var entry in catalog.entries)
        {
            // 使用 AbilityRegistry 创建能力实例（阶段 1-2：配置驱动）
            IPlayerAbility ability = AbilityRegistry.CreateAbility(entry, this, catalog);

            if (ability == null)
            {
                // 能力系统语义要求：AbilityCatalog 中的条目必须都能创建实例（包含 disabled）
                // 否则后续 SetAbilityEnabled(abilityId, ...) 将无法生效，属于配置/导入错误。
                string errorMsg = $"[PlayerController] 能力创建失败: {entry.id} (hookType={entry.hookType})";
                Debug.LogError(errorMsg, this);
                throw new InvalidOperationException(errorMsg);
            }

            // 注册到 AbilitySystem
            abilitySystem.RegisterAbility(entry.hookType, ability);

            // 统计
            hookTypeTotalCount[entry.hookType]++;
            registeredTotalCount++;
            if (entry.enabled)
            {
                hookTypeEnabledCount[entry.hookType]++;
                registeredEnabledCount++;
            }
            else
            {
                registeredDisabledCount++;
            }

            Debug.Log($"[PlayerController] 已注册能力: {entry.id}, hookType={entry.hookType}, priority={entry.priority}, enabled={entry.enabled}");
        }

        // 验证覆盖率：
        // 1) 若 hookType 没有任何能力条目（总数=0）→ 输入永远无效
        // 2) 若 hookType 当前没有启用能力（启用数=0）→ 当前输入无效，但后续可通过拾取/装备启用
        foreach (var kvp in hookTypeTotalCount)
        {
            AbilityHookType hookType = kvp.Key;
            int totalCount = kvp.Value;
            int enabledCount = hookTypeEnabledCount[hookType];

            if (totalCount == 0)
            {
                Debug.LogWarning($"[PlayerController] hookType {hookType} 在 AbilityCatalog 中没有任何能力条目，输入将永远无效");
                continue;
            }

            if (enabledCount == 0)
            {
                Debug.LogWarning($"[PlayerController] hookType {hookType} 当前没有任何启用的能力（已注册 {totalCount} 个，但均为 disabled），输入当前无效，可通过拾取/装备启用");
            }
        }

        Debug.Log($"[PlayerController] 能力系统构建完成: 注册 {registeredTotalCount} 个（enabled {registeredEnabledCount} / disabled {registeredDisabledCount}）");

        // ===== 阶段 4 集成：注入 AbilitySystem 到 PlayerContext =====
        PlayerContext playerContext = GetComponent<PlayerContext>();
        if (playerContext != null)
        {
            playerContext.SetAbilitySystem(abilitySystem);
            Debug.Log("[PlayerController] 已注入 AbilitySystem 到 PlayerContext");
        }
        else
        {
            Debug.LogError("[PlayerController] 未找到 PlayerContext 组件，无法注入 AbilitySystem");
        }

        // ===== 阶段 4-6 集成说明 =====
        // 注意：玩家各模块初始化由 GameBootstrap 触发，但入口已收拢到 PlayerContext.InitializeModules（阶段 3，契约 [C-Runtime-0]）
        // 说明：PlayerController 仅负责创建 AbilitySystem 并注入到 PlayerContext
        // 说明：GameBootstrap.Awake 将完成以下装配：
        //   1. 加载 ItemCatalog 并创建 CastleDbService
        //   2. 加载 HudBinding 并实例化/定位 HUD
        //   3. player.InitializeModules(items, cfg, hudPresenter, hudRefs)
        // 说明：GameBootstrap.Start 将执行：
        //   - player.SyncInitialAbilities()
    }

    /// <summary>
    /// [已废弃 - 阶段 3A] 应用Projectile伤害覆盖
    ///
    /// 注意：此方法已废弃，不再使用。
    /// 阶段 3A 使用 prefab 级一次性赋值（在 CastleDB Import 时完成）。
    /// Projectile prefab 的 damage 字段在导入时已被正确设置，运行时不需要再修改。
    ///
    /// 保留此方法仅用于向后兼容，实际运行时不应调用。
    /// </summary>
    [Obsolete("使用 prefab 级一次性赋值，此方法已废弃", true)]
    public void ApplyProjectileDamageOverride(GameObject projectileObject, string resourcesPath)
    {
        // 此方法已废弃，不应调用
        Debug.LogWarning("[PlayerController] ApplyProjectileDamageOverride 已废弃，使用 prefab 级赋值");
    }

    /// <summary>
    /// LateUpdate 生命周期回调（阶段 5）
    /// 每帧在所有 Update 完成后调用，用于刷新 AbilitySystem 的待处理状态变更
    ///
    /// 功能:
    /// - 调用 AbilitySystem.FlushPendingChanges() 统一应用本帧累积的 Enable/Disable 操作
    /// - 保证状态变更的保序去重语义（同帧多次操作只应用最后一次）
    /// </summary>
    private void LateUpdate()
    {
        // 阶段 5：刷新能力系统的待处理状态变更
        if (usePlayerConfigFromCastleDb && abilitySystem != null)
        {
            abilitySystem.FlushPendingChanges();
        }
    }

    public bool QueueAbilityRelease(string abilityId, Action releaseAction, float expirySeconds = 1.5f)
    {
        if (releaseAction == null)
        {
            Debug.LogError(
                $"[PlayerController] QueueAbilityRelease failed: releaseAction is null (abilityId='{abilityId ?? ""}')",
                this);
            return false;
        }

        _pendingAbilityRelease = new PendingAbilityRelease
        {
            hasRequest = true,
            expiresAt = Time.time + Mathf.Max(0.05f, expirySeconds),
            abilityId = abilityId ?? "",
            releaseAction = releaseAction
        };

        return true;
    }

    public void OnAbilityRelease()
    {
        if (!_pendingAbilityRelease.hasRequest)
        {
            return;
        }

        if (Time.time > _pendingAbilityRelease.expiresAt)
        {
            _pendingAbilityRelease.hasRequest = false;
            _pendingAbilityRelease.releaseAction = null;
            return;
        }

        Action releaseAction = _pendingAbilityRelease.releaseAction;
        string abilityId = _pendingAbilityRelease.abilityId;
        _pendingAbilityRelease.hasRequest = false;
        _pendingAbilityRelease.releaseAction = null;

        try
        {
            releaseAction?.Invoke();
        }
        catch (Exception ex)
        {
            Debug.LogError(
                $"[PlayerController] OnAbilityRelease exception (abilityId='{abilityId}'): {ex.Message}\n{ex.StackTrace}",
                this);
        }
    }
}
