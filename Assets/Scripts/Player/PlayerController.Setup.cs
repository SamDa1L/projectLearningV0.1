using UnityEngine;
using UnityEngine.InputSystem;

public partial class PlayerController
{
    private void EnsureInputModeSwitcher()
    {
        // 仅当 PlayerInput 挂在同一 GameObject 上时才启用（避免无输入时无意义地挂组件）。
        if (GetComponent<PlayerInput>() == null)
        {
            return;
        }

        if (GetComponent<InputModeSwitcher>() == null)
        {
            gameObject.AddComponent<InputModeSwitcher>();
        }

        if (GetComponent<InputModeHintOverlay>() == null)
        {
            gameObject.AddComponent<InputModeHintOverlay>();
        }
    }

    /// <summary>
    /// Awake生命周期函数
    /// 用于在Game Object激活时初始化组件引用
    /// 阶段 3A: 从 PlayerConfig 加载配置并应用到组件
    /// </summary>
    private void Awake()
    {
        // 获取当前GameObject上的Rigidbody2D组件
        rb = GetComponent<Rigidbody2D>();

        // 获取当前GameObject上的Animator组件
        animator = GetComponent<Animator>();

        // 获取当前GameObject上的TouchingDirections组件(碰撞检测)
        touchingDirections = GetComponent<TouchingDirections>();
        damageable = GetComponent<Damageable>();
        statLayer = GetComponent<StatModifierLayer>();
        if (statLayer == null)
        {
            statLayer = gameObject.AddComponent<StatModifierLayer>();
        }

        // 缓存 PlayerContext（可能在父节点）
        _playerContext = GetComponent<PlayerContext>();
        if (_playerContext == null)
        {
            _playerContext = GetComponentInParent<PlayerContext>();
        }

        if (GetComponent<StatusEffectController>() == null)
        {
            gameObject.AddComponent<StatusEffectController>();
        }

        // 阶段 9：最后输入设备优先的控制方案切换器（不使用 Find/Tag/单例）。
        EnsureInputModeSwitcher();

        // 阶段 3A: 从 PlayerConfig 加载配置
        LoadConfigFromPlayerConfig();

        // 阶段 3B: 构建能力系统（当 usePlayerConfigFromCastleDb = true 时）
        if (usePlayerConfigFromCastleDb)
        {
            BuildAbilitySystem();
        }
    }

    /// <summary>
    /// 从 PlayerConfig 加载配置并应用到组件（阶段 3A）
    /// - 如果 usePlayerConfigFromCastleDb=false，跳过加载（使用硬编码默认值）
    /// - 如果未设置 playerConfig，使用默认的硬编码值
    /// - 应用移动速度参数到 PlayerController
    /// - 应用生命值参数到 Damageable 组件
    /// - 应用攻击伤害覆盖到所有 Attack 子物体
    /// </summary>
    private void LoadConfigFromPlayerConfig()
    {
        // 检查回退开关
        if (!usePlayerConfigFromCastleDb)
        {
            Debug.Log("[PlayerController] usePlayerConfigFromCastleDb=false，使用硬编码默认值");
            return;
        }

        // 如果没有配置 PlayerConfig，使用默认值
        if (playerConfig == null)
        {
            Debug.LogWarning("[PlayerController] playerConfig 未设置，使用默认值");
            return;
        }

        // 应用移动速度参数
        walkSpeed = playerConfig.walkSpeed;
        runSpeed = playerConfig.runSpeed;
        airWalkSpeed = playerConfig.airWalkSpeed;
        jumpImpules = playerConfig.jumpImpulse; // 注意字段名映射
        climbSpeed = playerConfig.climbSpeed;

        // 应用 Damageable 配置
        if (damageable != null)
        {
            DamageableStats stats = new DamageableStats
            {
                maxHealth = playerConfig.maxHealth,
                invincibilityTime = playerConfig.invincibilityTime,
                knockbackMultiplier = 1.0f // 玩家受击击退倍率（可后续从配置读取）
            };
            damageable.Configure(stats);

            Debug.Log(
                $"[PlayerController] 已从 PlayerConfig 应用配置: " +
                $"walkSpeed={walkSpeed}, runSpeed={runSpeed}, maxHealth={playerConfig.maxHealth}, " +
                $"baseAttackDamage={playerConfig.baseAttackDamage}");
        }
        else
        {
            Debug.LogError("[PlayerController] Damageable 组件未找到，无法应用配置");
        }

        // 阶段 3A: 应用攻击伤害覆盖
        ApplyAttackDamageOverrides();
    }

    /// <summary>
    /// 应用攻击伤害覆盖到所有 Attack 子物体（阶段 3A）
    /// 遍历所有子物体，找到 Attack 组件，根据 attackId 从 PlayerConfig 计算最终伤害
    /// </summary>
    private void ApplyAttackDamageOverrides()
    {
        if (playerConfig == null)
        {
            return;
        }

        // 获取所有子物体的 Attack 组件（包括深层嵌套）
        Attack[] attacks = GetComponentsInChildren<Attack>(true);

        if (attacks.Length == 0)
        {
            Debug.LogWarning("[PlayerController] 未找到任何 Attack 组件");
            return;
        }

        int appliedCount = 0;
        foreach (var attack in attacks)
        {
            // 跳过未配置 attackId 的 Attack
            if (string.IsNullOrEmpty(attack.attackId))
            {
                Debug.LogWarning($"[PlayerController] Attack 组件 '{attack.gameObject.name}' 的 attackId 为空，跳过");
                continue;
            }

            // 从 PlayerConfig 计算最终伤害
            int finalDamage = playerConfig.CalculateFinalDamage(
                PlayerAttackOverride.TargetType.Hitbox,
                attack.attackId);

            // 应用伤害值
            attack.attackDamage = finalDamage;
            appliedCount++;

            Debug.Log(
                $"[PlayerController] 已应用攻击伤害覆盖: " +
                $"attackId={attack.attackId}, finalDamage={finalDamage}, " +
                $"GameObject={attack.gameObject.name}");
        }

        Debug.Log($"[PlayerController] 攻击伤害覆盖应用完成，共处理 {appliedCount}/{attacks.Length} 个 Attack 组件");
    }
}

