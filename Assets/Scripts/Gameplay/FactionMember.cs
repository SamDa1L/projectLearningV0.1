using UnityEngine;

/// <summary>
/// 阵营标记组件（运行时）。
/// - 默认 Enemy：用于兼容历史（现有 NPC 预制体通常在 Enemy Layer 上）。
/// - Summon 召唤时可对该值进行覆写。
/// </summary>
public class FactionMember : MonoBehaviour
{
    [SerializeField]
    [Tooltip("该单位的阵营（Enemy/Friend/Neutral）。")]
    private FactionId faction = FactionId.Enemy;

    public FactionId Faction
    {
        get => faction;
        set => faction = value;
    }
}
