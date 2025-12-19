using UnityEngine;

/// <summary>
/// 能力输入快照（阶段 3B）
///
/// 解耦 Unity InputSystem，用于：
/// - 测试/回放/AI（无需构造 InputAction.CallbackContext）
/// - 能力层不直接依赖 InputSystem
/// </summary>
public struct AbilityInput
{
    /// <summary>输入阶段（Started/Performed/Canceled）</summary>
    public AbilityInputPhase Phase;

    /// <summary>移动向量（WASD/摇杆输入）</summary>
    public Vector2 Move;

    /// <summary>按键是否按下（用于奔跑/跳跃/攻击等二元输入）</summary>
    public bool IsPressed;

    /// <summary>
    /// 创建 Started 阶段的输入快照
    /// </summary>
    public static AbilityInput Started(Vector2 move = default, bool isPressed = false)
    {
        return new AbilityInput
        {
            Phase = AbilityInputPhase.Started,
            Move = move,
            IsPressed = isPressed
        };
    }

    /// <summary>
    /// 创建 Performed 阶段的输入快照
    /// </summary>
    public static AbilityInput Performed(Vector2 move = default, bool isPressed = true)
    {
        return new AbilityInput
        {
            Phase = AbilityInputPhase.Performed,
            Move = move,
            IsPressed = isPressed
        };
    }

    /// <summary>
    /// 创建 Canceled 阶段的输入快照
    /// </summary>
    public static AbilityInput Canceled()
    {
        return new AbilityInput
        {
            Phase = AbilityInputPhase.Canceled,
            Move = Vector2.zero,
            IsPressed = false
        };
    }
}
