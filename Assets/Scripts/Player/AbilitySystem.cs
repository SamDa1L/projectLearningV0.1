using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 玩家能力调度系统
///
/// 职责（阶段 3B）：
/// - 管理已注册的能力实例
/// - 按 Priority 排序并分发输入到能力
/// - 执行 handled 中止传播语义
///
/// 职责（Phase 5）：
/// - Enable/Disable 队列化机制（保序去重）
/// - LateUpdate flush 统一应用状态变更
/// </summary>
public class AbilitySystem
{
    /// <summary>
    /// 能力列表（按 HookType 分组，每组内按 Priority 降序排列）
    /// </summary>
    private Dictionary<AbilityHookType, List<IPlayerAbility>> abilityMap;

    // ===== Phase 5: Enable/Disable 队列化机制 =====
    /// <summary>
    /// 能力 ID 到能力实例的映射（用于快速查找）
    /// </summary>
    private Dictionary<string, IPlayerAbility> abilityById;

    /// <summary>
    /// 待处理队列（保留写入顺序）
    /// </summary>
    private List<(string abilityId, bool enabled)> pendingQueue;

    /// <summary>
    /// 同帧去重记录最后状态
    /// </summary>
    private Dictionary<string, bool> pendingLast;

    /// <summary>
    /// 一次性日志去重（abilityId 不存在）
    /// </summary>
    private HashSet<string> loggedErrors;

    /// <summary>
    /// 构造函数
    /// </summary>
    public AbilitySystem()
    {
        abilityMap = new Dictionary<AbilityHookType, List<IPlayerAbility>>();
        abilityById = new Dictionary<string, IPlayerAbility>();
        pendingQueue = new List<(string, bool)>();
        pendingLast = new Dictionary<string, bool>();
        loggedErrors = new HashSet<string>();

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

        // Phase 5: 添加到 abilityById 映射
        if (!string.IsNullOrEmpty(ability.AbilityId))
        {
            abilityById[ability.AbilityId] = ability;
        }

        Debug.Log($"[AbilitySystem] Registered ability to {hookType}, Priority={ability.Priority}, AbilityId={ability.AbilityId}");
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
                // 跳过禁用的能力
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

    // ===== Phase 5: Enable/Disable API =====
    /// <summary>
    /// 设置能力启用/禁用状态（队列化，LateUpdate 统一应用）
    /// </summary>
    /// <param name="abilityId">能力 ID</param>
    /// <param name="enabled">是否启用</param>
    /// <returns>true=已入队；false=abilityId 不存在</returns>
    public bool SetAbilityEnabled(string abilityId, bool enabled)
    {
        // 1) abilityId 不存在：Error（一次性）+ 返回 false（不入队）
        if (!abilityById.ContainsKey(abilityId))
        {
            string key = $"SetAbilityEnabled_NotFound_{abilityId}";
            if (!loggedErrors.Contains(key))
            {
                Debug.LogError($"[AbilitySystem] SetAbilityEnabled 失败：abilityId 不存在 '{abilityId}'");
                loggedErrors.Add(key);
            }
            return false;
        }

        IPlayerAbility ability = abilityById[abilityId];

        // 2) 状态未变化：不记录，不入队
        if (ability.Enabled == enabled)
        {
            return true; // 视为成功（已是期望状态）
        }

        // 3) 状态变化：追加到 pendingQueue，并写入 pendingLast
        pendingQueue.Add((abilityId, enabled));
        pendingLast[abilityId] = enabled;

        return true;
    }

    /// <summary>
    /// 查询能力是否启用
    /// </summary>
    /// <param name="abilityId">能力 ID</param>
    /// <returns>true=启用；false=禁用或不存在</returns>
    public bool IsAbilityEnabled(string abilityId)
    {
        if (!abilityById.ContainsKey(abilityId))
        {
            // 默认不输出日志（避免噪音）
            // 如需调试，可启用以下代码：
            // string key = $"IsAbilityEnabled_NotFound_{abilityId}";
            // if (!loggedErrors.Contains(key))
            // {
            //     Debug.Log($"[AbilitySystem] IsAbilityEnabled: abilityId 不存在 '{abilityId}'");
            //     loggedErrors.Add(key);
            // }
            return false;
        }

        return abilityById[abilityId].Enabled;
    }

    /// <summary>
    /// LateUpdate flush 机制（由外部调用，如 PlayerController.LateUpdate）
    /// 按 pendingQueue 顺序遍历，仅应用最后一次写入
    /// </summary>
    public void FlushPendingChanges()
    {
        if (pendingQueue.Count == 0)
        {
            return; // 无待处理变更
        }

        int appliedCount = 0;

        // 按 pendingQueue 顺序遍历
        foreach (var (abilityId, enabled) in pendingQueue)
        {
            // 仅当该条目状态等于 pendingLast[abilityId] 时执行
            // （同帧只应用最后一次写入，且保留最终写入间的相对顺序）
            if (pendingLast[abilityId] == enabled)
            {
                IPlayerAbility ability = abilityById[abilityId];
                ability.Enabled = enabled;
                appliedCount++;

                // 可选 Debug 日志（可由编译宏控制）
                #if ABILITY_SYSTEM_DEBUG
                Debug.Log($"[AbilitySystem] Applied Enable={enabled} for abilityId={abilityId}");
                #endif
            }
        }

        // flush 后清空两个容器
        pendingQueue.Clear();
        pendingLast.Clear();

        // 可选统计日志
        if (appliedCount > 0)
        {
            Debug.Log($"[AbilitySystem] FlushPendingChanges: 应用 {appliedCount} 个状态变更");
        }
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
        abilityById.Clear();
        pendingQueue.Clear();
        pendingLast.Clear();
        loggedErrors.Clear();
    }
}
