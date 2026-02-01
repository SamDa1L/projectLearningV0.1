using System;
using System.Collections.Generic;

namespace CastleDB.Runtime
{
    /// <summary>
    /// CastleDB 数据源接口
    /// 负责从文件或其他来源读取 CastleDB JSON 数据
    /// </summary>
    public interface ICastleDbSource
    {
        /// <summary>
        /// 读取 CastleDB JSON 数据
        /// </summary>
        /// <returns>CastleDbRoot 对象，若读取失败返回 null</returns>
        CastleDbRoot ReadCastleDbJson();

        /// <summary>
        /// 获取数据源的描述（用于日志）
        /// </summary>
        string GetSourceDescription();
    }

    /// <summary>
    /// CastleDB 仓储接口
    /// 负责缓存和版本管理
    /// </summary>
    public interface ICastleDbRepository
    {
        /// <summary>
        /// 加载数据并缓存
        /// </summary>
        void Load(ICastleDbSource source);

        /// <summary>
        /// 获取所有 NPC 数据
        /// </summary>
        List<NpcEntry> GetAllNpcs();

        /// <summary>
        /// 按 ID 获取 NPC
        /// </summary>
        NpcEntry GetNpcById(string id);

        /// <summary>
        /// 获取所有技能数据
        /// </summary>
        List<AbilityEntry> GetAllAbilities();

        /// <summary>
        /// 按 ID 获取技能
        /// </summary>
        AbilityEntry GetAbilityById(string id);

        /// <summary>
        /// 获取所有玩家数据
        /// </summary>
        List<PlayerEntry> GetAllPlayers();

        /// <summary>
        /// 获取所有玩家攻击覆盖数据（阶段 3A）
        /// </summary>
        List<PlayerAttackOverrideEntry> GetAllPlayerAttackOverrides();

        /// <summary>
        /// 获取所有检测区数据
        /// </summary>
        List<DetectionZoneEntry> GetAllDetectionZones();

        /// <summary>
        /// 按 NPC ID 获取该 NPC 的所有检测区
        /// </summary>
        List<DetectionZoneEntry> GetDetectionZonesByNpcId(string npcId);

        /// <summary>
        /// 获取版本信息
        /// </summary>
        CastleDbVersionInfo GetVersionInfo();

        /// <summary>
        /// 检查版本是否匹配
        /// </summary>
        bool IsVersionMatched(string expectedVersion);
    }

    /// <summary>
    /// CastleDB 服务接口
    /// 对外提供统一的 API
    /// </summary>
    public interface ICastleDbService
    {
        /// <summary>
        /// 初始化服务
        /// </summary>
        void Initialize(ICastleDbSource source);

        /// <summary>
        /// 刷新数据
        /// </summary>
        void Refresh();

        /// <summary>
        /// 获取所有 NPC
        /// </summary>
        List<NpcEntry> GetAllNpcs();

        /// <summary>
        /// 按 ID 获取 NPC
        /// </summary>
        NpcEntry GetNpcById(string id);

        /// <summary>
        /// 获取所有技能
        /// </summary>
        List<AbilityEntry> GetAllAbilities();

        /// <summary>
        /// 按 ID 获取技能
        /// </summary>
        AbilityEntry GetAbilityById(string id);

        /// <summary>
        /// 获取所有玩家
        /// </summary>
        List<PlayerEntry> GetAllPlayers();

        /// <summary>
        /// 获取所有玩家攻击覆盖（阶段 3A）
        /// </summary>
        List<PlayerAttackOverrideEntry> GetAllPlayerAttackOverrides();

        /// <summary>
        /// 获取所有检测区
        /// </summary>
        List<DetectionZoneEntry> GetAllDetectionZones();

        /// <summary>
        /// 按 NPC ID 获取检测区
        /// </summary>
        List<DetectionZoneEntry> GetDetectionZonesByNpcId(string npcId);

        /// <summary>
        /// 尝试获取 Item 定义（0.4 版本）
        /// 规范（[C-Runtime-1]）：
        /// - 桥接到 ItemCatalog.TryGetItem()
        /// - 不存在返回 false，不写日志
        /// </summary>
        bool TryGetItem(string itemId, out ItemDefinition def);

        /// <summary>
        /// 获取所有 Item 定义（0.4 版本）
        /// 规范（[C-Runtime-1]）：
        /// - 桥接到 ItemCatalog.GetAllItems()
        /// - 返回只读列表，零分配
        /// </summary>
        System.Collections.Generic.IReadOnlyList<ItemDefinition> GetAllItems();

        /// <summary>
        /// 资源是否有效（0.4 版本）
        /// 规范（[C-Runtime-1]）：
        /// - 检查 ItemCatalog 是否加载成功且有效
        /// - 供 Bootstrap 检测资源损坏情况
        /// </summary>
        bool IsValid { get; }

        /// <summary>
        /// 获取版本信息
        /// </summary>
        CastleDbVersionInfo GetVersionInfo();

        /// <summary>
        /// 数据变更事件
        /// </summary>
        event Action OnDataChanged;

        /// <summary>
        /// 版本不匹配事件
        /// </summary>
        event Action<string> OnVersionMismatch;
    }
}
