using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 可配置能力投射物控制器（0.5 阶段 2：最小闭环）
/// - 运行时注入 AbilityProjectileDefinition（速度/伤害/生命周期/VFX）
/// - 命中时走 Damageable + 可选 OnHitSequence（当前仅支持 ApplyStatus）
/// </summary>
public class AbilityProjectileController : MonoBehaviour
{
    // 注意：Unity 不允许在 MonoBehaviour 的构造/字段初始化（包括静态初始化）阶段调用 NameToLayer。
    // 否则会在类型初始化（.cctor）时直接抛 UnityException，导致 AddComponent 失败。
    // 因此这里使用“延迟缓存”，在 Initialize/Awake 等运行时回调里再初始化。
    private static bool _layerCacheInitialized;
    private static int _layerPlayer = -1;
    private static int _layerEnemy = -1;
    private static int _layerPlayerHitBox = -1;
    private static int _layerEnemyHitBox = -1;

    private Rigidbody2D _rb;
    private Collider2D _collider;

    private IGameObjectRecycler _recycler;
    private VfxPoolService _vfxPool;

    private bool _initialized;
    private bool _finished;

    private GameObject _owner;
    private FactionId _ownerFaction = FactionId.Neutral;
    private string _abilityId;
    private AbilityProjectileDefinition _def;
    private IReadOnlyList<AbilityOnHitNode> _onHitNodes;

    private bool _hasHitMask;
    private int _hitLayerMask;

