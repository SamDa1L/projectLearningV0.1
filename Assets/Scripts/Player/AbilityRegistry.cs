using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 能力注册表和工厂（阶段 3B）
///
/// 职责：
/// - 提供"已注册能力 ID"的权威列表（Import/Runtime 共用）
/// - 根据 ID + PlayerController 创建能力实例（Factory）
///
/// 设计约束（0.2）：
/// - Import 期 Registry 校验与 Runtime Factory 必须共用同一份列表
/// - 0.2 采用硬编码注册（5 个基础能力），后续可扩展为反射/配置驱动
/// </summary>
public static class AbilityRegistry
{
    /// <summary>
    /// 已注册的能力 ID 列表（权威来源，Import/Runtime 共用）
    /// </summary>
    private static readonly HashSet<string> registeredIds = new HashSet<string>
    {
        "BasicMove",
        "BasicRun",
        "BasicJump",
        "BasicAttack",
        "BasicRangedAttack"
    };

    /// <summary>
    /// 检查 ID 是否已注册
    /// </summary>
    public static bool IsRegistered(string id)
    {
        return registeredIds.Contains(id);
    }

    /// <summary>
    /// 获取所有已注册的 ID（用于 Import 校验日志）
    /// </summary>
    public static IEnumerable<string> GetAllRegisteredIds()
    {
        return registeredIds;
    }

    /// <summary>
    /// 根据 ID 创建能力实例（Factory）
    /// </summary>
    /// <param name="id">能力 ID</param>
    /// <param name="playerController">PlayerController 引用</param>
    /// <param name="priority">优先级</param>
    /// <param name="enabled">是否启用</param>
    /// <returns>能力实例，失败返回 null</returns>
    public static IPlayerAbility CreateAbility(
        string id,
        PlayerController playerController,
        int priority,
        bool enabled)
    {
        if (!IsRegistered(id))
        {
            Debug.LogError($"[AbilityRegistry] CreateAbility failed: id '{id}' not registered");
            return null;
        }

        if (playerController == null)
        {
            Debug.LogError($"[AbilityRegistry] CreateAbility failed: playerController is null for id '{id}'");
            return null;
        }

        try
        {
            // 0.2 硬编码映射（后续可改为反射/配置驱动）
            // Phase 5: 所有构造函数现在接收 abilityId 参数
            switch (id)
            {
                case "BasicMove":
                    return new DefaultMoveAbility(playerController, id, priority, enabled);
                case "BasicRun":
                    return new DefaultRunAbility(playerController, id, priority, enabled);
                case "BasicJump":
                    return new DefaultJumpAbility(playerController, id, priority, enabled);
                case "BasicAttack":
                    return new DefaultAttackAbility(playerController, id, priority, enabled);
                case "BasicRangedAttack":
                    return new DefaultRangedAttackAbility(playerController, id, priority, enabled);
                default:
                    Debug.LogError($"[AbilityRegistry] CreateAbility failed: no factory for id '{id}'");
                    return null;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[AbilityRegistry] CreateAbility exception for id '{id}': {ex.Message}\n{ex.StackTrace}");
            return null;
        }
    }
}
