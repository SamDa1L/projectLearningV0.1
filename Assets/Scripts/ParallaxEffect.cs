using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor.Rendering;
using UnityEngine;

/// <summary>
/// 视差滚动效果脚本
///
/// 功能说明:
/// - 实现视差滚动背景效果
/// - 根据摄像机移动计算背景层的偏移距离
/// - 根据物体与摄像机的深度关系(Z轴)计算不同的移动速度
/// - 营造多层次的视觉深度感
///
/// 视差原理:
/// - 距离摄像机越近的物体应该移动得更快(跟随摄像机)
/// - 距离摄像机越远的物体应该移动得更慢(呈现深度感)
/// - 通过parallaxFactor来调整移动速度
///
/// 参考资源:
/// - YouTube视频: https://www.youtube.com/watch?v=tMXgLBwtsvI
/// - 详细讲解了视差滚动的数学原理和实现方法
///
/// 配置要求:
/// - 在Inspector中设置Camera和FollowTarget
/// - 不同的背景层应该有不同的Z值(depth)
/// - Z值越大(离摄像机越近)，parallaxFactor越大，移动越快
///
/// 常见用法:
/// - 背景图片Z = 10(距离最远，移动最慢)
/// - 中景图片Z = 5(中间距离，移动中等)
/// - 前景装饰Z = 0或负数(距离很近，移动很快或跟随)
/// </summary>

// 背景视差滚动效果参考教程: https://www.youtube.com/watch?v=tMXgLBwtsvI

public class ParallaxEffect : MonoBehaviour
{
    /// <summary>游戏摄像机引用 - 用于获取摄像机的位置和投影参数</summary>
    public Camera cam;

    /// <summary>跟随目标的Transform - 通常是主角，用于确定视差效果的参考点</summary>
    public Transform followTarget;


    /// <summary>
    /// 视差效果对象的起始位置
    /// 说明: 在Start()时记录当前位置，用于计算相对移动量
    /// 用途: 作为视差计算的基准点，所有移动都基于这个位置进行偏移
    /// </summary>
    Vector2 startingPosition;

    /// <summary>
    /// 视差效果对象的起始Z值
    /// 说明: 记录对象初始的深度值，用于维持对象的深度层级
    /// 用途: 确保视差效果播放后，物体的Z层级不会改变
    /// </summary>
    float startingZ;

    /// <summary>
    /// 摄像机相对于起始位置的移动距离(只读属性)
    ///
    /// 计算逻辑:
    /// - 获取摄像机当前位置(Vector3) -> 转换为Vector2(忽略Z轴)
    /// - 减去记录的起始位置
    /// - 结果是摄像机从Start以来移动了多少
    ///
    /// 返回值: Vector2 摄像机的移动向量
    /// </summary>
    Vector2 camMoveSinceStart => (Vector2)cam.transform.position - startingPosition;

    /// <summary>
    /// 视差对象与摄像机目标之间的Z轴距离(只读属性)
    ///
    /// 计算逻辑:
    /// - 获取当前对象的Z位置
    /// - 减去摄像机跟随目标的Z位置
    /// - 结果表示对象相对于主角的深度差
    ///
    /// 返回值: float Z轴距离(正数=在主角前方，负数=在主角后方)
    ///
    /// 用途: 确定视差强度 - 距离越大，视差效果越明显
    /// </summary>
    float zDistanceFromTaget => transform.position.z - followTarget.transform.position.z;

    /// <summary>
    /// 摄像机的有效裁剪范围(只读属性)
    ///
    /// 计算逻辑:
    /// - 获取摄像机当前的Z位置
    /// - 根据zDistanceFromTaget的正负判断使用哪个裁剪平面
    ///   - zDistanceFromTaget > 0: 对象在摄像机前方 -> 使用farClipPlane
    ///   - zDistanceFromTaget < 0: 对象在摄像机后方 -> 使用nearClipPlane
    /// - 返回有效的视锥范围，用于计算视差因子
    ///
    /// 返回值: float 摄像机的有效裁剪范围
    ///
    /// 原理: Unity的摄像机投影公式中需要考虑物体相对于摄像机的深度
    /// </summary>
    float clippingPlane => (cam.transform.position.z + (zDistanceFromTaget > 0 ? cam.farClipPlane : cam.nearClipPlane));

    /// <summary>
    /// 视差因子(只读属性)
    ///
    /// 计算逻辑:
    /// parallaxFactor = |Z距离| / 摄像机裁剪范围
    ///
    /// 说明:
    /// - parallaxFactor是一个0~1之间的比例系数
    /// - 当parallaxFactor = 0时，对象不移动(完全在摄像机处)
    /// - 当parallaxFactor = 1时，对象跟随摄像机100%移动
    /// - 当parallaxFactor介于0~1时，对象以相应比例跟随摄像机
    ///
    /// 效果:
    /// - 远处背景(Z值大)-> parallaxFactor大 -> 移动多 -> 显得近
    /// - 近处前景(Z值小)-> parallaxFactor小 -> 移动少 -> 显得远
    ///
    /// 返回值: float 0~1之间的视差因子
    /// </summary>
    float parallaxFactor => Mathf.Abs(zDistanceFromTaget) / clippingPlane;

    /// <summary>
    /// Start生命周期函数
    /// 在游戏开始时调用一次
    ///
    /// 功能:
    /// - 记录初始位置，作为视差计算的基准
    /// - 记录初始Z值，确保深度不变
    /// </summary>
    void Start()
    {
        // 记录当前位置的X和Y分量作为起始位置
        startingPosition = transform.position;

        // 记录当前的Z值(深度)，用于保持视差效果后的深度层级
        startingZ = transform.position.z;

    }

    /// <summary>
    /// Update生命周期函数
    /// 每帧调用一次
    ///
    /// 功能:
    /// - 计算视差效果的新位置
    /// - 更新物体位置以实现视差滚动效果
    ///
    /// 执行流程:
    /// 1. 获取摄像机的移动距离(camMoveSinceStart)
    /// 2. 计算视差因子(parallaxFactor)
    /// 3. 应用视差因子到摄像机移动
    /// 4. 加上起始位置得到最终位置
    /// 5. 保持Z值不变，只改变X和Y
    /// </summary>
    void Update()
    {
        // 计算新位置 = 起始位置 + 摄像机移动量 × 视差因子
        //
        // 例如:
        // - 摄像机移动了(5, 0)，parallaxFactor = 0.5
        // - 则背景移动 (5, 0) × 0.5 = (2.5, 0)
        // - 最终位置 = 起始位置 + (2.5, 0)
        Vector2 newPosition = startingPosition + camMoveSinceStart * parallaxFactor;

        // 更新对象位置
        // X和Y使用计算后的新位置(包含视差效果)
        // Z保持不变(startingZ)，确保深度层级正确
        transform.position = new Vector3(newPosition.x, newPosition.y, startingZ);

    }
}
