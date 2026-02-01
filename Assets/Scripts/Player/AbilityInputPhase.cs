/// <summary>
/// 能力输入阶段枚举（阶段 3B）
/// 映射 Unity InputSystem 的 InputActionPhase
/// </summary>
public enum AbilityInputPhase
{
    /// <summary>输入开始（按键按下瞬间）</summary>
    Started = 0,

    /// <summary>输入执行中（按键持续按住）</summary>
    Performed = 1,

    /// <summary>输入取消（按键释放）</summary>
    Canceled = 2
}
