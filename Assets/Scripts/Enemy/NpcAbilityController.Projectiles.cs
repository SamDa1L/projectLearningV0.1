using System.Collections.Generic;
using CastleDB.Runtime;
using UnityEngine;

public partial class NpcAbilityController : MonoBehaviour
{

    private void SpawnProjectile(
        string abilityId,
        AbilityProjectileDefinition projectileDef,
        IReadOnlyList<AbilityOnHitNode> onHitNodes,
        float directionSign)
    {
        if (projectileDef == null || string.IsNullOrWhiteSpace(projectileDef.prefabPath))
        {
            return;
        }

        GameObject prefab = ResolvePrefab(projectileDef.prefabPath);
        if (prefab == null)
        {
            return;
        }

        Transform spawnPoint = ResolveFirePoint();
        Vector3 spawnPosition = spawnPoint != null ? spawnPoint.position : transform.position;

        GameObject projectile = SpawnProjectileInstance(
            projectileDef.prefabPath,
            prefab,
            spawnPosition,
            prefab.transform.rotation,
            out PrefabGameObjectPool pool);

        Vector3 scale = projectile.transform.localScale;
        scale.x = Mathf.Abs(scale.x) * (directionSign >= 0f ? 1f : -1f);
        projectile.transform.localScale = scale;

        var legacy = projectile.GetComponent<Projectile>();
        if (legacy != null)
        {
            legacy.enabled = false;
        }

        var controller = projectile.GetComponent<AbilityProjectileController>();
        if (controller == null)
        {
            controller = projectile.AddComponent<AbilityProjectileController>();
        }

        controller.SetRecycler(pool);
        controller.SetVfxPool(GetOrCreateVfxPool());
        controller.Initialize(gameObject, abilityId, projectileDef, onHitNodes);
    }

    private GameObject SpawnProjectileInstance(
        string prefabPath,
        GameObject prefab,
        Vector3 position,
        Quaternion rotation,
        out PrefabGameObjectPool pool)
    {
        pool = null;

        if (!useProjectilePool || projectilePoolMaxSize <= 0 || prefab == null || string.IsNullOrWhiteSpace(prefabPath))
        {
            return Instantiate(prefab, position, rotation);
        }

        if (!_projectilePoolsByPrefabPath.TryGetValue(prefabPath, out pool) || pool == null)
        {
            pool = new PrefabGameObjectPool(prefab, transform, $"[Pool] {name}.Projectiles", projectilePoolMaxSize);
            _projectilePoolsByPrefabPath[prefabPath] = pool;
        }

        return pool.Get(position, rotation);
    }

    private VfxPoolService GetOrCreateVfxPool()
    {
        if (!useVfxPool || vfxPoolMaxSize <= 0)
        {
            return null;
        }

        if (_vfxPool == null)
        {
            _vfxPool = new VfxPoolService(transform, vfxPoolMaxSize);
        }

        return _vfxPool;
    }

    private GameObject ResolvePrefab(string prefabPath)
    {
        if (string.IsNullOrWhiteSpace(prefabPath))
        {
            return null;
        }

        if (_prefabCache.TryGetValue(prefabPath, out var prefab) && prefab != null)
        {
            return prefab;
        }

        prefab = ResourcesGameAssetProvider.Shared.Load<GameObject>(prefabPath);
        if (prefab != null)
        {
            _prefabCache[prefabPath] = prefab;
        }

        return prefab;
    }

    private Transform ResolveFirePoint()
    {
        if (firePointOverride != null)
        {
            return firePointOverride;
        }

        if (!_searchedFirePoint)
        {
            _searchedFirePoint = true;
            _cachedFirePoint = transform.Find("FirePoint");
        }

        if (_cachedFirePoint != null)
        {
            return _cachedFirePoint;
        }

        return transform;
    }
}
