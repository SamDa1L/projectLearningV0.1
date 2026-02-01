using UnityEngine;

public partial class PlayerController
{
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
            float speedMultiplier = statLayer != null ? statLayer.MoveSpeedMultiplier : 1f;

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
                            return runSpeed * speedMultiplier;
                        }
                        else
                        {
                            return walkSpeed * speedMultiplier;
                        }
                    }
                    else
                    {
                        // 在空中 - 返回降低的移动速度
                        return airWalkSpeed * speedMultiplier;
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

    /// <summary>是否在移动的内部字段（阶段 3B：改为 public 以支持能力系统）</summary>
    [SerializeField]
    public bool _isMoving = false;

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

    /// <summary>是否在奔跑的内部字段（阶段 3B：改为 public 以支持能力系统）</summary>
    [SerializeField]
    public bool _isRunning = false;

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

    public Transform AbilityFirePoint => abilityFirePoint != null ? abilityFirePoint : transform;

    public bool IsAlive
    {
        get
        {
            return animator.GetBool(AnimationStrings.isAlive);
        }
    }

    /// <summary>是否正在爬墙的内部字段（阶段 3B：改为 public 以支持能力系统）</summary>
    [SerializeField]
    public bool _isClimbing = false;

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
}

