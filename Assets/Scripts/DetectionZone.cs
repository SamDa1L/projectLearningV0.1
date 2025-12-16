using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class DetectionZone : MonoBehaviour
{
    [Header("事件（Event-Driven）")]
    public UnityEvent OnTargetEnter;        // 有目标进入时触发
    public UnityEvent OnTargetExit;         // 有目标离开时触发
    public UnityEvent NoColliderRemain;     // 所有目标都离开时触发（兼容旧代码）

    [Header("检测结果")]
    public List<Collider2D> detectedColliders = new List<Collider2D>();

    private Collider2D col;

    private void Awake()
    {
        col = GetComponent<Collider2D>();
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        detectedColliders.Add(collision);

        // 触发"目标进入"事件
        OnTargetEnter?.Invoke();
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        detectedColliders.Remove(collision);

        // 触发"目标离开"事件
        OnTargetExit?.Invoke();

        // 如果所有目标都离开，触发"无目标"事件
        if(detectedColliders.Count <= 0)
        {
            NoColliderRemain?.Invoke();
        }
    }
}
