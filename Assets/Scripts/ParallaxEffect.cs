using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor.Rendering;
using UnityEngine;

//场景视差效果参考视频：https://www.youtube.com/watch?v=tMXgLBwtsvI

public class ParallaxEffect : MonoBehaviour
{
    public Camera cam;
    public Transform followTarget;


    //视差效果对象的初始位置
    Vector2 startingPosition;

    //视差效果对象的初始Z值
    float startingZ;

    //计算摄像机从游戏开始到当前帧之间的移动距离，从而驱动背景层的偏移。
    Vector2 camMoveSinceStart => (Vector2)cam.transform.position - startingPosition;

    //计算当前物体与目标对象（通常是摄像机）在 Z 轴上的距离差，用于实现视差滚动（Parallax Effect）时判断背景层的深度。
    float zDistanceFromTaget => transform.position.z - followTarget.transform.position.z;

    //计算视差滚动中背景层的参考深度范围，它决定了背景层的移动比例（parallax factor）应该基于摄像机的哪一个裁剪面（near 或 far）来归一化。
    float clippingPlane => (cam.transform.position.z + (zDistanceFromTaget > 0 ? cam.farClipPlane : cam.nearClipPlane));

    //计算背景层的移动因子 parallaxFactor，从而让远近背景层以不同速度移动，增强空间深度感。
    float parallaxFactor => Mathf.Abs(zDistanceFromTaget) / clippingPlane;

    // Start is called before the first frame update
    void Start()
    {
        startingPosition = transform.position;
        startingZ = transform.position.z;
        
    }

    // Update is called once per frame
    void Update()
    {
        //当目标开始移动，视差效果沿着相同距离移动
        Vector2 newPosition = startingPosition + camMoveSinceStart * parallaxFactor;

        //x和y的改变基于目标移动速度乘以视差效果参数，但是z值不变
        transform.position = new Vector3(newPosition.x, newPosition.y, startingZ);
        
    }
}
