using UnityEngine;
using UnityEditor;
using CastleDB.Runtime;
using System.Linq;

/// <summary>
/// Pickup 快速创建工具
///
/// 契约 [C-Author-1] 可选效率工具：
/// - 右键 Create/Pickup From Item... 快速生成拾取物
/// - 自动添加 SpriteRenderer + Collider2D + ItemPickup
/// - 自动命名 Pickup_<itemId>
/// - 自动绑定 sprite（从 ItemDefinition.icon）
/// </summary>
public class PickupCreationTool : Editor
{
    [MenuItem("GameObject/Create Pickup From Item...", false, 10)]
    private static void CreatePickupFromItem()
    {
        PickupCreationWindow.ShowWindow();
    }
}

/// <summary>
/// Pickup 创建窗口
/// </summary>
public class PickupCreationWindow : EditorWindow
{
    private ItemCatalog _itemCatalog;
    private ItemDefinition[] _allItems;
    private string[] _itemDisplayNames;
    private int _selectedIndex = 0;
    private bool _catalogLoaded = false;

    public static void ShowWindow()
    {
        PickupCreationWindow window = GetWindow<PickupCreationWindow>("Create Pickup");
        window.minSize = new Vector2(400, 200);
        window.LoadItemCatalog();
        window.Show();
    }

    private void LoadItemCatalog()
    {
        _itemCatalog = Resources.Load<ItemCatalog>("Config/ItemCatalog");

        if (_itemCatalog == null)
        {
            Debug.LogError("[PickupCreationTool] Failed to load ItemCatalog at 'Resources/Config/ItemCatalog'");
            _catalogLoaded = false;
            return;
        }

        _allItems = _itemCatalog.GetAllItems().ToArray();

        if (_allItems == null || _allItems.Length == 0)
        {
            Debug.LogError("[PickupCreationTool] ItemCatalog is empty");
            _catalogLoaded = false;
            return;
        }

        // Build display names
        _itemDisplayNames = new string[_allItems.Length];

        for (int i = 0; i < _allItems.Length; i++)
        {
            _itemDisplayNames[i] = $"{_allItems[i].id} - {_allItems[i].displayName}";
        }

        _catalogLoaded = true;
    }

    private void OnGUI()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Create Pickup From Item", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        if (!_catalogLoaded)
        {
            EditorGUILayout.HelpBox("ItemCatalog not loaded. Please import Item.cdb first.", MessageType.Error);

            if (GUILayout.Button("Reload Catalog"))
            {
                LoadItemCatalog();
            }

            return;
        }

        // Item selection
        EditorGUILayout.LabelField("Select Item:");
        _selectedIndex = EditorGUILayout.Popup(_selectedIndex, _itemDisplayNames);

        EditorGUILayout.Space();

        // Preview
        if (_selectedIndex >= 0 && _selectedIndex < _allItems.Length)
        {
            ItemDefinition selectedItem = _allItems[_selectedIndex];

            EditorGUILayout.LabelField("Preview:", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Item ID:", selectedItem.id);
            EditorGUILayout.LabelField("Display Name:", selectedItem.displayName);
            EditorGUILayout.LabelField("Item Type:", selectedItem.itemType.ToString());

            if (selectedItem.itemType == ItemType.Ability)
            {
                EditorGUILayout.LabelField("Ability ID:", selectedItem.abilityId);
            }

            if (selectedItem.itemType == ItemType.Material)
            {
                EditorGUILayout.HelpBox("⚠ WARNING: Material items are NOT supported for pickup in 0.4", MessageType.Warning);
            }
        }

        EditorGUILayout.Space();

        // Create button
        if (GUILayout.Button("Create Pickup GameObject", GUILayout.Height(30)))
        {
            CreatePickupGameObject();
        }
    }

    private void CreatePickupGameObject()
    {
        if (!_catalogLoaded || _selectedIndex < 0 || _selectedIndex >= _allItems.Length)
        {
            Debug.LogError("[PickupCreationTool] Invalid item selection");
            return;
        }

        ItemDefinition selectedItem = _allItems[_selectedIndex];

        // Create GameObject
        GameObject pickupObj = new GameObject($"Pickup_{selectedItem.id}");

        // Add SpriteRenderer
        SpriteRenderer spriteRenderer = pickupObj.AddComponent<SpriteRenderer>();

        // Load and set sprite
        if (!string.IsNullOrWhiteSpace(selectedItem.icon))
        {
            Sprite sprite = Resources.Load<Sprite>(selectedItem.icon);

            if (sprite != null)
            {
                spriteRenderer.sprite = sprite;
            }
            else
            {
                Debug.LogWarning($"[PickupCreationTool] Failed to load sprite at '{selectedItem.icon}'");
            }
        }

        // Add Collider2D (BoxCollider2D by default)
        BoxCollider2D collider = pickupObj.AddComponent<BoxCollider2D>();
        collider.isTrigger = true;

        // Auto-size collider to sprite
        if (spriteRenderer.sprite != null)
        {
            collider.size = spriteRenderer.sprite.bounds.size;
        }

        // Add ItemPickup component
        ItemPickup itemPickup = pickupObj.AddComponent<ItemPickup>();
        itemPickup.itemId = selectedItem.id;
        itemPickup.amount = selectedItem.itemType == ItemType.Ability ? 1 : 1;
        itemPickup.autoPickup = true;

        // Register Undo
        Undo.RegisterCreatedObjectUndo(pickupObj, "Create Pickup GameObject");

        // Select the new GameObject
        Selection.activeGameObject = pickupObj;

        Debug.Log($"[PickupCreationTool] Created pickup '{pickupObj.name}' for item '{selectedItem.id}'");

        // Close window
        Close();
    }
}
