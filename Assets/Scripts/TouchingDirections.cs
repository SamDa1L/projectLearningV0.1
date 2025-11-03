using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 碰撞检测系统脚本
///
/// 功能说明:
/// - 使用CapsuleCollider2D.Cast()方法进行高效的碰撞检测
/// - 检测角色与环境的接触状态：地面、墙壁、天花板
/// - 通过属性与动画系统交互，实时更新Animator的相关参数
/// - 为PlayerController提供关键的物理感知数据
///
/// 检测原理:
/// - Cast()方法沿指定方向投射碰撞体，返回接触数量
/// - 如果返回值 > 0，表示有碰撞发生
///
/// 依赖组件:
/// - CapsuleCollider2D: 角色的胶囊形碰撞体
/// - Animator: 动画系统，用于同步状态参数
///
/// 关键属性:
/// - IsGrounded: 是否在地面上(跳跃条件)
/// - IsOnWall: 是否接触墙壁(移动限制)
/// - IsOnCeiling: 是否接触天花板(碰撞反馈)
/// </summary>
public class TouchingDirections : MonoBehaviour
{
    /// <summary>碰撞过滤器 - 定义检测哪些图层的碰撞体</summary>
    public ContactFilter2D castFilter;

    /// <summary>地面检测距离 - 单位为米，0.05f足以检测脚下是否有地面</summary>
    public float groundDistance = 0.05f;

    /// <summary>墙壁检测距离 - 0.2f提供合理的预检测范围，防止卡墙</summary>
    public float wallDistance = 0.2f;

    /// <summary>天花板检测距离 - 0.05f足以检测头顶是否有障碍</summary>
    public float ceilingDistance = 0.05f;

    /// <summary>胶囊形碰撞体组件引用 - 用于执行Cast检测</summary>
    CapsuleCollider2D touchingCol;

    /// <summary>动画系统组件引用 - 用于同步状态参数</summary>
    Animator animator;

    /// <summary>
    /// 地面碰撞检测结果数组 - 大小为5
    /// 说明: 创建大小为5的数组是为了存储最多5个检测到的碰撞体
    ///       Cast()方法会将接触到的碰撞体信息写入此数组
    ///       实际接触数可能少于5个，返回值会指示真实数量
    /// </summary>
    RaycastHit2D[] groundHits = new RaycastHit2D[5];

    /// <summary>
    /// 墙壁碰撞检测结果数组 - 大小为5
    /// 说明: 用途同上，存储墙壁检测的结果
    /// </summary>
    RaycastHit2D[] wallHits = new RaycastHit2D[5];

    /// <summary>
    /// 天花板碰撞检测结果数组 - 大小为5
    /// 说明: 用途同上，存储天花板检测的结果
    /// </summary>
    RaycastHit2D[] ceilingHits = new RaycastHit2D[5];

    /// <summary>是否在地面上的内部字段</summary>
    [SerializeField]
    private bool _isGrounded;

    /// <summary>
    /// 是否在地面上的属性
    ///
    /// getter: 返回当前地面接触状态
    /// setter: 设置地面状态并同步到Animator
    ///
    /// 用途: 控制跳跃动画的播放、防止地面多次跳跃
    /// 影响: PlayerController通过此属性判断是否允许跳跃
    /// </summary>
    public bool IsGrounded
    {
        get
        {
            return _isGrounded;
        }
        private set
        {
            _isGrounded = value;
            // 同步更新Animator的isGrounded参数
            animator.SetBool(AnimationStrings.isGrounded, value);
        }
    }

    /// <summary>是否接触墙壁的内部字段</summary>
    [SerializeField]
    private bool _isOnWall;

