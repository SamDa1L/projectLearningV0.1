using UnityEngine;
using CastleDB.Runtime;

/// <summary>
/// 敌人调参配置资源
/// 为敌人提供统一的参数管理，支持版本控制和验证
///
/// 设计思路：
/// - 将所有敌人参数集中在ScriptableObject中
/// - 通过Range/Min/Tooltip进行参数验证和说明
/// - OnValidate()自动验证数值合理性
/// - DumpProfileSnapshot()生成时间戳JSON快照，便于版本追踪
///
/// 使用步骤：
/// 1. 在Project窗口右键 → Create → Game/Enemy/Tuning Profile
/// 2. 在敌人Prefab中的EnemyAgentBase脚本引用此资源
/// 3. 通过Inspector调整参数
/// 4. 修改后自动记录版本和时间戳
/// 5. 需要回溯参数时，用DumpProfileSnapshot()生成快照
/// </summary>
[CreateAssetMenu(menuName = "Game/Enemy/Tuning Profile")]
public class EnemyTuningProfile : ScriptableObject
{
    // ===== 版本管理 =====
    [SerializeField] public string version = "1.0.0";
    [SerializeField] public string profileName = "Default Enemy";

    // ===== 生命值与移动 =====
    [Header("基础属性")]
    [Min(1f)]
    [SerializeField]
    [Tooltip("敌人最大生命值（最小1）")]
    public float maxHealth = 30f;

    [Min(0.1f)]
    [SerializeField]
    [Tooltip("敌人移动速度（最小0.1）")]
    public float moveSpeed = 3f;

    // ===== 感知与战斗 =====
    [Header("感知与战斗")]
    [Min(0.5f)]
    [SerializeField]
    [Tooltip("感知玩家的范围半径（最小0.5）")]
    public float perceptionRadius = 5f;

    [Min(0.1f)]
    [SerializeField]
    [Tooltip("攻击范围（最小0.1）")]
    public float attackRange = 1.5f;

    [Range(1, 100)]
    [SerializeField]
    [Tooltip("单次攻击伤害（1-100）")]
    public int attackDamage = 10;

    [Min(0.1f)]
    [SerializeField]
    [Tooltip("攻击冷却时间（秒）（最小0.1）")]
    public float attackCooldown = 1.5f;

    // ===== 物理与行为 =====
    [Header("物理与行为")]
    [SerializeField]
    [Tooltip("击退力度（x=水平，y=竖直）")]
    public Vector2 knockbackForce = new Vector2(5f, 3f);

    [Min(0f)]
    [SerializeField]
    [Tooltip("巡逻范围（最小0）")]
    public float patrolDistance = 4f;

    [Range(0f, 1f)]
    [SerializeField]
    [Tooltip("受伤后返回巡逻的延迟时间（秒）")]
    public float hitRecoveryDelay = 0.5f;

    // ===== 特殊属性 =====
    [Header("特殊属性")]
    [Range(0f, 1f)]
    [SerializeField]
    [Tooltip("无敌帧时长（秒）")]
    public float invulnerableFrameDuration = 0.3f;

    [Min(0f)]
    [SerializeField]
    [Tooltip("击退倍率（影响受击时的击退力度）")]
    public float knockbackMultiplier = 1f;

    [Min(0f)]
    [SerializeField]
    [Tooltip("怪物命中玩家时的击退缩放系数（Monster → Player）\n" +
             "与 knockbackMultiplier（Player → Monster：怪物受击倍率）是独立的两条链路\n" +
             "默认值 1 表示保持敌人攻击 Prefab 上的基础击退不变\n" +
             "例如：0.5 = 减半，2 = 翻倍，0 = 禁用击退")]
    public float knockbackToPlayer = 1f;

    [SerializeField]
    [Tooltip("死亡时是否播放死亡动画")]
    public bool enableDeathAnimation = true;

    [Min(0f)]
    [SerializeField]
    [Tooltip("死亡后消失的延迟时间（秒）")]
    public float deathDelay = 1f;

    // ===== 动画与行为控制 =====
    [Header("动画与行为控制")]
    [SerializeField]
    [Tooltip("主攻击动画触发器名称（需与Animator Controller中的Trigger参数匹配）")]
    public string animationTrigger = "Attack";

    [SerializeField]
    [Tooltip("是否使用旧版逻辑（兼容开关，用于回退到旧行为）")]
    public bool useLegacyLogicFallback = false;

    // ===== 验证 =====

