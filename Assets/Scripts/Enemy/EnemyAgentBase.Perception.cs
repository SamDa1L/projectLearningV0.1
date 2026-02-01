using System.Collections.Generic;
using UnityEngine;

public abstract partial class EnemyAgentBase
{
    // ===== IAgentPerception 接口实现 =====

    // ===== 阵营过滤（0.5 Summon 扩展）=====
    // 说明：敌对关系只允许 Enemy <-> Friend；其余组合均不敌对（含 Neutral）。
    // 这里对“检测目标”进行过滤，避免友军/中立单位被当作战斗目标。

    private FactionId GetSelfFaction()
    {
        return FactionUtility.GetFaction(gameObject);
    }

    private static bool IsHostileCollider(Collider2D col, FactionId selfFaction)
    {
        if (col == null)
        {
            return false;
        }

        FactionId targetFaction = FactionUtility.GetFaction(col.gameObject);
        return FactionUtility.IsHostile(selfFaction, targetFaction);
    }

    private int CountHostileColliders(List<Collider2D> colliders)
    {
        if (colliders == null || colliders.Count == 0)
        {
            return 0;
        }

        FactionId selfFaction = GetSelfFaction();
        int count = 0;

        for (int i = 0; i < colliders.Count; i++)
        {
            if (IsHostileCollider(colliders[i], selfFaction))
            {
                count++;
            }
        }

        return count;
    }

    private List<Collider2D> FilterHostileColliders(List<Collider2D> colliders)
    {
        var result = new List<Collider2D>();
        if (colliders == null || colliders.Count == 0)
        {
            return result;
        }

        FactionId selfFaction = GetSelfFaction();
        for (int i = 0; i < colliders.Count; i++)
        {
            var col = colliders[i];
            if (IsHostileCollider(col, selfFaction))
            {
                result.Add(col);
            }
        }

        return result;
    }

    /// <summary>
    /// 获取默认战斗检测区的检测目标（优先 PrimaryAttack，回退 SecondaryAttack）
    ///
    /// v0.2设计（方案A）：
    /// - 优先查询zoneBindings中role=PrimaryAttack的检测区；若不存在，则回退到role=SecondaryAttack
    /// - zoneBindings是唯一的数据源，通过GetDetectedTargetsForRole()访问
    /// - 若 PrimaryAttack/SecondaryAttack 都缺失，返回空列表
    ///
    /// 返回值：
    /// - 如果配置正确，返回 DZ_Attack（PrimaryAttack）或 DZ_Ability（SecondaryAttack）的目标列表
    /// - 如果配置不正确，返回空列表
    /// </summary>
    public virtual List<Collider2D> GetDetectedTargets()
    {
        // Prefab 约定：PrimaryAttack / SecondaryAttack 至少一个。
        // 默认优先返回 PrimaryAttack；若未配置 PrimaryAttack，则回退到 SecondaryAttack。
        var primary = GetZone(DetectionZoneBinding.Role.PrimaryAttack);
        if (primary != null && primary.detectedColliders != null)
        {
            return FilterHostileColliders(primary.detectedColliders);
        }

        var secondary = GetZone(DetectionZoneBinding.Role.SecondaryAttack);
        if (secondary != null && secondary.detectedColliders != null)
        {
            return FilterHostileColliders(secondary.detectedColliders);
        }

        return new List<Collider2D>();
    }

    public virtual bool IsTargetInRange(Transform target, float range)
    {
        if (target == null)
            return false;

        float distance = Vector2.Distance(cacheTransform.position, target.position);
        return distance <= range;
    }

    /// <summary>
    /// 根据角色获取特定的检测目标（v0.2核心API）
    ///
    /// 这是v0.2多检测区支持的主要方法，用于子类访问各类检测区的目标
    ///
    /// 使用示例：
    /// - 攻击目标：GetDetectedTargetsForRole(DetectionZoneBinding.Role.PrimaryAttack)
    /// - 崖边检测：GetDetectedTargetsForRole(DetectionZoneBinding.Role.Cliff)
    /// - 警戒范围：GetDetectedTargetsForRole(DetectionZoneBinding.Role.Alert)
    ///
    /// 实现逻辑：
    /// - 遍历zoneBindings查找对应role的binding
    /// - 如果找到且zone有效，返回该zone的目标列表
    /// - 如果未找到或无效，返回空列表（不会报错，子类需自行处理）
    ///
    /// 性能说明：
    /// - 遍历zoneBindings（通常3-5个元素），O(n)时间复杂度
    /// - 如果频繁调用同一role，建议在Initialize()中缓存结果
    /// </summary>
    /// <param name="role">检测区的角色/用途</param>
    /// <returns>该角色对应的检测区中的目标列表，未找到则返回空列表</returns>
    public virtual List<Collider2D> GetDetectedTargetsForRole(DetectionZoneBinding.Role role)
    {
        // 遍历zoneBindings查找对应的binding
        foreach (var binding in zoneBindings)
        {
            if (binding.role == role && binding.zone != null)
                return FilterHostileColliders(binding.zone.detectedColliders);
        }

        // 未找到该角色的检测区，返回空列表
        return new List<Collider2D>();
    }

    /// <summary>
    /// 根据角色获取对应的DetectionZone组件
    /// </summary>
    public virtual DetectionZone GetZone(DetectionZoneBinding.Role role)
    {
        foreach (var binding in zoneBindings)
        {
            if (binding.role == role && binding.zone != null)
                return binding.zone;
        }
        return null;
    }
}

