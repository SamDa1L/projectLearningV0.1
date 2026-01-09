using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 属性/修正层（Phase 1-4/1-5）
///
/// 目的：
/// - 为 Status/被动能力 提供统一的数值修改入口
/// - Phase 1-4 最小落地：只支持 MoveSpeedMultiplier
/// </summary>
public class StatModifierLayer : MonoBehaviour
{
    private readonly Dictionary<string, float> _moveSpeedBySource = new Dictionary<string, float>();
    private float _moveSpeedMultiplier = 1f;

    /// <summary>
    /// 当前移速倍率（乘到基础速度上）
    /// </summary>
    public float MoveSpeedMultiplier => _moveSpeedMultiplier;

    /// <summary>
    /// 设置某个来源的移速倍率
    /// - sourceId 为空：失败
    /// - multiplier 近似 1：视为移除该来源
    /// </summary>
    public bool SetMoveSpeedMultiplier(string sourceId, float multiplier)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return false;
        }

        multiplier = Mathf.Max(0f, multiplier);

        if (Mathf.Approximately(multiplier, 1f))
        {
            return ClearMoveSpeedMultiplier(sourceId);
        }

        _moveSpeedBySource[sourceId] = multiplier;
        RecomputeMoveSpeedMultiplier();
        return true;
    }

    public bool ClearMoveSpeedMultiplier(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return false;
        }

        if (_moveSpeedBySource.Remove(sourceId))
        {
            RecomputeMoveSpeedMultiplier();
        }

        return true;
    }

    public void ClearAll()
    {
        _moveSpeedBySource.Clear();
        _moveSpeedMultiplier = 1f;
    }

    private void RecomputeMoveSpeedMultiplier()
    {
        float result = 1f;
        foreach (float v in _moveSpeedBySource.Values)
        {
            result *= v;
        }

        _moveSpeedMultiplier = Mathf.Max(0f, result);
    }
}

