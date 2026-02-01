using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 玩家控制器脚本
///
/// 功能说明:
/// - 管理玩家的移动、奔跑、跳跃、攻击等核心玩法
/// - 处理输入系统(Input System)的输入回调
/// - 控制角色朝向(左/右)和翻转
/// - 计算当前移动速度，区分行走/奔跑/空中状态
/// - 驱动动画系统，更新Animator参数
/// - 与物理系统(Rigidbody2D)和碰撞检测(TouchingDirections)交互
///
/// 依赖组件:
/// - Rigidbody2D: 角色刚体，处理速度和物理
/// - TouchingDirections: 碰撞检测，检测地面/墙壁/天花板状态
/// - Animator: 动画系统，处理动画状态切换
///
/// 关键属性:
/// - CurrentMoveSpeed: 根据当前状态计算的速度(读取属性)
/// - IsMoving: 是否在移动中
/// - IsRunning: 是否在奔跑
/// - IsFacingRight: 是否朝向右侧
/// - CanMove: 是否允许移动(由动画控制，防止攻击时移动)
/// </summary>
[RequireComponent(typeof(Rigidbody2D), typeof(TouchingDirections), typeof(Damageable))]

[DefaultExecutionOrder(-100)]
public partial class PlayerController : MonoBehaviour
{
    /// <summary>
    /// 玩家配置资源（阶段 3A）
    /// 从 CastleDB 导入的玩家配置，包含移动速度、生命值等参数
    /// 如果未设置，将使用硬编码的默认值
    /// </summary>
    [Header("配置")]
    [SerializeField]
    [Tooltip("玩家配置资源（从 CastleDB 导入），留空则使用默认值")]
    private PlayerConfig playerConfig;

    /// <summary>
    /// 是否使用 PlayerConfig（回退开关，阶段 3A）
    /// - true: 从 PlayerConfig 加载配置（推荐，数据驱动）
    /// - false: 使用硬编码的默认值（回退方案）
    /// </summary>
    [SerializeField]
    [Tooltip("是否使用 PlayerConfig 加载配置（false则使用硬编码默认值）")]
    private bool usePlayerConfigFromCastleDb = true;

    [Header("技能释放（0.5 阶段2）")]
    [SerializeField]
    [Tooltip("技能释放出生点（例如火球）。为空时回退到玩家 Transform。")]
    private Transform abilityFirePoint;

    private struct PendingAbilityRelease
    {
        public bool hasRequest;
        public float expiresAt;
        public string abilityId;
        public Action releaseAction;
    }

    private PendingAbilityRelease _pendingAbilityRelease;

    /// <summary>行走速度(m/s)</summary>
    [Header("移动参数（运行时从 PlayerConfig 覆盖）")]
    public float walkSpeed = 5f;

    /// <summary>奔跑速度(m/s)</summary>
    public float runSpeed = 8f;

    /// <summary>空中移动速度(m/s) - 跳跃时的移动速度</summary>
    public float airWalkSpeed = 3f;

    /// <summary>跳跃冲力 - 给予Y轴速度</summary>
    public float jumpImpules = 10f;

    /// <summary>
    /// 输入的移动方向向量
    ///
    /// 说明: 现在分为水平输入(X轴)和垂直输入(Y轴)
    /// - X轴(moveInput.x): 来自A/D键，用于水平移动
    /// - Y轴(moveInput.y): 来自W/S键，预留给爬墙系统使用
    /// </summary>
    [System.NonSerialized]
    public Vector2 moveInput;

    /// <summary>
    /// 水平输入(只包含X轴分量)
    ///
    /// 说明: 由A/D键控制，用于驱动行走/奔跑动画
    /// 值域: -1.0 ~ 1.0
    /// - 负数: 向左
    /// - 0: 无水平输入
    /// - 正数: 向右
    /// 用途: 判断IsMoving状态，控制角色朝向
    /// </summary>
    [System.NonSerialized]
    public float moveInputHorizontal = 0f;

    /// <summary>
    /// 垂直输入(只包含Y轴分量)
    ///
    /// 说明: 由W/S键控制，预留给爬墙系统使用
    /// 值域: -1.0 ~ 1.0
    /// - 负数: 向下
    /// - 0: 无垂直输入
    /// - 正数: 向上
    /// 用途: 后续爬墙系统中控制攀爬方向
    /// </summary>
    [System.NonSerialized]
    public float moveInputVertical = 0f;

    /// <summary>爬墙输入向量
    ///
    /// 说明: 用于爬墙时的上下输入，与moveInput相同但用于爬墙逻辑
    /// 用途: 在爬墙状态下控制垂直方向的速度
    /// </summary>
    [System.NonSerialized]
    public Vector2 climbInput;

    /// <summary>爬墙速度(m/s)
    ///
    /// 说明: 角色爬墙时的上下移动速度
    /// 配置: 建议设置为2-4f，比行走速度(5f)略慢，给予玩家充足的反应时间
    /// 用途: 在FixedUpdate中计算爬墙时的Y轴速度
    /// </summary>
    public float climbSpeed = 3f;

    /// <summary>碰撞检测组件的引用</summary>
    TouchingDirections touchingDirections;
    Damageable damageable;
    StatModifierLayer statLayer;
    /// <summary>刚体组件引用(用于应用速度)</summary>
    Rigidbody2D rb;

    /// <summary>动画系统组件引用(用于驱动动画状态)</summary>
    Animator animator;

    /// <summary>能力系统（阶段 3B）</summary>
    private AbilitySystem abilitySystem;

    /// <summary>
    /// 玩家上下文（用于读取 Inventory 槽位，从而实现“按槽位释放”）
    /// </summary>
    private PlayerContext _playerContext;
}