    /// <summary>
    /// 编辑器编辑时自动验证参数范围
    /// 2A 要求：Profile 作为只读缓存，检测手工修改并警告
    /// </summary>
    private void OnValidate()
    {
        // 确保所有数值在合理范围内
        maxHealth = Mathf.Max(1f, maxHealth);
        moveSpeed = Mathf.Max(0.1f, moveSpeed);
        perceptionRadius = Mathf.Max(0.5f, perceptionRadius);
        attackRange = Mathf.Max(0.1f, attackRange);
        attackCooldown = Mathf.Max(0.1f, attackCooldown);
        patrolDistance = Mathf.Max(0f, patrolDistance);
        hitRecoveryDelay = Mathf.Clamp01(hitRecoveryDelay);
        invulnerableFrameDuration = Mathf.Clamp01(invulnerableFrameDuration);
        knockbackMultiplier = Mathf.Max(0f, knockbackMultiplier);
        knockbackToPlayer = Mathf.Max(0f, knockbackToPlayer);
        deathDelay = Mathf.Max(0f, deathDelay);

        // animationTrigger 校验：必须非空且仅含字母/数字/下划线
        if (string.IsNullOrWhiteSpace(animationTrigger))
        {
            #if UNITY_EDITOR
            Debug.LogWarning($"[EnemyTuningProfile] {profileName}: animationTrigger 为空，已回滚到默认值 'Attack'。请到 CastleDB 的 NPC.animationTrigger 修正。", this);
            #endif
            animationTrigger = "Attack";
        }
        else if (!System.Text.RegularExpressions.Regex.IsMatch(animationTrigger, @"^[a-zA-Z0-9_]+$"))
        {
            #if UNITY_EDITOR
            Debug.LogWarning($"[EnemyTuningProfile] {profileName}: animationTrigger '{animationTrigger}' 包含非法字符（仅允许字母/数字/下划线），已回滚到 'Attack'。请到 CastleDB 修正。", this);
            #endif
            animationTrigger = "Attack";
        }

        #if UNITY_EDITOR
        // 轻量版"只读缓存"警告：提示用户不要手工修改 Profile
        // 注：严格版（带 lastImportedHash 自动回滚）可在后续实现
        Debug.Log($"[EnemyTuningProfile] {profileName} v{version} 验证完成 @ {System.DateTime.Now:HH:mm:ss}");
        Debug.Log($"[EnemyTuningProfile] 提醒：此 Profile 应由 CastleDB Import 工具维护，手工修改可能在下次导入时被覆盖。", this);
        #endif
    }

    // ===== 数值接口 =====

    /// <summary>
    /// 获取 Damageable 组件所需的数值包
    /// 用于将配置中的生命值和无敌帧参数传递给 Damageable 组件
    /// </summary>
    /// <returns>包含 maxHealth、invincibilityTime 和 knockbackMultiplier 的结构体</returns>
    public DamageableStats GetDamageableStats()
    {
        return new DamageableStats
        {
            maxHealth = Mathf.RoundToInt(maxHealth),
            invincibilityTime = invulnerableFrameDuration,
            knockbackMultiplier = this.knockbackMultiplier  // 透传实际配置值，不再硬编码
        };
    }

    /// <summary>
    /// 从 CastleDB 的 NpcEntry 应用数值到此 Profile
    /// 用于 CastleDbImporter 导入数据时更新 Profile 字段
    /// 2A 要求：全量覆盖所有字段，不保留旧值
    /// </summary>
    /// <param name="npc">来自 CastleDB 的 NPC 数据条目</param>
    public void ApplyFromCastleDb(NpcEntry npc)
    {
        // ===== 基础信息 =====
        profileName = npc.displayName;

        // ===== 基础属性（生命值与移动）=====
        maxHealth = npc.maxHealth;
        moveSpeed = npc.moveSpeed;

        // ===== 感知与战斗 =====
        perceptionRadius = npc.perceptionRadius;

        attackRange = npc.attackRange;
        attackDamage = Mathf.RoundToInt(npc.attackDamage);
        attackCooldown = npc.attackCooldown;

        // ===== 物理与行为 =====
        // patrolDistance、hitRecoveryDelay：NpcEntry 暂无这些字段
        // knockbackForce 使用 Vector2，需从 knockbackMultiplier 派生或保持默认
        // 这里暂时不覆盖 knockbackForce，保持 Profile 默认值

        // ===== 特殊属性 =====
        invulnerableFrameDuration = npc.invincibleDuration;
        knockbackMultiplier = npc.knockbackMultiplier;
        knockbackToPlayer = npc.knockbackToPlayer;
        enableDeathAnimation = npc.enableDeathAnimation;
        // deathDelay：NpcEntry 暂无此字段，保持 Profile 默认值

        // ===== 动画与行为控制 =====
        animationTrigger = npc.animationTrigger;
        useLegacyLogicFallback = npc.useLegacyLogicFallback;

        #if UNITY_EDITOR
        Debug.Log($"[EnemyTuningProfile] 已从 CastleDB 应用数据: {profileName}\n" +
                  $"  HP={maxHealth}, Speed={moveSpeed}, Dmg={attackDamage}\n" +
                  $"  AtkRange={attackRange}, Cooldown={attackCooldown}\n" +
                  $"  Invincible={invulnerableFrameDuration}s, Knockback={knockbackMultiplier}x, KnockbackToPlayer={knockbackToPlayer}x\n" +
                  $"  AnimTrigger='{animationTrigger}', LegacyFallback={useLegacyLogicFallback}");
        #endif
    }

