using UnityEngine;

public abstract partial class EnemyAgentBase
{
    // ===== 调试可视化 =====

    /// <summary>
    /// 在编辑器中显示Gizmos的选项开关
    /// </summary>
    [Header("Gizmos可视化")]
    [SerializeField] private bool showDetectionZoneGizmos = true;
    [SerializeField] private bool showGizmosInPlayMode = false;

#if UNITY_EDITOR

    protected virtual void OnDrawGizmosSelected()
    {
        if (cacheTransform == null)
            cacheTransform = transform;

        // 绘制基础位置
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(cacheTransform.position, 0.1f);

        // 绘制检测区Gizmos
        DrawDetectionZoneGizmos();
    }

    protected virtual void OnDrawGizmos()
    {
        // 在Play模式下，仅当启用选项时才绘制
        if (Application.isPlaying && !showGizmosInPlayMode)
            return;

        // 非Play模式或已启用Play模式显示时，绘制检测区
        if (!Application.isPlaying || showGizmosInPlayMode)
        {
            DrawDetectionZoneGizmos();
        }
    }

    /// <summary>
    /// 绘制所有检测区的Gizmos
    ///
    /// 颜色方案：
    /// - PrimaryAttack: 红色
    /// - SecondaryAttack: 橙色
    /// - Cliff: 蓝色
    /// - Wall: 紫色
    /// - Alert: 绿色
    /// - Lookout: 黄色
    /// - Custom: 灰色
    /// </summary>
    private void DrawDetectionZoneGizmos()
    {
        if (!showDetectionZoneGizmos || zoneBindings == null || zoneBindings.Count == 0)
            return;

        foreach (var binding in zoneBindings)
        {
            if (binding.zone == null)
                continue;

            // 根据Role选择颜色
            Color zoneColor = GetColorForRole(binding.role);
            Gizmos.color = zoneColor;

            // 获取检测区的Collider2D
            var collider = binding.zone.GetComponent<Collider2D>();
            if (collider != null)
            {
                DrawCollider2DGizmo(collider, zoneColor);
            }

            // 绘制标签和检测目标数量
            DrawDetectionZoneLabel(binding);
        }
    }

    /// <summary>
    /// 根据Role获取对应的颜色
    /// </summary>
    private Color GetColorForRole(DetectionZoneBinding.Role role)
    {
        return role switch
        {
            DetectionZoneBinding.Role.PrimaryAttack => new Color(1f, 0f, 0f, 0.3f), // 红色
            DetectionZoneBinding.Role.SecondaryAttack => new Color(1f, 0.5f, 0f, 0.3f), // 橙色
            DetectionZoneBinding.Role.Cliff => new Color(0f, 0f, 1f, 0.3f), // 蓝色
            DetectionZoneBinding.Role.Wall => new Color(0.6f, 0.2f, 1f, 0.3f), // 紫色
            DetectionZoneBinding.Role.Alert => new Color(0f, 1f, 0f, 0.3f), // 绿色
            DetectionZoneBinding.Role.Lookout => new Color(1f, 1f, 0f, 0.3f), // 黄色
            _ => new Color(0.5f, 0.5f, 0.5f, 0.3f) // 灰色（Custom）
        };
    }