    private bool _loggedMissingVfx;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _collider = GetComponent<Collider2D>();
    }

    private void OnDisable()
    {
        // 池化安全性：
        // - 投射物生命周期使用协程；池化对象不会 Destroy，因此需要显式 StopAllCoroutines
        // - 必须恢复对 owner 的 IgnoreCollision，否则复用后可能永远打不到旧 owner
        StopAllCoroutines();
        RestoreOwnerCollisions();

        if (_rb != null)
        {
            _rb.velocity = Vector2.zero;
        }

        _initialized = false;
        _finished = false;

        _owner = null;
        _ownerFaction = FactionId.Neutral;
        _abilityId = "";
        _def = null;
        _onHitNodes = null;

        _hasHitMask = false;
        _hitLayerMask = 0;
        _loggedMissingVfx = false;
    }

    public void SetRecycler(IGameObjectRecycler recycler)
    {
        _recycler = recycler;
    }

    public void SetVfxPool(VfxPoolService vfxPool)
    {
        _vfxPool = vfxPool;
    }

    public void Initialize(
        GameObject owner,
        string abilityId,
        AbilityProjectileDefinition def,
        IReadOnlyList<AbilityOnHitNode> onHitNodes)
    {
        if (_initialized)
        {
            return;
        }

        EnsureLayerCache();

        _initialized = true;
        _owner = owner;
        _ownerFaction = ResolveOwnerFaction(owner);
        _abilityId = abilityId ?? "";
        _def = def;
        _onHitNodes = onHitNodes;

        // 关键：投射物需要继承施法者阵营（尤其是“召唤物覆写阵营”场景）。
        // 否则会出现：召唤物换成 Player 层了，但投射物仍停留在 EnemyHitBox，导致碰撞矩阵/命中判定全错。
        ApplyFactionToProjectile(gameObject, _ownerFaction);

        InitializeHitMask();

        if (_rb == null)
        {
            Debug.LogError($"[AbilityProjectileController] Rigidbody2D 缺失，无法移动 (abilityId='{_abilityId}')", this);
            return;
        }

        IgnoreOwnerCollisions();
        ApplyVelocity();

        if (_def != null && _def.lifetime > 0f)
        {
            StartCoroutine(ExpireAfterSeconds(_def.lifetime));
        }
    }

    private void Despawn()
    {
        if (_recycler != null)
        {
            _recycler.Recycle(gameObject);
            return;
        }

        Destroy(gameObject);
    }

    private void IgnoreOwnerCollisions()
    {
        RestoreOwnerCollisions();

        if (_owner == null || _collider == null)
        {
            return;
        }

        _ignoredOwnerColliders = _owner.GetComponentsInChildren<Collider2D>(true);
        foreach (var ownerCollider in _ignoredOwnerColliders)
        {
            if (ownerCollider == null)
            {
                continue;
            }

            Physics2D.IgnoreCollision(_collider, ownerCollider, true);
        }
    }

    private Collider2D[] _ignoredOwnerColliders;

    private void RestoreOwnerCollisions()
    {
        if (_ignoredOwnerColliders == null || _ignoredOwnerColliders.Length == 0 || _collider == null)
        {
            _ignoredOwnerColliders = null;
            return;
        }

        for (int i = 0; i < _ignoredOwnerColliders.Length; i++)
        {
            var ownerCollider = _ignoredOwnerColliders[i];
            if (ownerCollider == null)
            {
                continue;
            }

            Physics2D.IgnoreCollision(_collider, ownerCollider, false);
        }

        _ignoredOwnerColliders = null;
    }

    private void ApplyVelocity()
    {
        if (_def == null)
        {
            return;
        }

        float dirSign = transform.localScale.x >= 0f ? 1f : -1f;
        _rb.velocity = new Vector2(_def.speed * dirSign, 0f);
    }

    private IEnumerator ExpireAfterSeconds(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        Expire();
    }

    private void Expire()
    {
        if (_finished)
        {
            return;
        }

        _finished = true;

        Vector3 pos = transform.position;
        string vfxPath = ResolveExpireVfxPath();
        float vfxDuration = ResolveExpireVfxDuration();
        SpawnVfx(vfxPath, pos, vfxDuration);
        Despawn();
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (!_initialized || _finished || collision == null)
        {
            return;
        }

        if (_owner != null && collision.transform != null)
        {
            Transform ownerTransform = _owner.transform;
            if (collision.transform == ownerTransform || collision.transform.IsChildOf(ownerTransform))
            {
                return; // 过滤自伤
            }
        }

        if (_hasHitMask && (_hitLayerMask & (1 << collision.gameObject.layer)) == 0)
        {
            return; // Layer 过滤
        }

        Damageable damageable = collision.GetComponentInParent<Damageable>();
        if (damageable == null)
        {
            return;
        }

        // 阵营判定：仅对敌对阵营造成伤害（Enemy <-> Friend 才敌对，Neutral 不敌对）。
        // 注意：这里用 Damageable 所在对象取阵营，避免 child collider 的 layer 与阵营不一致。
        FactionId targetFaction = ResolveTargetFaction(damageable);
        if (!FactionUtility.IsHostile(_ownerFaction, targetFaction))
        {
            return;
        }

        if (_def == null)
        {
            Debug.LogWarning($"[AbilityProjectileController] 投射物定义为空，跳过结算 (abilityId='{_abilityId}')", this);
            return;
        }

        if (_def.baseDamage <= 0)
        {
            Debug.LogWarning($"[AbilityProjectileController] baseDamage<=0，跳过结算 (abilityId='{_abilityId}', projectileId='{_def.id}')", this);
            return;
        }

        Vector2 hitPoint = collision.ClosestPoint(transform.position);
        bool gotHit = damageable.Hit(_def.baseDamage, Vector2.zero, hitPoint);
        if (!gotHit)
        {
            return; // 目标可能无敌/已死亡
        }

        ExecuteOnHitSequence(damageable.gameObject);
        SpawnVfx(_def.onHitVfxPath, hitPoint, _def.onHitVfxDuration);

        _finished = true;
        Despawn();
    }

    private void InitializeHitMask()
    {
        _hasHitMask = false;
        _hitLayerMask = 0;

        EnsureLayerCache();

        if (_def == null || string.IsNullOrWhiteSpace(_def.hitMask))
        {
            return;
        }

        _hasHitMask = true;
        _hitLayerMask = BuildLayerMask(_def.hitMask, out List<string> unknownLayers);

        // 兼容：内容侧有时只会填 Player/Enemy，而实际碰撞发生在 PlayerHitBox/EnemyHitBox（或反过来）。
        // 这里做“成对补齐”，减少漏判。
        ExpandCharacterLayerPairs(ref _hitLayerMask);

        // 长期方案：当 hitMask 指向“角色层”时，按施法者阵营把“敌对阵营层”也补进来。
        // 这样同一套怪物投射物配置可以被 Friend/Enemy 双方复用（召唤物覆写阵营时尤为重要）。
        if (ContainsAnyCharacterLayer(_hitLayerMask))
        {
            AddHostileCharacterLayers(ref _hitLayerMask, _ownerFaction);
        }

        if (unknownLayers.Count > 0)
        {
            Debug.LogWarning(
                $"[AbilityProjectileController] hitMask 包含未知 Layer: [{string.Join(", ", unknownLayers)}] " +
                $"(abilityId='{_abilityId}', projectileId='{_def.id}', hitMask='{_def.hitMask}')",
                this);
        }

        if (_hitLayerMask == 0)
        {
            Debug.LogError(
                $"[AbilityProjectileController] hitMask 解析为空（将永远无法命中） " +
                $"(abilityId='{_abilityId}', projectileId='{_def.id}', hitMask='{_def.hitMask}')",
                this);
        }
    }

    private static int BuildLayerMask(string raw, out List<string> unknownLayers)
    {
        unknownLayers = new List<string>();

        if (string.IsNullOrWhiteSpace(raw))
        {
            return 0;
        }

        int mask = 0;
        string[] tokens = raw.Split(new[] { ',', ';', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < tokens.Length; i++)
        {
            string token = tokens[i]?.Trim();
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            int layer = LayerMask.NameToLayer(token);
            if (layer < 0)
            {
                unknownLayers.Add(token);
                continue;
            }

            mask |= 1 << layer;
        }

        return mask;
    }

    private static FactionId ResolveOwnerFaction(GameObject owner)
    {
        // 注意：owner 阵营为 None 通常意味着错误配置（或忘记挂 FactionMember）。
        // 这里回退为 Neutral，避免误伤；同时保留日志便于排查。
        FactionId faction = FactionUtility.GetFaction(owner);
        if (faction == FactionId.None)
        {
            Debug.LogWarning("[AbilityProjectileController] owner 阵营为 None，已按 Neutral 处理（请检查配置/导入链路）");
            return FactionId.Neutral;
        }

        return faction;
    }

    private static FactionId ResolveTargetFaction(Damageable damageable)
    {
        if (damageable == null)
        {
            return FactionId.Neutral;
        }

        FactionId faction = FactionUtility.GetFaction(damageable.gameObject);
        if (faction == FactionId.None)
        {
            return FactionId.Neutral;
        }

        return faction;
    }

    private static void ApplyFactionToProjectile(GameObject projectileRoot, FactionId faction)
    {
        if (projectileRoot == null)
        {
            return;
        }

        // 注意：Neutral 不参与敌对关系；这里不强行改 Layer，避免把特殊投射物错误映射到 Default 后影响其它碰撞规则。
        if (faction != FactionId.Enemy && faction != FactionId.Friend)
        {
            return;
        }

        FactionLayerApplier.Apply(projectileRoot, faction);
    }

    private static bool ContainsAnyCharacterLayer(int mask)
    {
        return HasLayer(mask, _layerPlayer)
               || HasLayer(mask, _layerPlayerHitBox)
               || HasLayer(mask, _layerEnemy)
               || HasLayer(mask, _layerEnemyHitBox);
    }

    private static void ExpandCharacterLayerPairs(ref int mask)
    {
        // 只要命中了“本体层”或“HitBox 层”，就把对应的另一层也加上。
        // 这样就算内容只写了一个，也不至于因为碰撞发生在另一层而完全打不到。
        if (HasLayer(mask, _layerPlayer))
        {
            AddLayer(ref mask, _layerPlayerHitBox);
        }

        if (HasLayer(mask, _layerPlayerHitBox))
        {
            AddLayer(ref mask, _layerPlayer);
        }

        if (HasLayer(mask, _layerEnemy))
        {
            AddLayer(ref mask, _layerEnemyHitBox);
        }

        if (HasLayer(mask, _layerEnemyHitBox))
        {
            AddLayer(ref mask, _layerEnemy);
        }
    }

    private static void AddHostileCharacterLayers(ref int mask, FactionId ownerFaction)
    {
        // 仅 Enemy <-> Friend 敌对；Neutral 不敌对。
        if (ownerFaction == FactionId.Enemy)
        {
            AddLayer(ref mask, _layerPlayer);
            AddLayer(ref mask, _layerPlayerHitBox);
        }
        else if (ownerFaction == FactionId.Friend)
        {
            AddLayer(ref mask, _layerEnemy);
            AddLayer(ref mask, _layerEnemyHitBox);
        }
    }

    private static void EnsureLayerCache()
    {
        if (_layerCacheInitialized)
        {
            return;
        }

        _layerCacheInitialized = true;
        _layerPlayer = LayerMask.NameToLayer("Player");
        _layerEnemy = LayerMask.NameToLayer("Enemy");
        _layerPlayerHitBox = LayerMask.NameToLayer("PlayerHitBox");
        _layerEnemyHitBox = LayerMask.NameToLayer("EnemyHitBox");
    }

    private static bool HasLayer(int mask, int layer)
    {
        if (layer < 0)
        {
            return false;
        }

        return (mask & (1 << layer)) != 0;
    }

    private static void AddLayer(ref int mask, int layer)
    {
        if (layer < 0)
        {
            return;
        }

        mask |= 1 << layer;
    }

    private void ExecuteOnHitSequence(GameObject target)
    {
        if (_onHitNodes == null || _onHitNodes.Count == 0 || target == null)
        {
            return;
        }

        for (int i = 0; i < _onHitNodes.Count; i++)
        {
            var node = _onHitNodes[i];
            if (node == null)
            {
                continue;
            }

            switch (node.nodeType)
            {
                case AbilityOnHitNodeType.ApplyStatus:
                    ApplyStatus(target, node.statusId);
                    break;
                default:
                    // 0.5 阶段 2 最小闭环：仅实现 ApplyStatus
                    break;
            }
        }
    }

    private void ApplyStatus(GameObject target, string statusId)
    {
        if (string.IsNullOrWhiteSpace(statusId))
        {
            return;
        }

        StatusEffectController controller = target.GetComponent<StatusEffectController>();
        if (controller == null)
        {
            controller = target.GetComponentInParent<StatusEffectController>();
        }

        if (controller == null)
        {
            return;
        }

        controller.Apply(statusId);
    }

    private string ResolveExpireVfxPath()
    {
        if (_def == null)
        {
            return "";
        }

        if (!string.IsNullOrWhiteSpace(_def.onExpireVfxPath))
        {
            return _def.onExpireVfxPath;
        }

        return _def.onHitVfxPath;
    }

    private float ResolveExpireVfxDuration()
    {
        if (_def == null)
        {
            return 0f;
        }

        if (!string.IsNullOrWhiteSpace(_def.onExpireVfxPath))
        {
            return Mathf.Max(0f, _def.onExpireVfxDuration);
        }

        return Mathf.Max(0f, _def.onHitVfxDuration);
    }

    private void SpawnVfx(string vfxPath, Vector3 position, float destroyAfterSeconds = 0f)
    {
        if (string.IsNullOrWhiteSpace(vfxPath))
        {
            return;
        }

        if (_vfxPool != null)
        {
            _vfxPool.SpawnOneShot(vfxPath, position, destroyAfterSeconds, this);
            return;
        }

        GameObject prefab = ResourcesGameAssetProvider.Shared.Load<GameObject>(vfxPath);
        if (prefab == null)
        {
            if (!_loggedMissingVfx)
            {
                Debug.LogWarning($"[AbilityProjectileController] VFX prefab 未找到: '{vfxPath}' (abilityId='{_abilityId}')", this);
                _loggedMissingVfx = true;
            }
            return;
        }

        GameObject instance = Instantiate(prefab, position, prefab.transform.rotation);
        if (destroyAfterSeconds > 0f)
        {
            Destroy(instance, destroyAfterSeconds);
        }
    }
}
