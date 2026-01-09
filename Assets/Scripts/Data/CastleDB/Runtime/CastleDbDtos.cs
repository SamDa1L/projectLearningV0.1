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
        public List<PlayerAttackOverrideEntry> playerAttackOverrideLines = new List<PlayerAttackOverrideEntry>();

        [System.NonSerialized]
        public List<DetectionZoneEntry> detectionZoneLines = new List<DetectionZoneEntry>();

        [System.NonSerialized]
        public List<MetaEntry> metaLines = new List<MetaEntry>();

        [System.NonSerialized]
        public List<ItemEntry> itemLines = new List<ItemEntry>();
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

        [SerializeField]
        public float perceptionRadius;

        /// <summary>
        /// 怪物命中玩家时的击退缩放系数（Monster → Player）
        /// 与 knockbackMultiplier（Player → Monster：怪物受击倍率）是独立的两条链路
        /// 默认值 1 表示保持敌人攻击 Prefab 上的基础击退不变
        /// </summary>
        [SerializeField]
        public float knockbackToPlayer;

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
    /// <summary>
    /// 能力表条目（阶段 3B）
    /// 对应 CastleDB 中 Ability Sheet 的一行数据
    /// </summary>
    public class AbilityEntry
    {
        [SerializeField]
        public string id;

        [SerializeField]
        public int hookType; // 0=Move, 1=Run, 2=Jump, 3=Attack, 4=RangedAttack

        [SerializeField]
        public int priority;

        [SerializeField]
        public bool enabled;

        [SerializeField]
        public string paramsJson; // Optional JSON parameters (0.2 不消费，但必须是合法 JSON)

        // ===== 0.5 扩展字段（与 PlayerAbility.cdb 对齐）=====

        [SerializeField]
        public int kind; // 0=BuiltinDefault, 1=Projectile, 2=StatModifier, 3=Buff

        [SerializeField]
        public string projectileId;

        [SerializeField]
        public string buffId;

        [SerializeField]
        public float cooldown;

        [SerializeField]
        public string onHitSequenceId;

        public override string ToString()
        {
            return $"Ability[id={id}, hookType={hookType}, priority={priority}, enabled={enabled}]";
        }
    }

    /// <summary>
    /// 玩家表条目
    /// 对应 CastleDB 中 Player Sheet 的一行数据
    /// 阶段 3A: 玩家基础属性
    /// </summary>
    [System.Serializable]
    public class PlayerEntry
    {
        [SerializeField]
        public string id;

        [SerializeField]
        public float maxHealth;

        [SerializeField]
        public float invincibilityTime;

        [SerializeField]
        public float walkSpeed;

        [SerializeField]
        public float runSpeed;

        [SerializeField]
        public float airWalkSpeed;

        [SerializeField]
        public float jumpImpulse;  // 注意：CastleDB中字段名是jumpImpulse，代码中是jumpImpules（拼写错误）

        [SerializeField]
        public float climbSpeed;

        [SerializeField]
        public float baseAttackDamage;

        public override string ToString()
        {
            return $"Player[id={id}, maxHealth={maxHealth}, walkSpeed={walkSpeed}, runSpeed={runSpeed}, baseAttackDamage={baseAttackDamage}]";
        }
    }

    /// <summary>
    /// 玩家攻击覆盖表条目
    /// 对应 CastleDB 中 PlayerAttackOverride Sheet 的一行数据
    /// 阶段 3A: 攻击伤害链路
    ///
    /// 设计说明：
    /// - targetType: 0=Hitbox, 1=Projectile
    /// - targetId:
    ///   - Hitbox: Attack.attackId（稳定ID）
    ///   - Projectile: Resources.Load路径（例如 Prefabs/Projectiles/Player/Arrow）
    /// - damageMultiplier: 伤害倍率（必须 > 0）
    /// - damageOverride: 直接覆盖伤害值（若填写必须 > 0，优先级高于multiplier）
    /// </summary>
    [System.Serializable]
    public class PlayerAttackOverrideEntry
    {
        [SerializeField]
        public string id;

        [SerializeField]
        public string playerId;

        [SerializeField]
        public int targetType;  // 0=Hitbox, 1=Projectile

        [SerializeField]
        public string targetId;

        [SerializeField]
        public float damageMultiplier;

        [SerializeField]
        public int damageOverride;  // 0表示未设置

        public override string ToString()
        {
            string overrideStr = damageOverride > 0 ? $", override={damageOverride}" : "";
            return $"PlayerAttackOverride[id={id}, playerId={playerId}, targetType={targetType}, targetId={targetId}, multiplier={damageMultiplier}{overrideStr}]";
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

        /// <summary>
        /// 获取 role 的字符串表示（用于日志输出）
        /// 注意：CastleDB.Runtime 不应依赖 Game.Runtime 的 DetectionZoneBinding（避免 asmdef 循环依赖）。
        /// </summary>
        public string GetRoleName()
        {
            return role switch
            {
                0 => "PrimaryAttack",
                1 => "SecondaryAttack",
                2 => "Cliff",
                3 => "Alert",
                4 => "Lookout",
                5 => "Custom",
                _ => $"Unknown({role})"
            };
        }

        public override string ToString()
        {
            return $"DetectionZone[id={id}, npcId={npcId}, role={GetRoleName()}, childId={childId}]";
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
