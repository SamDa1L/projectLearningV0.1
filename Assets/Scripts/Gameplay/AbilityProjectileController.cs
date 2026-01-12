using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 可配置能力投射物控制器（0.5 Phase 2：最小闭环）
/// - 运行时注入 AbilityProjectileDefinition（速度/伤害/生命周期/VFX）
/// - 命中时走 Damageable + 可选 OnHitSequence（当前仅支持 ApplyStatus）
/// </summary>
public class AbilityProjectileController : MonoBehaviour
{
    private Rigidbody2D _rb;
    private Collider2D _collider;

    private bool _initialized;
    private bool _finished;

    private GameObject _owner;
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

        _initialized = true;
        _owner = owner;
        _abilityId = abilityId ?? "";
        _def = def;
        _onHitNodes = onHitNodes;
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

    private void IgnoreOwnerCollisions()
    {
        if (_owner == null || _collider == null)
        {
            return;
        }

        Collider2D[] ownerColliders = _owner.GetComponentsInChildren<Collider2D>(true);
        foreach (var ownerCollider in ownerColliders)
        {
            if (ownerCollider == null)
            {
                continue;
            }

            Physics2D.IgnoreCollision(_collider, ownerCollider, true);
        }
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
        Destroy(gameObject);
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
        Destroy(gameObject);
    }

    private void InitializeHitMask()
    {
        _hasHitMask = false;
        _hitLayerMask = 0;

        if (_def == null || string.IsNullOrWhiteSpace(_def.hitMask))
        {
            return;
        }

        _hasHitMask = true;
        _hitLayerMask = BuildLayerMask(_def.hitMask, out List<string> unknownLayers);

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
                    // Phase 2 最小闭环：仅实现 ApplyStatus
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

        GameObject prefab = Resources.Load<GameObject>(vfxPath);
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
