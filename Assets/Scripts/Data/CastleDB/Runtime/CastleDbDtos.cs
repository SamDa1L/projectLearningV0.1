using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// CastleDB 数据传输对象（DTO）
/// 用于序列化/反序列化 CastleDB JSON 数据
///
/// 设计说明：
/// - 所有 DTO 都是可序列化的结构体或类
/// - 字段名必须与 CastleDB JSON 中的字段名完全匹配（区分大小写）
/// - 使用 [SerializeField] 确保 Unity 能正确序列化
/// - 枚举值在 JSON 中以整数形式存储
///
/// 阶段1修复：已完成 JSON 解析、测试框架引用、Mock 数据同步
/// </summary>

namespace CastleDB.Runtime
{
    /// <summary>
    /// CastleDB 根对象
    /// 对应整个 .cdb 文件的 JSON 结构
    /// </summary>
    [System.Serializable]
    public class CastleDbRoot
    {
        [SerializeField]
        public List<SheetData> sheets = new List<SheetData>();

        [SerializeField]
        public List<object> customTypes = new List<object>();

        [SerializeField]
        public bool compress = false;
    }

    /// <summary>
    /// Sheet 数据容器
    /// 包含表名和该表的所有行数据
    /// 注意：CastleDB JSON 中使用通用的 "lines" 字段，需要根据 sheet name 进行类型转换
    /// </summary>
    [System.Serializable]
    public class SheetData
    {
        [SerializeField]
        public string name;

        [SerializeField]
        public List<object> lines = new List<object>();

        // 以下字段用于类型特定的访问，在反序列化后由 CastleDbRepository 填充
        [System.NonSerialized]
        public List<NpcEntry> npcLines = new List<NpcEntry>();

        [System.NonSerialized]
        public List<AbilityEntry> abilityLines = new List<AbilityEntry>();

        [System.NonSerialized]
        public List<PlayerEntry> playerLines = new List<PlayerEntry>();

        [System.NonSerialized]
        public List<DetectionZoneEntry> detectionZoneLines = new List<DetectionZoneEntry>();

        [System.NonSerialized]
        public List<MetaEntry> metaLines = new List<MetaEntry>();
    }

    /// <summary>
    /// NPC 表条目
    /// 对应 CastleDB 中 NPC Sheet 的一行数据
    /// </summary>
    [System.Serializable]
    public class NpcEntry
    {
        [SerializeField]
        public string id;

        [SerializeField]
        public string displayName;

        [SerializeField]
        public string prefabName;

        [SerializeField]
        public string animationTrigger;

        [SerializeField]
        public float maxHealth;

        [SerializeField]
        public float attackDamage;

        [SerializeField]
        public float moveSpeed;

        [SerializeField]
        public float attackRange;

        [SerializeField]
        public float attackCooldown;

        [SerializeField]
        public float invincibleDuration;

        [SerializeField]
        public float knockbackMultiplier;

        [SerializeField]
        public bool enableDeathAnimation;

        [SerializeField]
        public bool useLegacyLogicFallback;

        public override string ToString()
        {
            return $"NPC[id={id}, displayName={displayName}, maxHealth={maxHealth}, moveSpeed={moveSpeed}]";
        }
    }

    /// <summary>
    /// 技能表条目
    /// 对应 CastleDB 中 Ability Sheet 的一行数据
    /// </summary>
    [System.Serializable]
    public class AbilityEntry
    {
        [SerializeField]
        public string id;

        [SerializeField]
        public int attackType; // 0=Meele, 1=Projectile

        public override string ToString()
        {
            return $"Ability[id={id}, attackType={attackType}]";
        }
    }

    /// <summary>
    /// 玩家表条目
    /// 对应 CastleDB 中 Player Sheet 的一行数据
    /// 阶段 3 使用
    /// </summary>
    [System.Serializable]
    public class PlayerEntry
    {
        [SerializeField]
        public string id;

        public override string ToString()
        {
            return $"Player[id={id}]";
        }
    }

    /// <summary>
    /// 检测区表条目
    /// 对应 CastleDB 中 DetectionZone Sheet 的一行数据
    ///
    /// 设计说明：
    /// - npcId：关联的 NPC ID（Reference 类型在 JSON 中存储为字符串）
    /// - role：检测区角色（Enumeration 类型在 JSON 中存储为整数索引）
    ///   0=PrimaryAttack, 1=SecondaryAttack, 2=Cliff, 3=Alert, 4=Lookout, 5=Custom
    /// - childId：Prefab 中的子物体名称
    /// </summary>
    [System.Serializable]
    public class DetectionZoneEntry
    {
        [SerializeField]
        public string id;

        [SerializeField]
        public string npcId;

        [SerializeField]
        public int role; // 枚举值：0=PrimaryAttack, 1=SecondaryAttack, 2=Cliff, 3=Alert, 4=Lookout, 5=Custom

        [SerializeField]
        public string childId;

        public override string ToString()
        {
            return $"DetectionZone[id={id}, npcId={npcId}, role={role}, childId={childId}]";
        }
    }

    /// <summary>
    /// 元信息表条目
    /// 对应 CastleDB 中 Meta Sheet 的一行数据
    ///
    /// 用途：
    /// - 存储全局配置信息（如 schemaVersion）
    /// - 存储功能开关（如 featureFlags）
    /// </summary>
    [System.Serializable]
    public class MetaEntry
    {
        [SerializeField]
        public string key;

        [SerializeField]
        public string value;

        public override string ToString()
        {
            return $"Meta[key={key}, value={value}]";
        }
    }

    /// <summary>
    /// CastleDB 版本信息
    /// 用于版本管理和兼容性检查
    /// </summary>
    [System.Serializable]
    public class CastleDbVersionInfo
    {
        public string schemaVersion;
        public string featureFlags;
        public DateTime loadTime;

        public CastleDbVersionInfo(string schemaVersion, string featureFlags)
        {
            this.schemaVersion = schemaVersion;
            this.featureFlags = featureFlags;
            this.loadTime = DateTime.Now;
        }

        public override string ToString()
        {
            return $"Version[schema={schemaVersion}, flags={featureFlags}, time={loadTime:HH:mm:ss}]";
        }
    }
}