    /// <summary>
    /// 是否接触墙壁的属性
    ///
    /// getter: 返回当前墙壁接触状态
    /// setter: 设置墙壁状态并同步到Animator
    ///
    /// 用途: 防止角色与墙壁重叠，控制墙壁滑动动画
    /// 影响: PlayerController在CurrentMoveSpeed中检查此属性，禁止墙壁移动
    /// 设计: 预留了未来的墙壁滑动、爬墙等高级机制
    /// </summary>
    public bool IsOnWall
    {
        get
        {
            return _isOnWall;
        }
        private set
        {
            _isOnWall = value;
            // 同步更新Animator的isOnWall参数
            animator.SetBool(AnimationStrings.isOnWall, value);
        }
    }

    /// <summary>是否接触天花板的内部字段</summary>
    [SerializeField]
    private bool _isOnCeiling;

    /// <summary>
    /// 墙壁检测方向属性(只读)
    ///
    /// 逻辑: 根据角色朝向(localScale.x)确定检测方向
    /// - localScale.x > 0: 角色朝右 -> 检测方向为右(Vector2.right)
    /// - localScale.x < 0: 角色朝左 -> 检测方向为左(Vector2.left)
    ///
    /// 优点: 无需单独的朝向变量，直接通过缩放计算
    /// 用途: 在FixedUpdate中用于检测角色前方是否有墙壁
    /// </summary>
    private Vector2 wallCheckDirection => gameObject.transform.localScale.x > 0 ? Vector2.right : Vector2.left;

    /// <summary>
    /// 是否接触天花板的属性
    ///
    /// getter: 返回当前天花板接触状态
    /// setter: 设置天花板状态并同步到Animator
    ///
    /// 用途: 防止角色穿透天花板，控制撞头等反馈
    /// 影响: 预留用于撞头动画、速度限制等机制
    /// 设计: 为未来功能扩展做准备
    /// </summary>
    public bool IsOnCeiling
    {
        get
        {
            return _isOnCeiling;
        }
        private set
        {
            _isOnCeiling = value;
            // 同步更新Animator的isOnCeiling参数
            animator.SetBool(AnimationStrings.isOnCeiling, value);
        }
    }

    /// <summary>
    /// Awake生命周期函数
    /// 在脚本实例初始化时调用，用于获取组件引用
    /// </summary>
    private void Awake()
    {
        // 获取当前GameObject上的CapsuleCollider2D组件
        touchingCol = GetComponent<CapsuleCollider2D>();

        // 获取当前GameObject上的Animator组件
        animator = GetComponent<Animator>();
    }

    // Start is called before the first frame update
    void Start()
    {

    }

    /// <summary>
    /// FixedUpdate生命周期函数
    /// 每个物理帧调用一次(默认0.02秒)
    ///
    /// 功能: 执行三个碰撞检测
    /// 1. 地面检测 - 向下投射，检测脚下是否有地面
    /// 2. 墙壁检测 - 沿角色朝向投射，检测前方是否有墙壁
    /// 3. 天花板检测 - 向上投射，检测头顶是否有障碍
    ///
    /// Cast()调用说明:
    /// - 参数1: 检测方向(Vector2)
    /// - 参数2: 碰撞过滤器(决定检测哪些图层)
    /// - 参数3: 用于接收结果的RaycastHit2D数组
    /// - 参数4: 检测距离(有效范围)
    /// - 返回值: int 表示接触到的碰撞体数量
    /// </summary>
    void FixedUpdate()
    {
        // 地面检测: 向下投射，距离为groundDistance(0.05f)
        // 如果投射结果数 > 0，表示脚下有碰撞体(地面)
        IsGrounded = touchingCol.Cast(Vector2.down, castFilter, groundHits, groundDistance) > 0;

        // 墙壁检测: 沿角色朝向投射，距离为wallDistance(0.2f)
        // 如果投射结果数 > 0，表示前方有碰撞体(墙壁)
        IsOnWall = touchingCol.Cast(wallCheckDirection, castFilter, wallHits, wallDistance) > 0;

        // 天花板检测: 向上投射，距离为ceilingDistance(0.05f)
        // 如果投射结果数 > 0，表示头顶有碰撞体(天花板)
        IsOnCeiling = touchingCol.Cast(Vector2.up, castFilter, ceilingHits, ceilingDistance) > 0;
    }

}
