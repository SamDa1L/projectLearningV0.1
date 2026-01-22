using UnityEngine;
using UnityEditor;
using System.IO;

/// <summary>
/// PickupItem.prefab 模板生成工具
///
/// 契约 [C-Author-1] 通用拾取物 Prefab 规范：
/// - 固定名：PickupItem.prefab
/// - 必备组件：SpriteRenderer + Collider2D(isTrigger=true) + ItemPickup
/// - 世界表现：sprite 由 Inspector 绑定，运行时不自动设置
/// </summary>
public class PickupPrefabGenerator : Editor
{
    // Editor-only template; keep it out of Resources so it won't be included in builds.
    private const string PREFAB_PATH = "Assets/_Generated/Authoring/PickupItem.prefab";

    [MenuItem("Tools/Authoring/Generate PickupItem.prefab Template")]
    private static void GeneratePickupItemPrefab()
    {
        // Ensure directory exists
        string directory = Path.GetDirectoryName(PREFAB_PATH);
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
            Debug.Log($"[PickupPrefabGenerator] Created directory: {directory}");
        }

        // Create GameObject
        GameObject pickupTemplate = new GameObject("PickupItem");

        // Add SpriteRenderer
        SpriteRenderer spriteRenderer = pickupTemplate.AddComponent<SpriteRenderer>();
        spriteRenderer.sprite = null; // Will be set via Inspector
        spriteRenderer.sortingLayerName = "Default";
        spriteRenderer.sortingOrder = 0;

        // Add Collider2D (BoxCollider2D by default)
        BoxCollider2D collider = pickupTemplate.AddComponent<BoxCollider2D>();
        collider.isTrigger = true;
        collider.size = new Vector2(1f, 1f); // Default size

        // Add ItemPickup component
        ItemPickup itemPickup = pickupTemplate.AddComponent<ItemPickup>();
        // Fields will be set via Inspector

        // Save as prefab
        bool success;
        PrefabUtility.SaveAsPrefabAsset(pickupTemplate, PREFAB_PATH, out success);

        if (success)
        {
            Debug.Log($"[PickupPrefabGenerator] Successfully created PickupItem.prefab at {PREFAB_PATH}");
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH));
        }
        else
        {
            Debug.LogError($"[PickupPrefabGenerator] Failed to create PickupItem.prefab at {PREFAB_PATH}");
        }

        // Clean up temporary GameObject
        DestroyImmediate(pickupTemplate);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }
}
