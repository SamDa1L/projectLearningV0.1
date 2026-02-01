using UnityEngine;

public abstract partial class EnemyAgentBase
{
    protected virtual void Awake()
    {
        // ===== 组件缓存 =====
        CacheComponents();

        // ===== 解决检测区依赖 =====
        ResolveDetectionZone();

        // ===== 初始化钩子 =====
        Initialize();
    }

    protected virtual void OnEnable()
    {
        // ===== 绑定 PrimaryAttack 检测区事件 =====
        if (_primaryAttackZone != null)
        {
            _primaryAttackZone.OnDetectedTargetsChanged.AddListener(OnPrimaryAttackTargetsChanged);

            if (debugStateOverlay)
            {
                Debug.Log($"[{gameObject.name}] 已绑定 PrimaryAttack 检测区事件", gameObject);
            }
        }
    }

    protected virtual void OnDisable()
    {
        // ===== 解绑 PrimaryAttack 检测区事件 =====
        if (_primaryAttackZone != null)
        {
            _primaryAttackZone.OnDetectedTargetsChanged.RemoveListener(OnPrimaryAttackTargetsChanged);

            if (debugStateOverlay)
            {
                Debug.Log($"[{gameObject.name}] 已解绑 PrimaryAttack 检测区事件", gameObject);
            }
        }
    }

    /// <summary>
    /// PrimaryAttack 检测区目标变化事件回调
    /// 由基类统一处理，更新 _hasTarget 标记
    /// </summary>
    private void OnPrimaryAttackTargetsChanged()
    {
        if (_primaryAttackZone != null)
        {
            int hostileCount = CountHostileColliders(_primaryAttackZone.detectedColliders);
            _hasTarget = hostileCount > 0;

            if (debugStateOverlay)
            {
                Debug.Log($"[{gameObject.name}] PrimaryAttack 目标变化：hasTarget={_hasTarget}, hostileCount={hostileCount}, rawCount={_primaryAttackZone.detectedColliders.Count}");
            }
        }
    }

    protected virtual void Update()
    {
        // ===== 击退保护计时器递减 =====
        if (_knockbackProtectionTimer > 0f)
        {
            _knockbackProtectionTimer -= Time.deltaTime;
        }

        // ===== 状态机更新 =====
        TickState(Time.deltaTime);

        // ===== 调试显示 =====
        #if UNITY_EDITOR
        UpdateDebugOverlay();
        #endif
    }

    protected virtual void FixedUpdate()
    {
        // ===== 所有物理操作集中在这里 =====
        TickPhysics(Time.fixedDeltaTime);
    }
}

