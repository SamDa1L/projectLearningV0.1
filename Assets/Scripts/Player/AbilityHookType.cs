/// <summary>
/// 能力钩子类型枚举（阶段 3B）
/// 定义玩家输入可以触发的能力入口点
/// </summary>
public enum AbilityHookType
{
    /// <summary>移动输入（WASD/摇杆）</summary>
    Move = 0,

    /// <summary>奔跑输入（Shift）</summary>
    Run = 1,

    /// <summary>跳跃输入（Space）</summary>
    Jump = 2,

    /// <summary>近战攻击输入（鼠标左键）</summary>
    Attack = 3,

    /// <summary>远程攻击输入（F键）</summary>
    RangedAttack = 4
}