    /// <summary>
    /// 绘制Collider2D的Gizmo
    /// 支持BoxCollider2D和CircleCollider2D
    /// </summary>
    private void DrawCollider2DGizmo(Collider2D collider, Color color)
    {
        Gizmos.color = color;
        var colliderTransform = collider.transform;

        if (collider is BoxCollider2D boxCollider)
        {
            // BoxCollider2D.offset/size 是 colliderTransform 的本地空间值。
            // 这里必须通过 TransformPoint 应用旋转/缩放（含负缩放翻转），否则翻转后 Gizmos 会画在错误位置。
            Vector2 offset = boxCollider.offset;
            Vector2 size = boxCollider.size;
            Vector2 half = size * 0.5f;

            Vector3[] corners = new Vector3[4]
            {
                colliderTransform.TransformPoint(offset + new Vector2(-half.x, -half.y)),
                colliderTransform.TransformPoint(offset + new Vector2( half.x, -half.y)),
                colliderTransform.TransformPoint(offset + new Vector2( half.x,  half.y)),
                colliderTransform.TransformPoint(offset + new Vector2(-half.x,  half.y))
            };

            // 绘制矩形边界
            Gizmos.DrawLine(corners[0], corners[1]);
            Gizmos.DrawLine(corners[1], corners[2]);
            Gizmos.DrawLine(corners[2], corners[3]);
            Gizmos.DrawLine(corners[3], corners[0]);

            // 绘制填充
            DrawFilledBox(corners, color);
        }
        else if (collider is CircleCollider2D circleCollider)
        {
            // CircleCollider2D.offset/radius 也是本地空间值，需要应用 Transform（含负缩放）。
            Vector2 offset = circleCollider.offset;
            float radius = circleCollider.radius;
            Vector3 center = colliderTransform.TransformPoint(offset);

            Vector3 axisX = colliderTransform.TransformVector(new Vector3(radius, 0f, 0f));
            Vector3 axisY = colliderTransform.TransformVector(new Vector3(0f, radius, 0f));
            float worldRadiusX = axisX.magnitude;
            float worldRadiusY = axisY.magnitude;

            DrawEllipse(center, worldRadiusX, worldRadiusY, color, 28);
        }
    }

    /// <summary>
    /// 绘制检测区的标签和信息
    /// </summary>
    private void DrawDetectionZoneLabel(DetectionZoneBinding binding)
    {
        if (binding.zone == null)
            return;

        Vector3 labelPos;
        var zoneCollider = binding.zone.GetComponent<Collider2D>();
        if (zoneCollider != null)
        {
            var bounds = zoneCollider.bounds;
            labelPos = bounds.center + Vector3.up * (bounds.extents.y + 0.15f);
        }
        else
        {
            labelPos = binding.zone.transform.position + Vector3.up * 0.5f;
        }

        // 获取检测到的目标数量
        int targetCount = binding.zone.detectedColliders.Count;
        string label = $"{binding.role}\n({targetCount})";

        TryDrawEditorLabel(labelPos, label);
    }

    /// <summary>
    /// 辅助方法：绘制填充的Box
    /// </summary>
    private void DrawFilledBox(Vector3[] corners, Color color)
    {
        // 这里简化处理，只绘制边框
        // 如需填充，可使用Mesh Gizmos (需要更复杂的实现)
    }

    /// <summary>
    /// 辅助方法：绘制填充的圆形
    /// </summary>
    private void DrawFilledCircle(Vector3 center, float radius, Color color, int segments)
    {
        Vector3[] points = new Vector3[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            float angle = (i / (float)segments) * 360f * Mathf.Deg2Rad;
            points[i] = center + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * radius;
        }

        // 绘制圆形线条
        for (int i = 0; i < segments; i++)
        {
            Gizmos.DrawLine(points[i], points[i + 1]);
        }
    }

    private static void DrawEllipse(Vector3 center, float radiusX, float radiusY, Color color, int segments)
    {
        Gizmos.color = color;

        if (segments < 8)
        {
            segments = 8;
        }

        Vector3 prev = center + new Vector3(radiusX, 0f, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float t = (i / (float)segments) * Mathf.PI * 2f;
            Vector3 next = center + new Vector3(Mathf.Cos(t) * radiusX, Mathf.Sin(t) * radiusY, 0f);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }

    private static void TryDrawEditorLabel(Vector3 labelPos, string label)
    {
#if UNITY_EDITOR
        var handlesType = System.Type.GetType("UnityEditor.Handles, UnityEditor");
        if (handlesType == null)
            return;

        var method = handlesType.GetMethod("Label", new[] { typeof(Vector3), typeof(string) });
        if (method == null)
            return;

        method.Invoke(null, new object[] { labelPos, label });
#endif
    }

    /// <summary>
    /// 在Scene视图显示调试信息面板
    /// </summary>
    private void UpdateDebugOverlay()
    {
        if (!debugStateOverlay)
            return;

        // 在Game视图左上角显示调试信息
        // 注：在Scene视图中显示需要使用Handles或GUILayout
    }

#endif
}

