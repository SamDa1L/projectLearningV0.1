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
    // 这是 Editor 侧的模板资源：不要放进 Resources，避免进入 Build。
    private const string PREFAB_PATH = "Assets/_Generated/Authoring/PickupItem.prefab";

    [MenuItem("Tools/Authoring/Generate PickupItem.prefab Template")]
    private static void GeneratePickupItemPrefab()
    {
        // 确保输出目录存在
        string directory = Path.GetDirectoryName(PREFAB_PATH);
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
            Debug.Log($"[PickupPrefabGenerator] Created directory: {directory}");
        }

        // 创建临时 GameObject（用于保存 Prefab）
        GameObject pickupTemplate = new GameObject("PickupItem");

        // 添加 SpriteRenderer（sprite 由 Inspector 绑定）
        SpriteRenderer spriteRenderer = pickupTemplate.AddComponent<SpriteRenderer>();
        spriteRenderer.sprite = null; // 由 Inspector 设置
        spriteRenderer.sortingLayerName = "Default";
        spriteRenderer.sortingOrder = 0;

        // 添加 Collider2D（默认使用 BoxCollider2D）
        BoxCollider2D collider = pickupTemplate.AddComponent<BoxCollider2D>();
        collider.isTrigger = true;
        collider.size = new Vector2(1f, 1f); // 默认大小

        // 添加 ItemPickup 组件
        ItemPickup itemPickup = pickupTemplate.AddComponent<ItemPickup>();
        // 字段由 Inspector 绑定

        // 保存为 Prefab 资产
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

        // 清理临时对象
        DestroyImmediate(pickupTemplate);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }
}
