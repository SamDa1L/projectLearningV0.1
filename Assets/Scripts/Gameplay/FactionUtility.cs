/// <summary>
/// 阵营敌对关系规则：
/// - Enemy 与 Friend 互为敌对
/// - 其余组合均不敌对（包括 Neutral 与任何阵营）
/// </summary>
public static class FactionUtility
{
    private static readonly int LayerEnemy = UnityEngine.LayerMask.NameToLayer("Enemy");
    private static readonly int LayerFriend = UnityEngine.LayerMask.NameToLayer("Player");
    private static readonly int LayerEnemyHitBox = UnityEngine.LayerMask.NameToLayer("EnemyHitBox");
    private static readonly int LayerFriendHitBox = UnityEngine.LayerMask.NameToLayer("PlayerHitBox");

    /// <summary>
    /// 将 CastleDB 的 faction 枚举值转换为运行时阵营。
    /// 约定（数据枚举顺序）：0=null，1=enemy，2=friend，3=Neutral。
    /// </summary>
    public static FactionId FromCastleDbFaction(int raw)
    {
        switch (raw)
        {
            case 1:
                return FactionId.Enemy;
            case 2:
                return FactionId.Friend;
            case 3:
                return FactionId.Neutral;
            case 0:
            default:
                return FactionId.None;
        }
    }

    public static bool IsHostile(FactionId a, FactionId b)
    {
        return (a == FactionId.Enemy && b == FactionId.Friend)
               || (a == FactionId.Friend && b == FactionId.Enemy);
    }

    /// <summary>
    /// 获取一个对象的阵营：
    /// - 优先读取父链上的 FactionMember
    /// - 若不存在，则根据 Layer 推断（Enemy/EnemyHitBox => Enemy；Player/PlayerHitBox => Friend）
    /// - 其余情况视为 Neutral
    /// </summary>
    public static FactionId GetFaction(UnityEngine.GameObject obj)
    {
        if (obj == null)
        {
            return FactionId.Neutral;
        }

        var member = obj.GetComponentInParent<FactionMember>();
        if (member != null)
        {
            return member.Faction;
        }

        int layer = obj.layer;
        if (layer == LayerEnemy || layer == LayerEnemyHitBox)
        {
            return FactionId.Enemy;
        }

        if (layer == LayerFriend || layer == LayerFriendHitBox)
        {
            return FactionId.Friend;
        }

        return FactionId.Neutral;
    }
}
