using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 玩家能力调度系统（阶段 3B）
///
/// 职责：
/// - 管理已注册的能力实例
/// - 按 Priority 排序并分发输入到能力
/// - 执行 handled 中止传播语义
/// </summary>
public class AbilitySystem
{
    /// <summary>
    /// 能力列表（按 HookType 分组，每组内按 Priority 降序排列）
    /// </summary>
    private Dictionary<AbilityHookType, List<IPlayerAbility>> abilityMap;

    /// <summary>
    /// 构造函数
    /// </summary>
    public AbilitySystem()
    {
        abilityMap = new Dictionary<AbilityHookType, List<IPlayerAbility>>();

        // 初始化所有 HookType 的空列表
        foreach (AbilityHookType hookType in System.Enum.GetValues(typeof(AbilityHookType)))
        {
            abilityMap[hookType] = new List<IPlayerAbility>();
        }
    }

    /// <summary>
    /// 注册能力到指定 Hook
    /// </summary>
    /// <param name="hookType">Hook 类型</param>
    /// <param name="ability">能力实例</param>
    public void RegisterAbility(AbilityHookType hookType, IPlayerAbility ability)
    {
        if (ability == null)
        {
            Debug.LogError($"[AbilitySystem] RegisterAbility failed: ability is null for {hookType}");
            return;
        }

        if (!abilityMap.ContainsKey(hookType))
        {
            abilityMap[hookType] = new List<IPlayerAbility>();
        }

        abilityMap[hookType].Add(ability);

        // 按 Priority 降序排序（数值越大越先执行）
        abilityMap[hookType] = abilityMap[hookType]
            .OrderByDescending(a => a.Priority)
            .ToList();

        Debug.Log($"[AbilitySystem] Registered ability to {hookType}, Priority={ability.Priority}");
    }

    /// <summary>
    /// 分发输入到指定 Hook 的所有能力（按 Priority 顺序执行，handled 则中止）
    /// </summary>
    /// <param name="hookType">Hook 类型</param>
    /// <param name="input">输入快照</param>
    /// <returns>是否有任意能力消费了输入</returns>
    public bool Dispatch(AbilityHookType hookType, AbilityInput input)
    {
        if (!abilityMap.ContainsKey(hookType))
        {
            Debug.LogWarning($"[AbilitySystem] Dispatch failed: no abilities registered for {hookType}");
            return false;
        }

        List<IPlayerAbility> abilities = abilityMap[hookType];

        if (abilities.Count == 0)
        {
            // 无能力监听该 Hook（警告已在构建时输出，此处静默）
            return false;
        }

        // 按 Priority 顺序执行，直到某个能力返回 handled=true
        foreach (IPlayerAbility ability in abilities)
        {
            if (!ability.Enabled)
            {
                // 跳过禁用的能力（理论上不应出现，因为构建时已过滤）
                continue;
            }

            bool handled = false;

            // 根据 HookType 调用对应的回调
            switch (hookType)
            {
                case AbilityHookType.Move:
                    handled = ability.OnMove(input);
                    break;
                case AbilityHookType.Run:
                    handled = ability.OnRun(input);
                    break;
                case AbilityHookType.Jump:
                    handled = ability.OnJump(input);
                    break;
                case AbilityHookType.Attack:
                    handled = ability.OnAttack(input);
                    break;
                case AbilityHookType.RangedAttack:
                    handled = ability.OnRangedAttack(input);
                    break;
                default:
                    Debug.LogError($"[AbilitySystem] Unknown HookType: {hookType}");
                    break;
            }

            // 如果能力消费了输入，中止后续传播
            if (handled)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 获取指定 Hook 的能力数量（用于调试/诊断）
    /// </summary>
    public int GetAbilityCount(AbilityHookType hookType)
    {
        if (abilityMap.ContainsKey(hookType))
        {
            return abilityMap[hookType].Count;
        }
        return 0;
    }

    /// <summary>
    /// 清空所有能力（用于重置/测试）
    /// </summary>
    public void ClearAll()
    {
        foreach (var list in abilityMap.Values)
        {
            list.Clear();
        }
    }
}
