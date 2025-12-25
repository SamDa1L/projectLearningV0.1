using UnityEngine;

/// <summary>
/// 全局游戏配置资源
/// 集中管理游戏全局参数，支持版本控制和快照追踪
///
/// 设计思路：
/// - 将游戏通用参数集中在一个ScriptableObject中
/// - 通过Load()显式加载，避免Singleton的null异常
/// - OnValidate()自动验证参数范围和合理性
/// - DumpConfigSnapshot()生成时间戳JSON快照，便于版本回溯
/// - 所有参数都有Range/Min/Tooltip说明，减少调参事故
///
/// 使用步骤：
/// 1. 在Assets/Resources/Config目录下创建此资源
/// 2. 在游戏启动时调用GameplayConfig.Load()获取实例
/// 3. 其他系统通过GameplayConfig.instance访问参数
/// 4. 修改参数后自动验证，Version会自动更新
/// 5. 需要回溯时使用DumpConfigSnapshot()
/// </summary>
[CreateAssetMenu(menuName = "Game/Gameplay Config")]
public class GameplayConfig : ScriptableObject
{
    // ===== Singleton实例 =====
    private static GameplayConfig _instance;
    public static GameplayConfig instance => _instance;

    // ===== 版本管理 =====
    [SerializeField] public string version = "0.2.0";
    [TextArea(2, 4)]
    [SerializeField]
    public string changelog = "Stage1: 敌人架构优化与参数集中化";

    // ===== 敌人全局参数 =====
    [Header("敌人全局参数")]
    [Range(1, 100)]
    [SerializeField]
    [Tooltip("敌人基础生命值（最小1，最大100）")]
    public int enemyMaxHealth = 30;

    [Min(0.1f)]
    [SerializeField]
    [Tooltip("敌人基础移动速度（最小0.1）")]
    public float enemyBaseSpeed = 3f;

    [Min(0.5f)]
    [SerializeField]
    [Tooltip("敌人基础感知范围（最小0.5）")]
    public float enemyBasePerceptionRadius = 5f;

    [Range(1, 50)]
    [SerializeField]
    [Tooltip("敌人基础攻击伤害（1-50）")]
    public int enemyBaseDamage = 10;

    // ===== 玩家全局参数 =====
    [Header("玩家参数")]
    [Range(1, 200)]
    [SerializeField]
    [Tooltip("玩家最大生命值（1-200）")]
    public int playerMaxHealth = 100;

    [Min(0.1f)]
    [SerializeField]
    [Tooltip("玩家移动速度（最小0.1）")]
    public float playerMoveSpeed = 5f;

    [Range(1, 50)]
    [SerializeField]
    [Tooltip("玩家攻击伤害（1-50）")]
    public int playerAttackDamage = 15;

    [Range(1, 999)]
    [SerializeField]
    [Tooltip("血瓶最大携带数量（1-999，0.4 版本 Consumable 上限唯一真相源）")]
    public int potionMaxCount = 99;

    // ===== 物理全局参数 =====
    [Header("物理参数")]
    [Min(0f)]
    [SerializeField]
    [Tooltip("重力加速度（最小0）")]
    public float gravityScale = 9.8f;

    [Min(0f)]
    [SerializeField]
    [Tooltip("击退力度乘数（最小0）")]
    public float knockbackMultiplier = 1f;

    // ===== 游戏内时间 =====
    [Header("时间参数")]
    [Min(0.01f)]
    [SerializeField]
    [Tooltip("固定时间步长（最小0.01，通常0.016~0.02）")]
    public float fixedDeltaTime = 0.016f;

    [Min(0f)]
    [SerializeField]
    [Tooltip("游戏时间缩放（0.5=慢半速，1.0=正常，2.0=快二倍）")]
    public float timeScale = 1f;

    // ===== 难度与平衡 =====
    [Header("难度与平衡")]
    [Range(0.5f, 3f)]
    [SerializeField]
    [Tooltip("敌人生命值倍数（0.5=简单，1.0=正常，3.0=困难）")]
    public float enemyHealthMultiplier = 1f;

    [Range(0.5f, 3f)]
    [SerializeField]
    [Tooltip("敌人伤害倍数（0.5=简单，1.0=正常，3.0=困难）")]
    public float enemyDamageMultiplier = 1f;

    [Range(0.5f, 3f)]
    [SerializeField]
    [Tooltip("敌人移动速度倍数（0.5=简单，1.0=正常，3.0=困难）")]
    public float enemySpeedMultiplier = 1f;

    // ===== 调试参数 =====
    [Header("调试")]
    [SerializeField]
    [Tooltip("是否启用调试模式（显示Gizmos等）")]
    public bool debugMode = true;

    [SerializeField]
    [Tooltip("是否启用敌人状态调试面板")]
    public bool debugEnemyStateOverlay = true;

    [SerializeField]
    [Tooltip("是否启用性能监视（FPS等）")]
    public bool debugPerformanceMonitor = false;

