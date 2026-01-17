using UnityEngine;

/// <summary>
/// 伤害拦截器（Phase 7）
/// 在 Damageable 扣血之前调用，用于护盾/减伤/反伤等“改写伤害”的需求。
/// </summary>
public interface IDamageInterceptor
{
    /// <summary>
    /// 扣血前回调：允许修改 damage（建议保持 >=0）；hitPoint 为命中点世界坐标。
    /// </summary>
    void BeforeDamage(ref int damage, Vector2 hitPoint);
}

