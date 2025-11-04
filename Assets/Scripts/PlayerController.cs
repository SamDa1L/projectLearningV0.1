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
[RequireComponent(typeof(Rigidbody2D), typeof(TouchingDirections))]

public class PlayerController : MonoBehaviour
{
    /// <summary>行走速度(m/s)</summary>
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
    Vector2 moveInput;

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
    float moveInputHorizontal = 0f;

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
    float moveInputVertical = 0f;

    /// <summary>爬墙输入向量
    ///
    /// 说明: 用于爬墙时的上下输入，与moveInput相同但用于爬墙逻辑
    /// 用途: 在爬墙状态下控制垂直方向的速度
    /// </summary>
    Vector2 climbInput;

    /// <summary>爬墙速度(m/s)
    ///
    /// 说明: 角色爬墙时的上下移动速度
    /// 配置: 建议设置为2-4f，比行走速度(5f)略慢，给予玩家充足的反应时间
    /// 用途: 在FixedUpdate中计算爬墙时的Y轴速度
    /// </summary>
    public float climbSpeed = 3f;

    /// <summary>碰撞检测组件的引用</summary>
    TouchingDirections touchingDirections;

    /// <summary>
    /// 计算当前水平移动速度属性
    ///
    /// 逻辑流程:
    /// 1. 检查CanMove - 是否允许移动(由动画系统控制)
    /// 2. 检查IsMoving && !IsOnWall - 是否有水平移动输入且不在墙壁
    /// 3. 判断IsGrounded - 在地面还是空中
    /// 4. 在地面上区分IsRunning - 返回奔跑速度或行走速度
    /// 5. 在空中 - 返回降低的空中移动速度
    ///
    /// 注意: 仅基于水平移动(moveInputHorizontal)，不受垂直输入(W/S)影响
    ///
    /// 返回值: float 当前应该使用的水平移动速度
    /// </summary>
    public float CurrentMoveSpeed
    {
        get
        {
            if (CanMove)
            {
                // 检查是否在移动且没有接触墙壁
                if (IsMoving && !touchingDirections.IsOnWall)
                {
                    // 区分地面和空中状态
                    if (touchingDirections.IsGrounded)
                    {
                        // 在地面上 - 区分奔跑和行走
                        if (IsRunning)
                        {
                            return runSpeed;
                        }
                        else
                        {
                            return walkSpeed;
                        }
                    }
                    else
                    {
                        // 在空中 - 返回降低的移动速度
                        return airWalkSpeed;
                    }
                }
                else
                {
                    // 没有移动输入或接触墙壁 - 待机速度为0
                    return 0;
                }
            }
            else
            {
                // 禁止移动(例如正在攻击) - 返回0
                return 0;
            }

        }
    }


    /// <summary>是否在移动的内部字段</summary>
    [SerializeField]
    private bool _isMoving = false;

    /// <summary>
    /// 是否在进行水平移动的属性
    ///
    /// 定义: 当前是否有水平方向的移动输入(A/D键)
    /// 说明: 仅基于moveInputHorizontal判断，不包含垂直方向(W/S)
    ///
    /// getter: 返回当前水平移动状态
    /// setter: 设置水平移动状态并同步到Animator
    /// </summary>
    public bool IsMoving
    {
        get
        {
            return _isMoving;
        }
        private set
        {
            _isMoving = value;
            // 同步更新Animator的isMoving参数，驱动待机/移动动画切换
            animator.SetBool(AnimationStrings.isMoving, value);
        }
    }

    /// <summary>是否在奔跑的内部字段</summary>
    [SerializeField]
    private bool _isRunning = false;

    /// <summary>
    /// 是否在奔跑的属性
    ///
    /// getter: 返回当前奔跑状态
    /// setter: 设置奔跑状态并同步到动画系统
    /// </summary>
    public bool IsRunning
    {
        get
        {
            return _isRunning;
        }
        set
        {
            _isRunning = value;
            // 同步更新Animator的isRunning参数，驱动行走/奔跑动画切换
            animator.SetBool(AnimationStrings.isRunning, value);
        }
    }

