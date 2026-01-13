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

    private readonly Dictionary<string, float> _attackMultiplierBySource = new Dictionary<string, float>();
    private float _attackMultiplier = 1f;

    /// <summary>
    /// 当前移速倍率（乘到基础速度上）
    /// </summary>
    public float MoveSpeedMultiplier => _moveSpeedMultiplier;

    /// <summary>
    /// 当前攻击倍率（乘到基础伤害上）
    /// </summary>
    public float AttackMultiplier => _attackMultiplier;

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
        _attackMultiplierBySource.Clear();
        _attackMultiplier = 1f;
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

    public bool SetAttackMultiplier(string sourceId, float multiplier)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return false;
        }

        multiplier = Mathf.Max(0f, multiplier);

        if (Mathf.Approximately(multiplier, 1f))
        {
            return ClearAttackMultiplier(sourceId);
        }

        _attackMultiplierBySource[sourceId] = multiplier;
        RecomputeAttackMultiplier();
        return true;
    }

    public bool ClearAttackMultiplier(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            return false;
        }

        if (_attackMultiplierBySource.Remove(sourceId))
        {
            RecomputeAttackMultiplier();
        }

        return true;
    }

    private void RecomputeAttackMultiplier()
    {
        float result = 1f;
        foreach (float v in _attackMultiplierBySource.Values)
        {
            result *= v;
        }

        _attackMultiplier = Mathf.Max(0f, result);
    }
}
