using UnityEngine;
using UnityEditor;
using CastleDB.Runtime;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// ItemPickup 自定义 Inspector
///
/// 契约 [C-Author-1] 功能：
/// - itemId 下拉选择（来自 ItemCatalog，显示 id + displayName）
/// - 选中后自动设置 SpriteRenderer.sprite（来自 icon 路径）
/// - itemType==Ability 强制 amount=1
/// - itemType==Material 显示醒目标记 + Error
/// - 预览：显示 icon、itemType、abilityId
/// - 校验：itemId 存在性、icon 资源可加载、SpriteRenderer 非空
/// </summary>
[CustomEditor(typeof(ItemPickup))]
public class ItemPickupEditor : Editor
{
    // ===== Cached Data =====
    private ItemCatalog _itemCatalog;
    private List<ItemDefinition> _allItems;
    private string[] _itemDisplayNames;
    private int _selectedIndex = -1;
    private bool _catalogLoaded = false;

    // ===== Style =====
    private GUIStyle _errorStyle;
    private GUIStyle _warningStyle;
    private GUIStyle _infoStyle;

    // ===== Lifecycle =====
    private void OnEnable()
    {
        LoadItemCatalog();
    }

    public override void OnInspectorGUI()
    {
        ItemPickup pickup = (ItemPickup)target;

        InitStyles();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Item Pickup Configuration", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // ===== Item ID Dropdown =====
        DrawItemIdDropdown(pickup);

        EditorGUILayout.Space();

        // ===== Amount Field =====
        DrawAmountField(pickup);

        EditorGUILayout.Space();

        // ===== Auto Pickup Field =====
        DrawAutoPickupField(pickup);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

        // ===== Preview Section =====
        DrawPreview(pickup);

        EditorGUILayout.Space();

        // ===== Validation Section =====
        DrawValidation(pickup);

        // Apply modifications
        if (GUI.changed)
        {
            EditorUtility.SetDirty(pickup);
        }
    }

    // ===== Load ItemCatalog =====
    private void LoadItemCatalog()
    {
        _itemCatalog = Resources.Load<ItemCatalog>("Config/ItemCatalog");

        if (_itemCatalog == null)
        {
            _catalogLoaded = false;
            return;
        }

        _allItems = _itemCatalog.GetAllItems().ToList();

        if (_allItems == null || _allItems.Count == 0)
        {
            _catalogLoaded = false;
            return;
        }

        // Build display names: "id - displayName"
        _itemDisplayNames = new string[_allItems.Count + 1];
        _itemDisplayNames[0] = "(None)";

        for (int i = 0; i < _allItems.Count; i++)
        {
            var item = _allItems[i];
            _itemDisplayNames[i + 1] = $"{item.id} - {item.displayName}";
        }

        _catalogLoaded = true;
    }

    // ===== Init Styles =====
    private void InitStyles()
    {
        if (_errorStyle == null)
        {
            _errorStyle = new GUIStyle(EditorStyles.helpBox);
            _errorStyle.normal.textColor = new Color(1f, 0.3f, 0.3f);
            _errorStyle.fontSize = 11;
            _errorStyle.wordWrap = true;
        }

        if (_warningStyle == null)
        {
            _warningStyle = new GUIStyle(EditorStyles.helpBox);
            _warningStyle.normal.textColor = new Color(1f, 0.7f, 0f);
            _warningStyle.fontSize = 11;
            _warningStyle.wordWrap = true;
        }

        if (_infoStyle == null)
        {
            _infoStyle = new GUIStyle(EditorStyles.helpBox);
            _infoStyle.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
            _infoStyle.fontSize = 11;
            _infoStyle.wordWrap = true;
        }
    }

    // ===== Draw Item ID Dropdown =====
    private void DrawItemIdDropdown(ItemPickup pickup)
    {
        if (!_catalogLoaded)
        {
            EditorGUILayout.HelpBox("ItemCatalog not loaded. Please import Item.cdb first.", MessageType.Warning);

            // Fallback: text field
            EditorGUI.BeginChangeCheck();
            string newItemId = EditorGUILayout.TextField("Item ID", pickup.itemId);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(pickup, "Change Item ID");
                pickup.itemId = newItemId;
            }
            return;
        }

        // Find current index
        _selectedIndex = 0; // Default to (None)
        if (!string.IsNullOrWhiteSpace(pickup.itemId))
        {
            for (int i = 0; i < _allItems.Count; i++)
            {
                if (_allItems[i].id == pickup.itemId)
                {
                    _selectedIndex = i + 1;
                    break;
                }
            }
        }

