using UnityEngine;

public static class AbilityBuffVfx
{
    public const float DefaultExpireVfxDurationSeconds = 1f;

    public static GameObject SpawnLoop(AbilityBuffDefinition def, Transform targetRoot)
    {
        if (def == null || targetRoot == null || string.IsNullOrWhiteSpace(def.prefabPath))
        {
            return null;
        }

        GameObject prefab = ResourcesGameAssetProvider.Shared.Load<GameObject>(def.prefabPath);
        if (prefab == null)
        {
            return null;
        }

        Transform attachPoint = ResolveAttachPoint(targetRoot, def.attachPointPath);

        GameObject instance;
        if (def.followTarget)
        {
            instance = Object.Instantiate(prefab);
            instance.transform.SetParent(attachPoint, worldPositionStays: false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
        }
        else
        {
            instance = Object.Instantiate(prefab, attachPoint.position, prefab.transform.rotation);
        }

        if (def.prefabDuration > 0f)
        {
            Object.Destroy(instance, def.prefabDuration);
        }

        return instance;
    }

    public static void DestroyLoop(GameObject loopInstance)
    {
        if (loopInstance != null)
        {
            Object.Destroy(loopInstance);
        }
    }

    public static GameObject SpawnExpire(AbilityBuffDefinition def, Vector3 worldPosition)
    {
        if (def == null || string.IsNullOrWhiteSpace(def.onExpireVfxPath))
        {
            return null;
        }

        GameObject prefab = ResourcesGameAssetProvider.Shared.Load<GameObject>(def.onExpireVfxPath);
        if (prefab == null)
        {
            return null;
        }

        GameObject instance = Object.Instantiate(prefab, worldPosition, prefab.transform.rotation);

        float duration = def.onExpireVfxDuration > 0f ? def.onExpireVfxDuration : DefaultExpireVfxDurationSeconds;
        Object.Destroy(instance, duration);
        return instance;
    }

    public static Transform ResolveAttachPoint(Transform targetRoot, string attachPointPath)
    {
        if (targetRoot == null || string.IsNullOrWhiteSpace(attachPointPath))
        {
            return targetRoot;
        }

        string[] tokens = attachPointPath.Split('/');
        Transform current = targetRoot;

        for (int i = 0; i < tokens.Length; i++)
        {
            string token = tokens[i]?.Trim();
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            Transform next = null;
            for (int j = 0; j < current.childCount; j++)
            {
                Transform child = current.GetChild(j);
                if (child != null && child.name == token)
                {
                    next = child;
                    break;
                }
            }

            if (next == null)
            {
                return targetRoot;
            }

            current = next;
        }

        return current != null ? current : targetRoot;
    }
}
