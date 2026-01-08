using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 状态效果控制器（Phase 1-3 最小落地占位）
///
/// 当前职责（Phase 1-3）：
/// - 提供 Apply(statusId, durationOverride) 入口，供 AbilityEffectExecutor 调用
/// - 以最小可测试方式记录已应用的 statusId（Phase 1-4 会扩展为完整状态系统）
/// </summary>
public class StatusEffectController : MonoBehaviour
{
    private readonly HashSet<string> _active = new HashSet<string>();
    private readonly List<string> _activeList = new List<string>();

    public event Action<string, float> OnStatusApplied;

    /// <summary>
    /// 当前已激活的状态 ID 列表（调试/测试用）
    /// </summary>
    public IReadOnlyList<string> ActiveStatusIds => _activeList;

    /// <summary>
    /// 应用状态效果
    /// </summary>
    /// <param name="statusId">状态 ID（必填）</param>
    /// <param name="durationOverride">持续时间覆盖（<0 表示使用默认值；Phase 1-4 才会消费）</param>
    /// <returns>true=记录成功；false=参数非法</returns>
    public bool Apply(string statusId, float durationOverride = -1f)
    {
        if (string.IsNullOrWhiteSpace(statusId))
        {
            Debug.LogError("[StatusEffectController] Apply 失败：statusId 为空", this);
            return false;
        }

        if (_active.Add(statusId))
        {
            _activeList.Add(statusId);
        }

        OnStatusApplied?.Invoke(statusId, durationOverride);
        return true;
    }

    public bool HasStatus(string statusId)
    {
        if (string.IsNullOrWhiteSpace(statusId))
        {
            return false;
        }

        return _active.Contains(statusId);
    }
}

