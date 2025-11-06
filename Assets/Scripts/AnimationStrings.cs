using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

/// <summary>
/// 动画参数字符串常量管理类
///
/// 功能说明:
/// - 集中管理所有Animator动画参数的字符串常量
/// - 避免在代码中硬编码字符串导致的拼写错误
/// - 提供单一的参考源，便于维护和查阅
/// - 确保代码中使用的动画参数名与Animator编辑器中定义的名称保持一致
///
/// 使用示例:
/// animator.SetBool(AnimationStrings.isMoving, true);
/// animator.SetTrigger(AnimationStrings.jumpTrigger);
/// animator.SetFloat(AnimationStrings.yVelocity, velocity);
///
/// 注意事项:
/// - 这里定义的字符串必须与Animator状态机编辑器中创建的参数名称完全一致
/// - 如果修改Animator中的参数名，也必须在这里同步修改
/// - 通过使用常量可以在编译时捕获拼写错误而不是在运行时才发现
/// </summary>
internal class AnimationStrings
{
    /// <summary>
    /// 移动状态参数
    /// 类型: Bool
    /// 说明: 控制角色是否在移动，驱动待机(Idle)和移动(Walk)动画之间的切换
    /// 用途: PlayerController.cs 在OnMove回调中设置
    /// </summary>
    internal static string isMoving = "isMoving";

    /// <summary>
    /// 奔跑状态参数
    /// 类型: Bool
    /// 说明: 控制角色是否在奔跑，驱动行走(Walk)和奔跑(Run)动画之间的切换
    /// 用途: PlayerController.cs 在OnRun回调中设置
    /// </summary>
    internal static string isRunning = "isRunning";

    /// <summary>
    /// 地面状态参数
    /// 类型: Bool
    /// 说明: 标记角色是否接触地面，用于区分地面动画和空中动画
    /// 用途: TouchingDirections.cs 定期更新此参数
    /// 影响: 控制下落、着地等空中动画状态
    /// </summary>
    internal static string isGrounded = "isGrounded";

    /// <summary>
    /// Y轴速度参数
    /// 类型: Float
    /// 说明: 角色当前的Y轴速度(向上为正，向下为负)
    /// 用途: PlayerController.cs 在FixedUpdate中设置
    /// 影响: 控制上升(Rising)、下落(Falling)等动画状态的选择
    /// 原理: 根据速度值大于/小于0来切换不同的动画状态
    /// </summary>
    internal static string yVelocity = "yVelocity";

    /// <summary>
    /// 跳跃触发器参数
    /// 类型: Trigger(触发器，自动重置)
    /// 说明: 触发角色跳跃动画
    /// 用途: PlayerController.cs 在OnJump回调中设置
    /// 说明: 使用Trigger类型会在被读取后自动重置，防止重复触发
    /// </summary>
    internal static string jumpTrigger = "jump";

    /// <summary>
    /// 墙壁接触状态参数
    /// 类型: Bool
    /// 说明: 标记角色是否接触墙壁
    /// 用途: TouchingDirections.cs 定期更新此参数
    /// 影响: 可用于控制墙壁滑动、爬墙等动画(当前功能预留)
    /// 设计: 为未来的墙壁滑动机制预留
    /// </summary>
    internal static string isOnWall = "isOnWall";

    /// <summary>
    /// 天花板接触状态参数
    /// 类型: Bool
    /// 说明: 标记角色是否接触天花板
    /// 用途: TouchingDirections.cs 定期更新此参数
    /// 影响: 防止角色穿透天花板，控制撞头动画(功能预留)
    /// 设计: 为未来的撞头等特殊状态预留
    /// </summary>
    internal static string isOnCeiling = "isOnCeiling";

    /// <summary>
    /// 攻击触发器参数
    /// 类型: Trigger(触发器，自动重置)
    /// 说明: 触发角色攻击动画序列
    /// 用途: PlayerController.cs 在OnAttack回调中设置
    /// 说明: 使用Trigger类型防止多次触发
    /// 配合: 动画状态机中的SetBoolBehaviour脚本控制canMove参数
    ///       在攻击动画播放期间设置canMove=false防止移动
    /// </summary>
    internal static string attackTrigger = "attack";

    /// <summary>
    /// 允许移动状态参数
    /// 类型: Bool
    /// 说明: 控制是否允许角色移动，由动画系统设置
    /// 用途: PlayerController.cs 在CurrentMoveSpeed属性中读取
    /// 机制: 动画状态机使用SetBoolBehaviour脚本在特定动画状态(如攻击)中设置此参数
    ///       - 进入攻击状态时: canMove = false (禁止移动)
    ///       - 退出攻击状态时: canMove = true (允许移动)
    /// 好处: 将角色行为控制权交给动画系统，便于在编辑器中微调
    /// </summary>
    internal static string canMove = "canMove";

    /// <summary>
    /// 爬墙状态参数
    /// 类型: Bool
    /// 说明: 标记角色是否正在爬墙
    /// 用途: PlayerController 在OnMove中设置
    /// 影响: 控制爬墙/非爬墙动画切换
    /// 配合参数: climbSpeed - 指示爬墙方向(上/停止/下)
    /// </summary>
    internal static string isClimbing = "isClimbing";

    /// <summary>
    /// 爬墙速度参数
    /// 类型: Float
    /// 说明: 爬墙时的Y轴速度方向和大小(玩家意图的控制值)
    /// 用途: PlayerController 在FixedUpdate中设置
    /// 值说明:
    /// - 正数(> 0.1): 向上爬
    /// - 接近0(-0.1 ~ 0.1): 停止爬(贴在墙上)
    /// - 负数(< -0.1): 向下爬
    /// 影响: 控制爬墙的上升/下降/停止动画
    /// 注意: 与yVelocity不同，climbSpeed是玩家的"意图"而非实际物理速度
    /// </summary>
    internal static string climbSpeed = "climbSpeed";
    internal static string hasTarget = "hasTarget";
    internal static string isAlive = "isAlive";
}