    /// <summary>角色是否朝向右侧的内部字段(true=右, false=左)</summary>
    public bool _isFacingRight = true;

    /// <summary>
    /// 角色朝向属性
    ///
    /// getter: 返回当前朝向(true=右, false=左)
    /// setter: 设置朝向，如果改变则翻转角色(缩放X=-1)
    ///
    /// 翻转原理: 改变transform.localScale的X分量来实现角色左右翻转
    /// </summary>
    public bool IsFacingRight
    {
        get
        {
            return _isFacingRight;
        }
        private set
        {
            if (_isFacingRight != value)
            {
                // 朝向改变时翻转角色 - 缩放X轴乘以-1
                transform.localScale *= new Vector2(-1, 1);
            }
            _isFacingRight = value;
        }
    }

    /// <summary>
    /// 是否允许移动属性(只读)
    ///
    /// 该属性从Animator中读取canMove参数
    /// 用于防止在攻击等特定动画播放时进行移动
    /// 返回值: true表示允许移动，false表示禁止移动
    /// </summary>
    public bool CanMove
    {
        get
        {
            return animator.GetBool(AnimationStrings.canMove);
        }
    }

    /// <summary>是否正在爬墙的内部字段</summary>
    [SerializeField]
    private bool _isClimbing = false;

    /// <summary>
    /// 是否正在爬墙的属性
    ///
    /// 定义: 当前角色是否接触墙壁并进行爬墙操作
    ///
    /// getter: 返回当前爬墙状态
    /// setter: 设置爬墙状态并同步到Animator的isClimbing参数
    ///
    /// 用途:
    /// - 在OnMove中判断是否进入爬墙状态
    /// - 在FixedUpdate中判断是否使用爬墙物理
    /// - 在OnJump中判断是否进行壁跳
    ///
    /// 进入条件:
    /// - 接触墙壁(IsOnWall = true)
    /// - 有垂直方向的输入(moveInputVertical ≠ 0)
    /// - 允许移动(CanMove = true)
    ///
    /// 退出条件:
    /// - 释放垂直输入(moveInputVertical = 0)
    /// - 离开墙壁(IsOnWall = false)
    /// - 执行跳跃(OnJump触发)
    /// </summary>
    public bool IsClimbing
    {
        get
        {
            return _isClimbing;
        }
        private set
        {
            _isClimbing = value;
            // 同步爬墙状态到Animator
            animator.SetBool(AnimationStrings.isClimbing, value);
        }
    }


    /// <summary>刚体组件引用(用于应用速度)</summary>
    Rigidbody2D rb;

    /// <summary>动画系统组件引用(用于驱动动画状态)</summary>
    Animator animator;


    /// <summary>
    /// Awake生命周期函数
    /// 用于在Game Object激活时初始化组件引用
    /// </summary>
    private void Awake()
    {
        // 获取当前GameObject上的Rigidbody2D组件
        rb = GetComponent<Rigidbody2D>();

        // 获取当前GameObject上的Animator组件
        animator = GetComponent<Animator>();

        // 获取当前GameObject上的TouchingDirections组件(碰撞检测)
        touchingDirections = GetComponent<TouchingDirections>();
    }

    // Start is called before the first frame update
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {

    }

