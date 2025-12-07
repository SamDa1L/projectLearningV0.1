using UnityEngine;

/// <summary>
/// 敌人伤害响应接口
/// 定义敌人如何响应伤害和击退的方法
///
/// 设计思路：
/// - 不同敌人类型可能有不同的伤害处理方式
/// - 通过接口将伤害逻辑与状态机逻辑解耦
/// - 便于未来实现无敌状态、反弹等特殊响应
/// </summary>
public interface IDamageResponder
{
    /// <summary>
    /// 处理受到的伤害和击退
    /// </summary>
    /// <param name="damage">受到的伤害值</param>
    /// <param name="knockbackDirection">击退方向和力度（x为水平，y为竖直）</param>
    void OnDamageTaken(int damage, Vector2 knockbackDirection);

    /// <summary>
    /// 检查敌人是否处于无敌状态
    /// </summary>
    /// <returns>是否无敌</returns>
    bool IsInvulnerable();
}
