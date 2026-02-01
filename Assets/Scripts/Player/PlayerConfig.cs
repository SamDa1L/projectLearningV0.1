using UnityEngine;
using System.Collections.Generic;
using CastleDB.Runtime;

/// <summary>
/// 玩家配置资源（0.3 版本）
/// 包含玩家基础属性与攻击覆盖配置
///
/// 设计思路：
/// - 将玩家所有参数集中在 ScriptableObject 中
/// - 从 CastleDB 的 Player.cdb（Player + PlayerAttackOverride 表）导入生成
/// - 提供幂等的应用方法，确保重复调用不会累乘数值
///
/// 使用步骤：
/// 1. 在 CastleDB 中维护 Player.cdb 的 Player 和 PlayerAttackOverride 表
/// 2. 运行 Tools > CastleDB > Import All 生成/更新此资源
/// 3. 运行时由 PlayerController 初始化时自动加载并应用
/// </summary>
[CreateAssetMenu(menuName = "Game/Player/Player Config")]
public class PlayerConfig : ScriptableObject
{
    // ===== 版本管理 =====
    [Header("版本管理")]
    [SerializeField] public string version = "0.3";
    [SerializeField] public string playerId = "player";

    // ===== 基础属性（来自 Player Sheet）=====
    [Header("基础属性")]
    [Min(0.1f)]
    [SerializeField]
    [Tooltip("玩家最大生命值（阶段 3A：支持小数）")]
    public float maxHealth = 100f;

    [Min(0f)]
    [SerializeField]
    [Tooltip("无敌帧时长（秒）")]
    public float invincibilityTime = 0.25f;

    [Min(0.1f)]
    [SerializeField]
    [Tooltip("行走速度（m/s）")]
    public float walkSpeed = 5f;

    [Min(0.1f)]
    [SerializeField]
    [Tooltip("奔跑速度（m/s）")]
    public float runSpeed = 8f;

    [Min(0.1f)]
    [SerializeField]
    [Tooltip("空中移动速度（m/s）")]
    public float airWalkSpeed = 3f;

    [Min(0.1f)]
    [SerializeField]
    [Tooltip("跳跃冲力（注意：代码字段名为 jumpImpules，拼写错误）")]
    public float jumpImpulse = 10f;

    [Min(0.1f)]
    [SerializeField]
    [Tooltip("爬墙速度（m/s）")]
    public float climbSpeed = 3f;

    [Min(0.1f)]
    [SerializeField]
    [Tooltip("基础攻击力（与攻击覆盖的倍率共同决定最终伤害）")]
    public float baseAttackDamage = 10f;

    // ===== 攻击覆盖列表（来自 PlayerAttackOverride Sheet）=====
    [Header("攻击覆盖配置")]
    [SerializeField]
    [Tooltip("攻击伤害覆盖列表（从 CastleDB 导入，运行时只读）")]
    public List<PlayerAttackOverride> attackOverrides = new List<PlayerAttackOverride>();

    /// <summary>
    /// 从 CastleDB PlayerEntry 应用数据到此配置
    /// 仅在导入期调用（Tools/CastleDB/Import All）
    /// </summary>
    public void ApplyFromCastleDb(PlayerEntry player, List<PlayerAttackOverrideEntry> overrides)
    {
        if (player == null)
        {
            Debug.LogError("[PlayerConfig] ApplyFromCastleDb: player 为 null");
            return;
        }

        // 应用基础属性
        playerId = player.id;
        maxHealth = player.maxHealth;
        invincibilityTime = player.invincibilityTime;
        walkSpeed = player.walkSpeed;
        runSpeed = player.runSpeed;
        airWalkSpeed = player.airWalkSpeed;
        jumpImpulse = player.jumpImpulse;  // 注意字段映射
        climbSpeed = player.climbSpeed;
        baseAttackDamage = player.baseAttackDamage;

        // 应用攻击覆盖列表
        attackOverrides.Clear();
        if (overrides != null)
        {
            foreach (var entry in overrides)
            {
                attackOverrides.Add(new PlayerAttackOverride
                {
                    id = entry.id,
                    playerId = entry.playerId,
                    targetType = (PlayerAttackOverride.TargetType)entry.targetType,
                    targetId = entry.targetId,
                    damageMultiplier = entry.damageMultiplier,
                    damageOverride = entry.damageOverride
                });
            }
        }

        Debug.Log($"[PlayerConfig] 已应用 CastleDB 数据: playerId={playerId}, baseAttackDamage={baseAttackDamage}, overrides={attackOverrides.Count}");
    }