    /// <summary>
    /// FixedUpdate生命周期函数
    /// 每个物理帧调用一次，用于更新物理相关的逻辑
    ///
    /// 功能:
    /// - 判断是否在爬墙：IsClimbing
    /// - 如果爬墙: 使用climbInput和climbSpeed控制垂直速度
    /// - 如果正常: 根据CurrentMoveSpeed和水平输入更新速度
    /// - 将Y轴速度同步到Animator，用于控制下落/上升动画
    ///
    /// 爬墙物理:
    /// - X轴速度 = 0 (完全贴住墙壁)
    /// - Y轴速度 = climbInput.y × climbSpeed (由玩家输入控制)
    ///
    /// 正常物理:
    /// - X轴速度 = moveInputHorizontal × CurrentMoveSpeed
    /// - Y轴速度 = 保持不变(由重力和跳跃控制)
    /// </summary>
    private void FixedUpdate()
    {
        // 判断爬墙状态并应用对应的物理
        if (IsClimbing)
        {
            // 爬墙模式: X轴锁定为0(贴在墙上), Y轴由爬墙输入控制
            rb.velocity = new Vector2(0, climbInput.y * climbSpeed);

            // 同步爬墙速度到Animator的climbSpeed参数
            // 用于驱动爬墙动画的混合(向上/停止/向下)
            animator.SetFloat(AnimationStrings.climbSpeed, climbInput.y);
        }
        else
        {
            // 正常模式: 使用水平输入和当前速度
            rb.velocity = new Vector2(moveInputHorizontal * CurrentMoveSpeed, rb.velocity.y);
        }

        // 将当前Y轴速度同步到Animator
        // 用于控制下落/上升动画，以及到达最高点时的转换
        animator.SetFloat(AnimationStrings.yVelocity, rb.velocity.y);
    }

    /// <summary>
    /// 移动输入回调函数
    /// 由Input System在输入事件发生时调用
    ///
    /// 参数说明:
    /// - context: 输入事件的上下文，包含输入值和事件类型
    ///
    /// 功能:
    /// - 读取移动输入的Vector2值(WASD或摇杆)
    /// - 分离处理水平输入(X轴/A/D)和垂直输入(Y轴/W/S)
    /// - 水平输入驱动行走/奔跑动画和角色朝向
    /// - 垂直输入用于爬墙系统(W/S控制上下爬行)
    ///
    /// 爬墙逻辑:
    /// - 检查是否接触墙壁(IsOnWall)
    /// - 检查是否有垂直输入(moveInputVertical ≠ 0)
    /// - 如果两者都满足，进入爬墙状态(IsClimbing = true)
    /// - 爬墙时禁止水平移动，IsMoving = false
    /// - 爬墙时保持朝向，不改变facing
    /// </summary>
    public void OnMove(InputAction.CallbackContext context)
    {
        // 从输入事件读取Vector2值(来自WASD键或左摇杆)
        moveInput = context.ReadValue<Vector2>();

        // 分离水平和垂直输入分量
        // 水平输入(A/D): 用于行走/奔跑
        moveInputHorizontal = moveInput.x;

        // 垂直输入(W/S): 用于爬墙系统
        moveInputVertical = moveInput.y;

        // 同时保存给爬墙使用
        climbInput = moveInput;

        // 爬墙逻辑判断
        // 条件: 接触墙壁 && 有垂直输入 && 允许移动
        if (touchingDirections.IsOnWall && moveInputVertical != 0 && CanMove)
        {
            // 进入爬墙状态
            IsClimbing = true;
        }
        else if (!touchingDirections.IsOnWall || moveInputVertical == 0)
        {
            // 退出爬墙状态: 离开墙壁 或 没有垂直输入
            IsClimbing = false;
        }

        // 根据爬墙状态更新行走状态和朝向
        if (!IsClimbing)
        {
            // 正常模式: 判断是否行走，更新朝向
            IsMoving = moveInputHorizontal != 0;
            SetFacingDirection(moveInput);
        }
        else
        {
            // 爬墙模式: 禁止水平移动，保持朝向
            IsMoving = false;
            // 朝向保持不变，不调用SetFacingDirection
        }
    }

    /// <summary>
    /// 设置角色朝向函数
    ///
    /// 根据水平输入的X分量判断角色应该朝向的方向
    /// - moveInput.x > 0 -> 朝向右侧
    /// - moveInput.x < 0 -> 朝向左侧
    /// - moveInput.x = 0 -> 保持当前朝向
    ///
    /// 说明: 仅检查X分量(水平方向)
    ///       Y分量(垂直方向/W/S)不影响朝向
    ///
    /// 参数:
    /// - moveInput: 移动输入向量
    /// </summary>
    private void SetFacingDirection(Vector2 moveInput)
    {
        // 如果输入向右且当前朝向左侧，则改为朝向右侧
        if (moveInput.x > 0 && !IsFacingRight)
        {
            // 设置朝向为右
            IsFacingRight = true;
        }
        // 如果输入向左且当前朝向右侧，则改为朝向左侧
        else if (moveInput.x < 0 && IsFacingRight)
        {
            // 设置朝向为左
            IsFacingRight = false;
        }
        // 注意: 当moveInput.x = 0(包括只按W/S)时，朝向不变
    }