    // ===== 生命周期 =====

    /// <summary>
    /// 显式加载GameplayConfig
    /// 在游戏启动时调用，避免Singleton的null异常
    /// </summary>
    public static GameplayConfig Load()
    {
        if (_instance != null)
            return _instance;

        _instance = Resources.Load<GameplayConfig>("Config/GameplayConfig");
        if (_instance == null)
        {
            Debug.LogError("[GameplayConfig] 配置资源未找到。请在 Assets/Resources/Config/GameplayConfig.asset 创建资源");
            return null;
        }

        Debug.Log($"[GameplayConfig] 已加载版本 v{_instance.version}");
        return _instance;
    }

    /// <summary>
    /// 编辑器编辑时自动验证参数范围
    /// </summary>
    private void OnValidate()
    {
        // 敌人参数
        enemyMaxHealth = Mathf.Max(1, enemyMaxHealth);
        enemyBaseSpeed = Mathf.Max(0.1f, enemyBaseSpeed);
        enemyBasePerceptionRadius = Mathf.Max(0.5f, enemyBasePerceptionRadius);
        enemyBaseDamage = Mathf.Max(1, enemyBaseDamage);

        // 玩家参数
        playerMaxHealth = Mathf.Max(1, playerMaxHealth);
        playerMoveSpeed = Mathf.Max(0.1f, playerMoveSpeed);
        playerAttackDamage = Mathf.Max(1, playerAttackDamage);
        potionMaxCount = Mathf.Max(1, potionMaxCount);

        // 物理参数
        gravityScale = Mathf.Max(0f, gravityScale);
        knockbackMultiplier = Mathf.Max(0f, knockbackMultiplier);

        // 时间参数
        fixedDeltaTime = Mathf.Max(0.01f, fixedDeltaTime);
        timeScale = Mathf.Max(0f, timeScale);

        #if UNITY_EDITOR
        Debug.Log($"[GameplayConfig] v{version} 验证完成 @ {System.DateTime.Now:HH:mm:ss}");
        #endif
    }

    // ===== 快照与版本管理 =====

    /// <summary>
    /// 生成配置快照，用于版本追踪
    /// 右键Asset → Dump Config Snapshot生成带时间戳的JSON文件
    /// </summary>
    [ContextMenu("Dump Config Snapshot")]
    public void DumpConfigSnapshot()
    {
        string snapshotDir = "Logs/NotesLog/ConfigSnapshots";
        System.IO.Directory.CreateDirectory(snapshotDir);

        string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string snapshotPath = $"{snapshotDir}/GameplayConfig_v{version}_{timestamp}.json";

        string json = JsonUtility.ToJson(this, true);
        System.IO.File.WriteAllText(snapshotPath, json);

        #if UNITY_EDITOR
        Debug.Log($"[GameplayConfig] 快照已保存: {snapshotPath}");
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
        Debug.Log($"[GameplayConfig v{version}]\n" +
                  $"  Enemy Max Health: {enemyMaxHealth}\n" +
                  $"  Enemy Base Speed: {enemyBaseSpeed}\n" +
                  $"  Enemy Base Perception: {enemyBasePerceptionRadius}\n" +
                  $"  Enemy Base Damage: {enemyBaseDamage}\n" +
                  $"  Player Max Health: {playerMaxHealth}\n" +
                  $"  Player Move Speed: {playerMoveSpeed}\n" +
                  $"  Player Attack Damage: {playerAttackDamage}\n" +
                  $"  Gravity Scale: {gravityScale}\n" +
                  $"  Knockback Multiplier: {knockbackMultiplier}\n" +
                  $"  Fixed Delta Time: {fixedDeltaTime}\n" +
                  $"  Time Scale: {timeScale}\n" +
                  $"  Enemy Health Multiplier: {enemyHealthMultiplier}\n" +
                  $"  Enemy Damage Multiplier: {enemyDamageMultiplier}\n" +
                  $"  Enemy Speed Multiplier: {enemySpeedMultiplier}\n" +
                  $"  Debug Mode: {debugMode}\n" +
                  $"  Debug Enemy State Overlay: {debugEnemyStateOverlay}\n" +
                  $"  Timestamp: {System.DateTime.Now:HH:mm:ss}");
    }

    /// <summary>
    /// 编辑器菜单：验证配置是否正确加载
    /// </summary>
#if UNITY_EDITOR
    [UnityEditor.MenuItem("Tools/Gameplay/Verify GameplayConfig")]
    public static void VerifyConfig()
    {
        var config = Resources.Load<GameplayConfig>("Config/GameplayConfig");
        if (config == null)
        {
            Debug.LogError("[GameplayConfig] 配置未找到。请在 Assets/Resources/Config/GameplayConfig.asset 创建");
        }
        else
        {
            Debug.Log($"[GameplayConfig] 验证成功 - 版本 v{config.version}");
            config.PrintAllValues();
        }
    }
#endif
}
