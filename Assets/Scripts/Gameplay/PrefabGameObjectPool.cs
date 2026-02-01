using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 按 Prefab 维度的最小对象池（2.3）。
/// - 用于高频对象（投射物/VFX）减少 Instantiate/Destroy 抖动。
/// - 由调用方持有实例，避免静态单例扩散依赖。
/// </summary>
public interface IGameObjectRecycler
{
    void Recycle(GameObject instance);
}

public sealed class PrefabGameObjectPool : IGameObjectRecycler
{
    private readonly GameObject _prefab;
    private readonly Transform _poolRootParent;
    private readonly string _poolRootName;
    private readonly int _maxSize;

    private readonly Stack<GameObject> _pool = new Stack<GameObject>();
    private Transform _poolRoot;

    public int InstantiateCount { get; private set; }
    public int ReuseCount { get; private set; }

    public PrefabGameObjectPool(GameObject prefab, Transform poolRootParent, string poolRootName, int maxSize)
    {
        _prefab = prefab;
        _poolRootParent = poolRootParent;
        _poolRootName = string.IsNullOrWhiteSpace(poolRootName) ? "[Pool]" : poolRootName.Trim();
        _maxSize = Mathf.Max(0, maxSize);
    }

    public GameObject Get(Vector3 position, Quaternion rotation, Transform parent = null)
    {
        if (_prefab == null)
        {
            return null;
        }

        // 优先复用池内对象；跳过已被销毁/为空的条目。
        GameObject instance = null;
        while (_pool.Count > 0 && instance == null)
        {
            instance = _pool.Pop();
        }

        if (instance != null)
        {
            ReuseCount++;
            var t = instance.transform;
            t.SetParent(parent, worldPositionStays: false);
            t.SetPositionAndRotation(position, rotation);
            instance.SetActive(true);
            return instance;
        }

        InstantiateCount++;
        return parent != null
            ? Object.Instantiate(_prefab, position, rotation, parent)
            : Object.Instantiate(_prefab, position, rotation);
    }

    public void Recycle(GameObject instance)
    {
        if (instance == null)
        {
            return;
        }

        if (_maxSize <= 0 || _pool.Count >= _maxSize)
        {
            Object.Destroy(instance);
            return;
        }

        instance.SetActive(false);
        instance.transform.SetParent(GetOrCreatePoolRoot(), worldPositionStays: false);
        _pool.Push(instance);
    }

    private Transform GetOrCreatePoolRoot()
    {
        if (_poolRoot != null)
        {
            return _poolRoot;
        }

        var go = new GameObject(_poolRootName);
        if (_poolRootParent != null)
        {
            go.transform.SetParent(_poolRootParent, worldPositionStays: false);
        }

        _poolRoot = go.transform;
        return _poolRoot;
    }
}
