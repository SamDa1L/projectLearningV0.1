using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 敌人感知接口
/// 定义敌人如何检测和感知目标的方法
///
/// 设计思路：
/// - 不同敌人类型可能有不同的感知方式（视锥、范围、射线等）
/// - 通过接口将感知逻辑与状态机逻辑解耦
/// - 便于未来扩展不同的感知方式（远程、AOE等）
/// </summary>
public interface IAgentPerception
{
    /// <summary>
    /// 获取所有检测到的目标碰撞器列表
    /// </summary>
    /// <returns>检测到的Collider2D列表</returns>
    List<Collider2D> GetDetectedTargets();

    /// <summary>
    /// 检查特定目标是否在感知范围内
    /// </summary>
    /// <param name="target">目标的Transform</param>
    /// <param name="range">感知范围半径</param>
    /// <returns>目标是否在范围内</returns>
    bool IsTargetInRange(Transform target, float range);
}