    /// <summary>
    /// 查找特定目标的攻击覆盖
    /// </summary>
    public PlayerAttackOverride FindAttackOverride(PlayerAttackOverride.TargetType targetType, string targetId)
    {
        return attackOverrides.Find(o => o.targetType == targetType && o.targetId == targetId);
    }

    /// <summary>
    /// 计算最终伤害值（幂等计算）
    /// 返回值为绝对伤害值（不依赖 Prefab 初始值）
    /// </summary>
    public int CalculateFinalDamage(PlayerAttackOverride.TargetType targetType, string targetId)
    {
        var attackOverride = FindAttackOverride(targetType, targetId);

        if (attackOverride != null)
        {
            // 如果有 override，优先使用 override 值
            if (attackOverride.damageOverride > 0)
            {
                return attackOverride.damageOverride;
            }

            // 否则使用 baseAttackDamage * multiplier
            float rawDamage = baseAttackDamage * attackOverride.damageMultiplier;
            int finalDamage = Mathf.Max(1, Mathf.RoundToInt(rawDamage));

            // 如果发生了 Clamp，输出日志
            if (rawDamage < 1f && finalDamage == 1)
            {
                Debug.LogWarning($"[PlayerConfig] 伤害计算被 Clamp 到最小值 1: " +
                    $"targetType={targetType}, targetId={targetId}, " +
                    $"baseAttackDamage={baseAttackDamage}, multiplier={attackOverride.damageMultiplier}, " +
                    $"raw={rawDamage}, rounded={Mathf.RoundToInt(rawDamage)}");
            }

            return finalDamage;
        }

        // 没有找到 override，使用默认倍率 1.0
        return Mathf.Max(1, Mathf.RoundToInt(baseAttackDamage * 1.0f));
    }

    /// <summary>
    /// OnValidate：防止手动修改 ScriptableObject
    /// 所有数值必须来自 CastleDB，手动修改会被提示
    /// </summary>
    private void OnValidate()
    {
        // 基础数值校验
        if (maxHealth <= 0) maxHealth = 0.1f;
        if (walkSpeed <= 0) walkSpeed = 0.1f;
        if (runSpeed <= 0) runSpeed = 0.1f;
        if (airWalkSpeed <= 0) airWalkSpeed = 0.1f;
        if (jumpImpulse <= 0) jumpImpulse = 0.1f;
        if (climbSpeed <= 0) climbSpeed = 0.1f;
        if (baseAttackDamage <= 0) baseAttackDamage = 0.1f;
        if (invincibilityTime < 0) invincibilityTime = 0f;
    }
}

/// <summary>
/// 玩家攻击覆盖配置项
/// 用于在 PlayerConfig 中存储攻击倍率/覆盖信息
/// </summary>
[System.Serializable]
public class PlayerAttackOverride
{
    public enum TargetType
    {
        Hitbox = 0,      // Attack 组件（通过 attackId 匹配）
        Projectile = 1   // 投射物 Prefab（通过 Resources 路径匹配）
    }

    [SerializeField]
    public string id;

    [SerializeField]
    public string playerId;

    [SerializeField]
    public TargetType targetType;

    [SerializeField]
    public string targetId;

    [Min(0.01f)]
    [SerializeField]
    [Tooltip("伤害倍率（必须 > 0）")]
    public float damageMultiplier = 1.0f;

    [SerializeField]
    [Tooltip("直接覆盖伤害值（0=不使用，优先级高于 multiplier）")]
    public int damageOverride = 0;

    public override string ToString()
    {
        string overrideStr = damageOverride > 0 ? $", override={damageOverride}" : "";
        return $"PlayerAttackOverride[id={id}, targetType={targetType}, targetId={targetId}, multiplier={damageMultiplier}{overrideStr}]";
    }
}