    /// <summary>
    /// 奔跑输入回调函数
    /// 由Input System在按下/释放奔跑键时调用(默认Shift键)
    ///
    /// 参数说明:
    /// - context: 输入事件的上下文
    ///   - context.started: 键按下时
    ///   - context.canceled: 键释放时
    ///
    /// 功能:
    /// - 按下时设置IsRunning=true，启用奔跑状态
    /// - 释放时设置IsRunning=false，返回行走状态
    /// </summary>
    public void OnRun(InputAction.CallbackContext context)
    {
        // 检查输入事件类型
        if (context.started)
        {
            // 奔跑键按下 - 启用奔跑
            IsRunning = true;
        }
        else if (context.canceled)
        {
            // 奔跑键释放 - 禁用奔跑
            IsRunning = false;
        }
    }

    /// <summary>
    /// 跳跃输入回调函数
    /// 由Input System在按下空格键时调用
    ///
    /// 跳跃条件(二选一):
    /// - 地面跳跃: 在地面上(IsGrounded = true)
    /// - 壁跳: 正在爬墙(IsClimbing = true)且接触墙壁(IsOnWall = true)
    ///
    /// 前置条件:
    /// - context.started: 空格键刚按下
    /// - CanMove: 允许移动(不在攻击等特殊状态)
    ///
    /// 功能:
    /// - 触发Animator的跳跃动画
    /// - 给予Y轴速度(jumpImpules)实现向上运动
    /// - 如果是壁跳，给予横向冲力(离墙方向)，实现推离墙壁
    /// - 壁跳后立即退出爬墙状态
    /// </summary>
    public void OnJump(InputAction.CallbackContext context)
    {
        // 检查是否满足跳跃条件
        if (context.started && CanMove)
        {
            // 支持地面跳跃或壁跳
            bool canJumpFromGround = touchingDirections.IsGrounded;
            bool canJumpFromWall = IsClimbing && touchingDirections.IsOnWall;

            if (canJumpFromGround || canJumpFromWall)
            {
                // 触发Animator的跳跃动画
                animator.SetTrigger(AnimationStrings.jumpTrigger);

                if (canJumpFromWall)
                {
                    // 壁跳逻辑: 给予离墙的横向冲力 + 向上冲力
                    // 横向冲力大小: 8f(可在Inspector中调整)
                    // 方向: 与当前朝向相反(推离墙壁)
                    float wallJumpForce = 8f;
                    float horizontalForce = IsFacingRight ? -wallJumpForce : wallJumpForce;

                    // 设置速度: 横向冲力 + 向上冲力
                    rb.velocity = new Vector2(horizontalForce, jumpImpules);

                    // 立即退出爬墙状态
                    IsClimbing = false;
                }
                else
                {
                    // 地面跳跃: 保持X轴速度，只改变Y轴
                    rb.velocity = new Vector2(rb.velocity.x, jumpImpules);
                }
            }
        }
    }

    /// <summary>
    /// 攻击输入回调函数
    /// 由Input System在按下攻击键时调用(默认Z键或J键)
    ///
    /// 参数说明:
    /// - context: 输入事件的上下文
    ///   - context.started: 攻击键刚按下
    ///
    /// 功能:
    /// - 触发Animator的攻击动画
    /// - 动画系统会自动控制CanMove参数，禁止攻击时的移动
    /// </summary>
    public void OnAttack(InputAction.CallbackContext context)
    {
        // 检查攻击键是否刚按下
        if (context.started)
        {
            // 触发Animator的攻击动画
            animator.SetTrigger(AnimationStrings.attackTrigger);
        }
    }
}