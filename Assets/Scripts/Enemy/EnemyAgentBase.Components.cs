using UnityEngine;

public abstract partial class EnemyAgentBase
{
    // ===== 组件缓存 =====

    /// <summary>
    /// 在Awake中缓存所有必需的组件
    /// 子类可以覆盖此方法来扩展组件缓存
    /// </summary>
    protected virtual void CacheComponents()
    {
        // 获取本体的组件
        rb2d = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        damageable = GetComponent<Damageable>();
        statLayer = GetComponent<StatModifierLayer>();
        if (statLayer == null)
        {
            statLayer = gameObject.AddComponent<StatModifierLayer>();
        }

        if (GetComponent<StatusEffectController>() == null)
        {
            gameObject.AddComponent<StatusEffectController>();
        }

        _npcAbilityController = GetComponent<NpcAbilityController>();
        if (_npcAbilityController == null)
        {
            _npcAbilityController = gameObject.AddComponent<NpcAbilityController>();
        }

        detectionZone = GetComponent<DetectionZone>();
        cacheTransform = transform;

        // 验证关键组件
        if (rb2d == null)
            Debug.LogError($"[{gameObject.name}] 缺少Rigidbody2D组件", gameObject);

        if (animator == null)
            Debug.LogError($"[{gameObject.name}] 缺少Animator组件", gameObject);

        if (damageable == null)
            Debug.LogError($"[{gameObject.name}] 缺少Damageable组件", gameObject);

        // 注意：DetectionZone的验证在ResolveDetectionZone()中执行
    }

    /// <summary>
    /// 解决检测区依赖（v0.2重构版 - Plan A）
    /// 在Awake中CacheComponents()之后调用
    ///
    /// 设计说明（方案A：基于zoneBindings）：
    /// - 放弃复杂的自动推断，改为强制要求显式配置
    /// - zoneBindings是唯一的数据源（必须在Inspector中配置）
    /// - 每个binding必须有有效的zone和role
    /// - 优先从zoneBindings查询PrimaryAttack（无则回退SecondaryAttack），无需额外的 public DetectionZone 字段
    /// - GetDetectedTargets()和GetDetectedTargetsForRole()都从zoneBindings读取
    ///
    /// 配置要求：
    /// 1. 在Inspector中为敌人配置zoneBindings列表
    /// 2. PrimaryAttack / SecondaryAttack 至少要有一个有效绑定
    /// 3. 可选地添加其他role的binding（Cliff、Alert等）
    ///
    /// 优势：
    /// - 数据源单一，无冲突
    /// - 逻辑清晰简洁
    /// - 无歧义的推断问题
    /// - GetDetectedTargets()/GetDetectedTargetsForRole() 均从 zoneBindings 读取
    /// - 不需要冗余的缓存字段
    ///
    /// v0.3 更新（攻击系统重构）：
    /// - 缓存 PrimaryAttack 检测区到 _primaryAttackZone
    /// - 由基类在 OnEnable/OnDisable 中统一绑定/解绑事件
    /// </summary>
    private void ResolveDetectionZone()
    {
        // 检查zoneBindings是否已配置
        if (zoneBindings == null || zoneBindings.Count == 0)
        {
            Debug.LogError(
                $"[{gameObject.name}] ✗ 检测区配置失败：zoneBindings列表为空！\n" +
                $"请在Inspector中为敌人脚本配置zoneBindings，至少需要一个role=PrimaryAttack或role=SecondaryAttack的binding，" +
                $"并拖拽对应子物体的DetectionZone到zone字段（例如 DZ_Attack / DZ_Ability）。",
                gameObject
            );
            return;
        }

        // 选择可用的“战斗检测区”：优先 PrimaryAttack，其次 SecondaryAttack。
        // 注意：_hasTarget 只由 PrimaryAttack 事件驱动；SecondaryAttack 仅用于法术/能力判定。
        bool hasPrimaryRole = false;
        bool hasSecondaryRole = false;
        DetectionZone primaryZone = null;
        DetectionZone secondaryZone = null;
        int primaryIndex = -1;
        int secondaryIndex = -1;

        for (int i = 0; i < zoneBindings.Count; i++)
        {
            var binding = zoneBindings[i];
            if (binding.role == DetectionZoneBinding.Role.PrimaryAttack)
            {
                hasPrimaryRole = true;
                if (primaryZone == null && binding.zone != null)
                {
                    primaryZone = binding.zone;
                    primaryIndex = i;
                }
            }
            else if (binding.role == DetectionZoneBinding.Role.SecondaryAttack)
            {
                hasSecondaryRole = true;
                if (secondaryZone == null && binding.zone != null)
                {
                    secondaryZone = binding.zone;
                    secondaryIndex = i;
                }
            }
        }

        var chosenZone = primaryZone != null ? primaryZone : secondaryZone;
        var chosenRole = primaryZone != null ? DetectionZoneBinding.Role.PrimaryAttack : DetectionZoneBinding.Role.SecondaryAttack;
        var chosenIndex = primaryZone != null ? primaryIndex : secondaryIndex;

        if (chosenZone == null)
        {
            string hint;
            if (hasPrimaryRole || hasSecondaryRole)
            {
                hint = "已配置 PrimaryAttack/SecondaryAttack 的 role，但对应的 zone 为空（请拖拽子物体的 DetectionZone）。";
            }
            else
            {
                hint = "zoneBindings 未配置 PrimaryAttack/SecondaryAttack（至少需要一个）。";
            }

            Debug.LogError(
                $"[{gameObject.name}] ✗ 检测区配置失败：未找到可用的战斗检测区（PrimaryAttack / SecondaryAttack）！\n" +
                $"检测到的binding数量：{zoneBindings.Count}\n" +
                $"{hint}",
                gameObject
            );
            return;
        }

        // 配置成功：缓存一个“默认检测区”（用于 legacy/泛化访问），并可选缓存 PrimaryAttack。
        detectionZone = chosenZone;
        _primaryAttackZone = primaryZone; // 仅在存在 PrimaryAttack 时启用事件驱动
        _hasTarget = _primaryAttackZone != null
            && _primaryAttackZone.detectedColliders != null
            && _primaryAttackZone.detectedColliders.Count > 0;

        if (debugStateOverlay)
        {
            Debug.Log(
                $"[{gameObject.name}] ✓ 检测区初始化成功\n" +
                $"  配置来源：zoneBindings[{chosenIndex}]\n" +
                $"  {chosenRole} → {chosenZone.gameObject.name}\n" +
                $"  已配置的binding总数：{zoneBindings.Count}",
                gameObject
            );
        }
    }
}

