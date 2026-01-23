using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 简易 VFX 生成器（可选池化）（2.3）。
/// - 以 Resources 路径（vfxPath）作为 key 建池，保持数据驱动。
/// - 必须提供 duration：duration<=0 时回退为 Instantiate（不走对象池）。
/// </summary>
public sealed class VfxPoolService
{
    private readonly Transform _poolRootParent;
    private readonly int _poolMaxSizePerPrefab;

    private readonly Dictionary<string, GameObject> _prefabByPath = new Dictionary<string, GameObject>();
    private readonly Dictionary<string, PrefabGameObjectPool> _poolsByPath = new Dictionary<string, PrefabGameObjectPool>();
    private readonly HashSet<string> _loggedMissingPaths = new HashSet<string>();

    public VfxPoolService(Transform poolRootParent, int poolMaxSizePerPrefab)
    {
        _poolRootParent = poolRootParent;
        _poolMaxSizePerPrefab = Mathf.Max(0, poolMaxSizePerPrefab);
    }

    public void SpawnOneShot(string vfxPath, Vector3 position, float durationSeconds, Object context = null)
    {
        if (string.IsNullOrWhiteSpace(vfxPath))
        {
            return;
        }

        if (!_prefabByPath.TryGetValue(vfxPath, out GameObject prefab) || prefab == null)
        {
            prefab = ResourcesGameAssetProvider.Shared.Load<GameObject>(vfxPath);
            if (prefab != null)
            {
                _prefabByPath[vfxPath] = prefab;
            }
        }

        if (prefab == null)
        {
            if (_loggedMissingPaths.Add(vfxPath))
            {
                Debug.LogWarning($"[VfxPoolService] VFX prefab not found: '{vfxPath}'", context);
            }
            return;
        }

        // 没有可靠的回收时机（或池容量为 0）时不池化，保持旧行为。
        if (durationSeconds <= 0f || _poolMaxSizePerPrefab <= 0)
        {
            GameObject instance = Object.Instantiate(prefab, position, prefab.transform.rotation);
            if (durationSeconds > 0f)
            {
                Object.Destroy(instance, durationSeconds);
            }
            return;
        }

        PrefabGameObjectPool pool = GetOrCreatePool(vfxPath, prefab);
        GameObject pooled = pool.Get(position, prefab.transform.rotation);

        var timer = pooled.GetComponent<PooledRecycleTimer>();
        if (timer == null)
        {
            timer = pooled.AddComponent<PooledRecycleTimer>();
        }

        timer.Arm(pool, durationSeconds);
    }

    private PrefabGameObjectPool GetOrCreatePool(string vfxPath, GameObject prefab)
    {
        if (_poolsByPath.TryGetValue(vfxPath, out var pool) && pool != null)
        {
            return pool;
        }

        string poolName = prefab != null
            ? $"[Pool] VFX({prefab.name})"
            : "[Pool] VFX";

        pool = new PrefabGameObjectPool(prefab, _poolRootParent, poolName, _poolMaxSizePerPrefab);
        _poolsByPath[vfxPath] = pool;
        return pool;
    }
}
