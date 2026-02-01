using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class DetectionZone : MonoBehaviour
{
    [Header("事件（Event-Driven）")]
    public UnityEvent OnTargetEnter = new UnityEvent();        // 有目标进入时触发
    public UnityEvent OnTargetExit = new UnityEvent();         // 有目标离开时触发
    public UnityEvent NoColliderRemain = new UnityEvent();     // 所有目标都离开时触发（兼容旧代码）
    public UnityEvent OnDetectedTargetsChanged = new UnityEvent(); // 检测目标变化时触发（0.3 统一事件）

    [Header("检测结果")]
    public List<Collider2D> detectedColliders = new List<Collider2D>();

    private Collider2D col;

    private void Awake()
    {
        EnsureUnityEvents();
        col = GetComponent<Collider2D>();
    }

    private void Reset()
    {
        EnsureUnityEvents();
        col = GetComponent<Collider2D>();
    }

    private void OnValidate()
    {
        EnsureUnityEvents();
    }

    private void EnsureUnityEvents()
    {
        // 确保 UnityEvent 不为 null（Editor 工具可能会 AddComponent 后立刻绑定 Persistent Listener）
        if (OnTargetEnter == null)
        {
            OnTargetEnter = new UnityEvent();
        }

        if (OnTargetExit == null)
        {
            OnTargetExit = new UnityEvent();
        }

        if (NoColliderRemain == null)
        {
            NoColliderRemain = new UnityEvent();
        }

        if (OnDetectedTargetsChanged == null)
        {
            OnDetectedTargetsChanged = new UnityEvent();
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        detectedColliders.Add(collision);

        // 触发"目标进入"事件
        OnTargetEnter?.Invoke();

        // 触发"检测目标变化"事件（0.3 统一事件）
        OnDetectedTargetsChanged?.Invoke();
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        detectedColliders.Remove(collision);

        // 触发"目标离开"事件
        OnTargetExit?.Invoke();

        // 触发"检测目标变化"事件（0.3 统一事件）
        OnDetectedTargetsChanged?.Invoke();

        // 如果所有目标都离开，触发"无目标"事件
        if(detectedColliders.Count <= 0)
        {
            NoColliderRemain?.Invoke();
        }
    }
}
