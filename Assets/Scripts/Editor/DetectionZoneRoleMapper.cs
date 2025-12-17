using UnityEngine;

/// <summary>
/// 检测区 Role 映射工具类（P1-3.4）
///
/// 提供 CastleDB DetectionZone.role (int) 与 Prefab DetectionZoneBinding.Role (enum) 之间的统一映射。
///
/// 设计说明：
/// - 此类放在 Editor 层，避免 CastleDB.Runtime 依赖 Game.Runtime（asmdef 循环依赖）
/// - 所有需要 role 映射的地方（校验工具、同步工具等）应使用此类，避免多处 switch 漂移
/// - CastleDB schema 增加 role 时只需更新此处
///
/// CastleDB role 枚举定义（0.2 版本）：
/// - 0: PrimaryAttack（主攻击检测，GetDetectedTargets 默认来源）
/// - 1: SecondaryAttack（副攻击检测）
/// - 2: Cliff（崖边检测）
/// - 3: Alert（警戒范围）
/// - 4: Lookout（视野范围）
/// - 5: Custom（自定义用途）
/// </summary>
public static class DetectionZoneRoleMapper
{
    /// <summary>
    /// 将 CastleDB DetectionZone.role (int) 转换为 Prefab DetectionZoneBinding.Role (enum)
    /// </summary>
    /// <param name="roleIndex">CastleDB 中的 role 整数值</param>
    /// <returns>对应的 DetectionZoneBinding.Role 枚举值</returns>
    public static DetectionZoneBinding.Role ToBindingRole(int roleIndex)
    {
        return roleIndex switch
        {
            0 => DetectionZoneBinding.Role.PrimaryAttack,
            1 => DetectionZoneBinding.Role.SecondaryAttack,
            2 => DetectionZoneBinding.Role.Cliff,
            3 => DetectionZoneBinding.Role.Alert,
            4 => DetectionZoneBinding.Role.Lookout,
            5 => DetectionZoneBinding.Role.Custom,
            _ => DetectionZoneBinding.Role.Custom
        };
    }

    /// <summary>
    /// 将 Prefab DetectionZoneBinding.Role (enum) 转换为 CastleDB DetectionZone.role (int)
    /// </summary>
    /// <param name="role">Prefab 中的 DetectionZoneBinding.Role 枚举值</param>
    /// <returns>对应的 CastleDB role 整数值</returns>
    public static int ToRoleIndex(DetectionZoneBinding.Role role)
    {
        return role switch
        {
            DetectionZoneBinding.Role.PrimaryAttack => 0,
            DetectionZoneBinding.Role.SecondaryAttack => 1,
            DetectionZoneBinding.Role.Cliff => 2,
            DetectionZoneBinding.Role.Alert => 3,
            DetectionZoneBinding.Role.Lookout => 4,
            DetectionZoneBinding.Role.Custom => 5,
            _ => 5
        };
    }

    /// <summary>
    /// 获取 role 的显示名称（用于日志输出）
    /// </summary>
    /// <param name="roleIndex">CastleDB 中的 role 整数值</param>
    /// <returns>role 的字符串名称</returns>
    public static string GetRoleName(int roleIndex)
    {
        return roleIndex switch
        {
            0 => "PrimaryAttack",
            1 => "SecondaryAttack",
            2 => "Cliff",
            3 => "Alert",
            4 => "Lookout",
            5 => "Custom",
            _ => $"Unknown({roleIndex})"
        };
    }

    /// <summary>
    /// 获取 role 的详细描述（用于文档/报告）
    /// </summary>
    /// <param name="roleIndex">CastleDB 中的 role 整数值</param>
    /// <returns>role 的详细描述</returns>
    public static string GetRoleDescription(int roleIndex)
    {
        return roleIndex switch
        {
            0 => "主攻击检测（GetDetectedTargets 默认来源）",
            1 => "副攻击检测",
            2 => "崖边检测",
            3 => "警戒范围",
            4 => "视野范围",
            5 => "自定义用途",
            _ => "未知角色"
        };
    }

    /// <summary>
    /// 检查 roleIndex 是否在有效范围内
    /// </summary>
    /// <param name="roleIndex">CastleDB 中的 role 整数值</param>
    /// <returns>是否为有效的 role 值</returns>
    public static bool IsValidRoleIndex(int roleIndex)
    {
        return roleIndex >= 0 && roleIndex <= 5;
    }

    /// <summary>
    /// 获取所有有效的 role 索引
    /// </summary>
    public static int[] AllRoleIndices => new[] { 0, 1, 2, 3, 4, 5 };

    /// <summary>
    /// 获取所有有效的 DetectionZoneBinding.Role 枚举值
    /// </summary>
    public static DetectionZoneBinding.Role[] AllBindingRoles => new[]
    {
        DetectionZoneBinding.Role.PrimaryAttack,
        DetectionZoneBinding.Role.SecondaryAttack,
        DetectionZoneBinding.Role.Cliff,
        DetectionZoneBinding.Role.Alert,
        DetectionZoneBinding.Role.Lookout,
        DetectionZoneBinding.Role.Custom
    };
}
