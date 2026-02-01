using UnityEngine;

/// <summary>
/// 将“阵营”落到 Unity Layer（通过重映射 layer 实现）。
/// 说明：
/// - 本项目战斗/检测强依赖 Physics2D Layer 碰撞矩阵，所以最稳的实现是“换阵营=换 Layer”。 
/// - 目前采用映射：Enemy <-> Player、EnemyHitBox <-> PlayerHitBox。
/// </summary>
public static class FactionLayerApplier
{
    private static readonly int LayerEnemy = LayerMask.NameToLayer("Enemy");
    private static readonly int LayerFriend = LayerMask.NameToLayer("Player");

    private static readonly int LayerEnemyHitBox = LayerMask.NameToLayer("EnemyHitBox");
    private static readonly int LayerFriendHitBox = LayerMask.NameToLayer("PlayerHitBox");

    public static void Apply(GameObject root, FactionId faction)
    {
        if (root == null)
        {
            return;
        }

        int desiredBody = GetBodyLayer(faction);
        int desiredHitBox = GetHitBoxLayer(faction);

        // 仅重映射已知的“战斗相关 Layer”，其余 Layer（如 Pickup/Ground）保持不变，避免误伤系统外对象。
        var transforms = root.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            GameObject go = transforms[i].gameObject;
            int current = go.layer;

            if (current == LayerEnemy || current == LayerFriend)
            {
                go.layer = desiredBody;
            }
            else if (current == LayerEnemyHitBox || current == LayerFriendHitBox)
            {
                go.layer = desiredHitBox;
            }
        }
    }

    private static int GetBodyLayer(FactionId faction)
    {
        switch (faction)
        {
            case FactionId.Enemy:
                return LayerEnemy;
            case FactionId.Friend:
                return LayerFriend;
            case FactionId.Neutral:
                // Neutral 当前映射为 Default（后续如需更强隔离，可新增 Neutral/NeutralHitBox layer）。
                return 0;
            default:
                return LayerEnemy;
        }
    }

    private static int GetHitBoxLayer(FactionId faction)
    {
        switch (faction)
        {
            case FactionId.Enemy:
                return LayerEnemyHitBox;
            case FactionId.Friend:
                return LayerFriendHitBox;
            case FactionId.Neutral:
                return 0;
            default:
                return LayerEnemyHitBox;
        }
    }
}