    // ===== 快照与追踪 =====

    /// <summary>
    /// 生成配置快照，用于版本追踪
    /// 右键Asset → Dump Profile Snapshot生成带时间戳的JSON文件
    /// </summary>
    [ContextMenu("Dump Profile Snapshot")]
    public void DumpProfileSnapshot()
    {
        string snapshotDir = "Logs/NotesLog/ConfigSnapshots";
        System.IO.Directory.CreateDirectory(snapshotDir);

        string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string snapshotPath = $"{snapshotDir}/Profile_{profileName}_v{version}_{timestamp}.json";

        string json = JsonUtility.ToJson(this, true);
        System.IO.File.WriteAllText(snapshotPath, json);

        #if UNITY_EDITOR
        Debug.Log($"[EnemyTuningProfile] 快照已保存: {snapshotPath}");
        UnityEditor.AssetDatabase.Refresh();
        #endif
    }

    /// <summary>
    /// 在控制台打印所有参数值
    /// 便于快速查看当前配置
    /// </summary>
    [ContextMenu("Print All Values")]
    public void PrintAllValues()
    {
        Debug.Log($"[EnemyTuningProfile: {profileName} v{version}]\n" +
                  $"  Max Health: {maxHealth}\n" +
                  $"  Move Speed: {moveSpeed}\n" +
                  $"  Perception Radius: {perceptionRadius}\n" +
                  $"  Attack Range: {attackRange}\n" +
                  $"  Attack Damage: {attackDamage}\n" +
                  $"  Attack Cooldown: {attackCooldown}\n" +
                  $"  Knockback Force: {knockbackForce}\n" +
                  $"  Knockback Multiplier: {knockbackMultiplier}\n" +
                  $"  Knockback To Player: {knockbackToPlayer}\n" +
                  $"  Patrol Distance: {patrolDistance}\n" +
                  $"  Hit Recovery Delay: {hitRecoveryDelay}\n" +
                  $"  Invulnerable Frame Duration: {invulnerableFrameDuration}\n" +
                  $"  Enable Death Animation: {enableDeathAnimation}\n" +
                  $"  Death Delay: {deathDelay}\n" +
                  $"  Animation Trigger: '{animationTrigger}'\n" +
                  $"  Use Legacy Logic Fallback: {useLegacyLogicFallback}\n" +
                  $"  Timestamp: {System.DateTime.Now:HH:mm:ss}");
    }

    /// <summary>
    /// 克隆此Profile为新资源
    /// 便于快速创建相似敌人的配置
    /// </summary>
    public EnemyTuningProfile Clone()
    {
        EnemyTuningProfile cloned = CreateInstance<EnemyTuningProfile>();
        cloned.version = version;
        cloned.profileName = profileName + "_Clone";
        cloned.maxHealth = maxHealth;
        cloned.moveSpeed = moveSpeed;
        cloned.perceptionRadius = perceptionRadius;
        cloned.attackRange = attackRange;
        cloned.attackDamage = attackDamage;
        cloned.attackCooldown = attackCooldown;
        cloned.knockbackForce = knockbackForce;
        cloned.knockbackMultiplier = knockbackMultiplier;
        cloned.knockbackToPlayer = knockbackToPlayer;
        cloned.patrolDistance = patrolDistance;
        cloned.hitRecoveryDelay = hitRecoveryDelay;
        cloned.invulnerableFrameDuration = invulnerableFrameDuration;
        cloned.enableDeathAnimation = enableDeathAnimation;
        cloned.deathDelay = deathDelay;
        cloned.animationTrigger = animationTrigger;
        cloned.useLegacyLogicFallback = useLegacyLogicFallback;

        return cloned;
    }
}
