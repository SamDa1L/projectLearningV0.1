using System.Collections.Generic;
using CastleDB.Runtime;
using UnityEngine;

public partial class NpcAbilityController : MonoBehaviour
{

    /// <summary>
    /// 仅 Tick“待释放的施法请求”（如果存在），与 DetectionZone 的 role/targets 无关。
    /// 返回 true 表示当前仍处于“施法等待释放”阶段，调用方应跳过近战逻辑。
    /// </summary>
    public bool TickPendingCast()
    {
        if (!_hasPendingCast)
        {
            return false;
        }

        float now = Time.time;

        // 兜底：如果没有配置 AnimationEvent（或动画没走到事件帧），允许按 releaseDelay 走延迟发射
        if (_pendingCast.fallbackReleaseAtTime > 0f && now >= _pendingCast.fallbackReleaseAtTime)
        {
            ReleasePendingCast();
            return true;
        }

        // 超时保护：避免因为“动画事件未触发”导致 NPC 永久卡在 pending 状态
        if (_pendingCast.expiresAtTime > 0f && now > _pendingCast.expiresAtTime)
        {
            PendingCastKind kind = _pendingCast.kind;
            string abilityId = _pendingCast.abilityId ?? "";
            _hasPendingCast = false;
            _pendingCast = default;

            string expectedEvent = kind == PendingCastKind.Buff ? "OnBuffRelease" : "OnAbilityRelease";
            Debug.LogWarning(
                $"[NpcAbilityController] 施法等待超时，可能缺少 AnimationEvent: {expectedEvent}（abilityId='{abilityId}'）",
                this);
            return false;
        }

        return true;
    }

    /// <summary>
    /// AnimationEvent 入口：释放当前排队的投射物施法（如果存在）。
    /// 命名需与 PlayerController.OnAbilityRelease() 保持一致，方便复用同一套动画事件。
    /// </summary>
    public void OnAbilityRelease()
    {
        if (!_hasPendingCast || _pendingCast.kind != PendingCastKind.Projectile)
        {
            return;
        }

        ReleasePendingCast();
    }

    /// <summary>
    /// AnimationEvent 入口：释放当前排队的 Buff/StatModifier（如果存在）。
    /// 与 OnAbilityRelease 分开，便于在 AnimatorEvent 下拉列表中明确区分 projectile/buff。
    /// </summary>
    public void OnBuffRelease()
    {
        if (!_hasPendingCast || _pendingCast.kind != PendingCastKind.Buff)
        {
            return;
        }

        ReleasePendingCast();
    }

    private void ReleasePendingCast()
    {
        if (!_hasPendingCast)
        {
            return;
        }

        var cast = _pendingCast;
        _hasPendingCast = false;
        _pendingCast = default;

        switch (cast.kind)
        {
            case PendingCastKind.Projectile:
                if (cast.projectile == null || string.IsNullOrWhiteSpace(cast.abilityId))
                {
                    return;
                }

                SpawnProjectile(cast.abilityId, cast.projectile, cast.onHitNodes, cast.directionSign);
                return;

            case PendingCastKind.Buff:
                ApplyPendingBuff(cast);
                return;
        }
    }
}
