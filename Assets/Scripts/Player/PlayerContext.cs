using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 玩家上下文组件
/// 统一暴露玩家相关的所有服务引用（单人项目口径）
///
/// 规范（契约 [C-Runtime-4a]）：
/// - 挂载位置：Player 根 GameObject
/// - 装配时机：Awake/OnEnable 完成自身引用缓存（仅 GetComponent，不查询 CastleDbService）
/// - 必需依赖：Inventory、Damageable、AbilitySystem（缺失则 Error 并设置 InteractionEnabled=false）
/// - 建议依赖：ReplaceController（缺失则 Error 一次性，仅禁用 Replace 功能）
/// - 可选依赖：EquipmentController、PlayerInput
/// - HudRefs/HudPresenter 由 GameBootstrap 装配，不在此处完成
/// </summary>
public class PlayerContext : MonoBehaviour
{
    // ===== 必需依赖 =====
    /// <summary>
    /// 玩家背包系统（必需）
    /// </summary>
    public PlayerInventory Inventory { get; private set; }

    /// <summary>
    /// 伤害系统（必需）
    /// </summary>
    public Damageable Damageable { get; private set; }

    /// <summary>
    /// 能力调度系统（必需）
    /// </summary>
    public AbilitySystem AbilitySystem { get; private set; }

    // ===== 建议依赖 =====
    /// <summary>
    /// 替换控制器（建议存在，缺失时仅禁用 Replace 功能）
    /// </summary>
    public ReplaceController ReplaceController { get; private set; }

    // ===== 可选依赖 =====
    /// <summary>
    /// 装备控制器（可选）
    /// </summary>
    public PlayerEquipmentController EquipmentController { get; private set; }

    /// <summary>
    /// 输入系统（可选）
    /// </summary>
    public PlayerInput PlayerInput { get; private set; }

    // ===== 状态标记 =====
    /// <summary>
    /// 交互是否启用
    /// 仅当必需依赖（Inventory/Damageable/AbilitySystem）缺失时置为 false
    /// </summary>
    public bool InteractionEnabled { get; private set; } = true;

    // ===== 一次性日志去重 =====
    private static readonly HashSet<string> _loggedWarnings = new HashSet<string>();

    // ===== 生命周期 =====
    private void Awake()
    {
        CacheDependencies();
    }

    /// <summary>
    /// 缓存所有依赖引用
    /// </summary>
    private void CacheDependencies()
    {
        // 必需依赖
        Inventory = GetComponent<PlayerInventory>();
        Damageable = GetComponent<Damageable>();

        // AbilitySystem 不是 MonoBehaviour，需要特殊处理
        // 假设它通过某个管理类持有，这里先标记为 null（需要外部注入或通过 AbilityRegistry 获取）
        // 根据项目架构，可能需要调整获取方式
        // AbilitySystem = GetComponent<AbilitySystemHolder>()?.System;

        // 建议依赖
        ReplaceController = GetComponent<ReplaceController>();

        // 可选依赖
        EquipmentController = GetComponent<PlayerEquipmentController>();
        PlayerInput = GetComponent<PlayerInput>();

        // 校验必需依赖
        ValidateDependencies();
    }

    /// <summary>
    /// 校验必需依赖
    /// </summary>
    private void ValidateDependencies()
    {
        // 硬缺失（组件层面）：Inventory 和 Damageable 应该在 Awake 时就存在
        bool hasHardMissingDependencies = false;

        // 校验 Inventory（硬缺失）
        if (Inventory == null)
        {
            Debug.LogError($"[PlayerContext] 缺少必需依赖：PlayerInventory", this);
            hasHardMissingDependencies = true;
        }

        // 校验 Damageable（硬缺失）
        if (Damageable == null)
        {
            Debug.LogError($"[PlayerContext] 缺少必需依赖：Damageable", this);
            hasHardMissingDependencies = true;
        }

        // 校验 AbilitySystem（等待注入）
        // AbilitySystem 不是 MonoBehaviour，需要外部通过 SetAbilitySystem 注入
        // Awake 阶段为 null 是预期行为，仅 Warning 不报 Error
        bool abilitySystemNotReady = false;
        if (AbilitySystem == null)
        {
            string key = "PlayerContext_MissingAbilitySystem_Awake";
            if (!_loggedWarnings.Contains(key))
            {
                Debug.LogWarning($"[PlayerContext] AbilitySystem 尚未注入（需要外部调用 SetAbilitySystem），交互功能将在注入前被禁用", this);
                _loggedWarnings.Add(key);
            }
            abilitySystemNotReady = true;
        }

        // 校验 ReplaceController（建议依赖）
        if (ReplaceController == null)
        {
            string key = "PlayerContext_MissingReplaceController";
            if (!_loggedWarnings.Contains(key))
            {
                Debug.LogError($"[PlayerContext] 缺少建议依赖：ReplaceController（Replace 功能将被禁用）", this);
                _loggedWarnings.Add(key);
            }
        }

        // 设置 InteractionEnabled
        // 只有硬缺失或 AbilitySystem 未就绪时才禁用交互
        if (hasHardMissingDependencies || abilitySystemNotReady)
        {
            InteractionEnabled = false;

            // 仅硬缺失时输出 Error，等待注入时不输出
            if (hasHardMissingDependencies)
            {
                Debug.LogError($"[PlayerContext] 必需组件缺失（Inventory/Damageable），交互功能已禁用", this);
            }
        }
    }

    /// <summary>
    /// 外部设置 AbilitySystem
    /// 由于 AbilitySystem 不是 MonoBehaviour，需要外部注入
    /// </summary>
    public void SetAbilitySystem(AbilitySystem abilitySystem)
    {
        if (AbilitySystem != null)
        {
            Debug.LogWarning($"[PlayerContext] AbilitySystem 已存在，不允许重复设置");
            return;
        }

        AbilitySystem = abilitySystem;

        // 重新校验并更新 InteractionEnabled
        if (AbilitySystem == null)
        {
            Debug.LogError($"[PlayerContext] SetAbilitySystem 传入空引用，交互功能已禁用", this);
            InteractionEnabled = false;
        }
        else
        {
            // 检查所有必需依赖是否都已满足
            bool allDependenciesMet = Inventory != null && Damageable != null && AbilitySystem != null;
            if (allDependenciesMet)
            {
                InteractionEnabled = true;
                Debug.Log($"[PlayerContext] AbilitySystem 注入成功，所有必需依赖已满足，交互功能已启用");
            }
            else
            {
                Debug.LogWarning($"[PlayerContext] AbilitySystem 已注入，但其他必需依赖仍缺失，交互功能保持禁用");
            }
        }
    }
}