        // Dropdown
        EditorGUI.BeginChangeCheck();
        int newIndex = EditorGUILayout.Popup("Item ID", _selectedIndex, _itemDisplayNames);

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(pickup, "Change Item ID");

            if (newIndex == 0)
            {
                pickup.itemId = "";
            }
            else
            {
                ItemDefinition selectedItem = _allItems[newIndex - 1];
                pickup.itemId = selectedItem.id;

                // Auto-bind sprite
                AutoBindSprite(pickup, selectedItem);

                // 自动修正：Ability/Relic 的 amount 固定为 1
                if (selectedItem.itemType == ItemType.Ability || selectedItem.itemType == ItemType.Relic)
                {
                    if (pickup.amount != 1)
                    {
                        Debug.LogWarning($"[ItemPickupEditor] Ability/Relic 类型物品 '{selectedItem.id}' 的 amount 自动修正为 1（原值={pickup.amount}）");
                        pickup.amount = 1;
                    }
                }
            }

            _selectedIndex = newIndex;
        }
    }

    // ===== Auto Bind Sprite =====
    private void AutoBindSprite(ItemPickup pickup, ItemDefinition itemDef)
    {
        if (string.IsNullOrWhiteSpace(itemDef.icon))
        {
            Debug.LogWarning($"[ItemPickupEditor] Item '{itemDef.id}' has no icon path");
            return;
        }

        Sprite sprite = Resources.Load<Sprite>(itemDef.icon);

        if (sprite == null)
        {
            Debug.LogWarning($"[ItemPickupEditor] Failed to load sprite at '{itemDef.icon}' for item '{itemDef.id}'");
            return;
        }

        SpriteRenderer spriteRenderer = pickup.GetComponent<SpriteRenderer>();

        if (spriteRenderer == null)
        {
            Debug.LogWarning($"[ItemPickupEditor] No SpriteRenderer found on '{pickup.name}'. Please add SpriteRenderer component.");
            return;
        }

        Undo.RecordObject(spriteRenderer, "Auto Bind Sprite");
        spriteRenderer.sprite = sprite;

        Debug.Log($"[ItemPickupEditor] Auto-bound sprite '{sprite.name}' to '{pickup.name}'");
    }

    // ===== Draw Amount Field =====
    private void DrawAmountField(ItemPickup pickup)
    {
        EditorGUI.BeginChangeCheck();
        int newAmount = EditorGUILayout.IntField("Amount", pickup.amount);

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(pickup, "Change Amount");

            // Auto-correct: amount <= 0 -> 1
            if (newAmount <= 0)
            {
                Debug.LogWarning($"[ItemPickupEditor] Amount must be > 0, auto-corrected to 1");
                newAmount = 1;
            }

            // 自动修正：Ability/Relic 的 amount 固定为 1
            if (_catalogLoaded && !string.IsNullOrWhiteSpace(pickup.itemId))
            {
                if (_itemCatalog.TryGetItem(pickup.itemId, out ItemDefinition itemDef))
                {
                    if ((itemDef.itemType == ItemType.Ability || itemDef.itemType == ItemType.Relic) && newAmount != 1)
                    {
                        Debug.LogWarning($"[ItemPickupEditor] Ability/Relic 类型物品的 amount 必须为 1，已自动修正");
                        newAmount = 1;
                    }
                }
            }

            pickup.amount = newAmount;
        }
    }

    // ===== Draw Auto Pickup Field =====
    private void DrawAutoPickupField(ItemPickup pickup)
    {
        EditorGUI.BeginChangeCheck();
        bool newAutoPickup = EditorGUILayout.Toggle("Auto Pickup", pickup.autoPickup);

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(pickup, "Change Auto Pickup");
            pickup.autoPickup = newAutoPickup;
        }

        if (!pickup.autoPickup)
        {
            EditorGUILayout.HelpBox("Note: 0.4 does not support manual pickup. This will be treated as auto-pickup at runtime.", MessageType.Info);
        }
    }

    // ===== Draw Preview =====
    private void DrawPreview(ItemPickup pickup)
    {
        if (!_catalogLoaded || string.IsNullOrWhiteSpace(pickup.itemId))
        {
            EditorGUILayout.LabelField("No item selected", _infoStyle);
            return;
        }

        if (!_itemCatalog.TryGetItem(pickup.itemId, out ItemDefinition itemDef))
        {
            EditorGUILayout.LabelField($"Item '{pickup.itemId}' not found in ItemCatalog", _errorStyle);
            return;
        }

        // Icon preview
        if (!string.IsNullOrWhiteSpace(itemDef.icon))
        {
            Sprite sprite = Resources.Load<Sprite>(itemDef.icon);
            if (sprite != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Icon:", GUILayout.Width(100));
                GUILayout.Label(sprite.texture, GUILayout.Width(64), GUILayout.Height(64));
                GUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.LabelField($"Icon: (Failed to load '{itemDef.icon}')", _warningStyle);
            }
        }
        else
        {
            EditorGUILayout.LabelField("Icon: (None)", _infoStyle);
        }

        // Item Type
        EditorGUILayout.LabelField("Item Type:", itemDef.itemType.ToString());

        // Ability ID (for Ability type)
        if (itemDef.itemType == ItemType.Ability)
        {
            if (!string.IsNullOrWhiteSpace(itemDef.abilityId))
            {
                EditorGUILayout.LabelField("Ability ID:", itemDef.abilityId);
            }
            else
            {
                EditorGUILayout.LabelField("Ability ID: (None)", _errorStyle);
            }
        }

        // Material warning
        if (itemDef.itemType == ItemType.Material)
        {
            EditorGUILayout.HelpBox("⚠ WARNING: Material items are NOT supported for pickup in 0.4. This pickup will fail at runtime (Failed_NotSupported).", MessageType.Error);
        }
    }

    // ===== Draw Validation =====
    private void DrawValidation(ItemPickup pickup)
    {
        EditorGUILayout.LabelField("Validation", EditorStyles.boldLabel);

        bool hasErrors = false;
        bool hasWarnings = false;

        // Check itemId exists
        if (string.IsNullOrWhiteSpace(pickup.itemId))
        {
            EditorGUILayout.HelpBox("❌ ERROR: Item ID is empty", MessageType.Error);
            hasErrors = true;
        }
        else if (_catalogLoaded)
        {
            if (!_itemCatalog.TryGetItem(pickup.itemId, out ItemDefinition itemDef))
            {
                EditorGUILayout.HelpBox($"❌ ERROR: Item '{pickup.itemId}' not found in ItemCatalog", MessageType.Error);
                hasErrors = true;
            }
            else
            {
                // Check icon loadable
                if (string.IsNullOrWhiteSpace(itemDef.icon))
                {
                    EditorGUILayout.HelpBox("⚠ WARNING: Item has no icon path", MessageType.Warning);
                    hasWarnings = true;
                }
                else
                {
                    Sprite sprite = Resources.Load<Sprite>(itemDef.icon);
                    if (sprite == null)
                    {
                        EditorGUILayout.HelpBox($"⚠ WARNING: Icon resource not loadable at '{itemDef.icon}'", MessageType.Warning);
                        hasWarnings = true;
                    }
                }

                // Check SpriteRenderer.sprite
                SpriteRenderer spriteRenderer = pickup.GetComponent<SpriteRenderer>();
                if (spriteRenderer == null)
                {
                    EditorGUILayout.HelpBox("❌ ERROR: No SpriteRenderer component. Add SpriteRenderer to this GameObject.", MessageType.Error);
                    hasErrors = true;
                }
                else if (spriteRenderer.sprite == null)
                {
                    EditorGUILayout.HelpBox("❌ ERROR: SpriteRenderer.sprite is null. HUD will have icon but scene will be invisible.", MessageType.Error);
                    hasErrors = true;
                }

                // Check Material type
                if (itemDef.itemType == ItemType.Material)
                {
                    EditorGUILayout.HelpBox("❌ ERROR: Material items cannot be placed as pickups in 0.4", MessageType.Error);
                    hasErrors = true;
                }
            }
        }

        // Check Collider2D
        Collider2D collider = pickup.GetComponent<Collider2D>();
        if (collider == null)
        {
            EditorGUILayout.HelpBox("❌ ERROR: No Collider2D component. ItemPickup requires Collider2D (isTrigger=true).", MessageType.Error);
            hasErrors = true;
        }
        else if (!collider.isTrigger)
        {
            EditorGUILayout.HelpBox("⚠ WARNING: Collider2D.isTrigger should be true for pickups", MessageType.Warning);
            hasWarnings = true;
        }

        // Summary
        if (!hasErrors && !hasWarnings)
        {
            EditorGUILayout.HelpBox("✅ All validations passed", MessageType.Info);
        }
    }
}
