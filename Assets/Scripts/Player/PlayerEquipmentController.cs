using System.Collections.Generic;
using UnityEngine;
using CastleDB.Runtime;

/// <summary>
/// 玩家装备控制器（Phase 6 完整实现）
///
/// 核心职责（契约 [C-Runtime-7]）：
/// - 唯一责任主体：将 Inventory 槽位变更映射为 AbilitySystem Enable/Disable
/// - 订阅 PlayerInventory.OnAbilitySlotChanged 事件
/// - 通过 CastleDbService 解析 itemId → abilityId 映射
/// - 执行顺序：先 Disable 旧能力，再 Enable 新能力（保留 pendingQueue 顺序）
/// - 初始同步：Start 时执行一次 SyncAllSlotsToAbilities
///
/// 依赖注入：
/// - CastleDbService（查询 Item 定义）
/// - AbilitySystem（控制能力启停）
/// - PlayerInventory（监听槽位变化）
///
/// 初始化规则（硬契约）：
/// - Initialize 只允许调用一次
/// - SyncAllSlotsToAbilities 仅允许在 Start 或 Bootstrap 回调后执行
/// </summary>
public class PlayerEquipmentController : MonoBehaviour
{
    // ===== 依赖（由 GameBootstrap 注入）=====
    private ICastleDbService _items;
    private AbilitySystem _abilitySystem;
    private PlayerInventory _inventory;

    // ===== 状态 =====
    private bool _initialized = false;
    private string[] _slotAbilityIds = new string[4]; // 记录每槽当前启用的 abilityId

    // ===== 一次性日志去重 =====
    private static readonly HashSet<string> _loggedWarnings = new HashSet<string>();

    // ===== 初始化 =====

    /// <summary>
    /// 初始化装备控制器（由 GameBootstrap 调用）
    /// </summary>
    public void Initialize(ICastleDbService items, AbilitySystem abilitySystem, PlayerInventory inventory)
    {
        // 只允许初始化一次
        if (_initialized)
        {
            Debug.LogError("[PlayerEquipmentController] Initialize 已被调用，不允许重复初始化", this);
            return;
        }

        if (items == null || abilitySystem == null || inventory == null)
        {
            Debug.LogError("[PlayerEquipmentController] Initialize 参数不能为空", this);
            return;
        }

        _items = items;
        _abilitySystem = abilitySystem;
        _inventory = inventory;
        _initialized = true;

        // 订阅槽位变化事件
        _inventory.OnAbilitySlotChanged += OnSlotChanged;

        Debug.Log("[PlayerEquipmentController] 初始化完成，已订阅槽位变化事件");
    }

    private void OnDestroy()
    {
        // 取消订阅
        if (_inventory != null)
        {
            _inventory.OnAbilitySlotChanged -= OnSlotChanged;
        }
    }

    // ===== 槽位映射逻辑 =====

    /// <summary>
    /// 槽位变化事件处理（核心映射逻辑）
    /// </summary>
    private void OnSlotChanged(int slot, string oldItemId, string newItemId)
    {
        if (!_initialized)
        {
            Debug.LogError("[PlayerEquipmentController] 未初始化，无法处理槽位变化", this);
            return;
        }

        // 解析旧 itemId → abilityId
        string oldAbilityId = GetAbilityIdFromItem(oldItemId);

        // 解析新 itemId → abilityId
        string newAbilityId = GetAbilityIdFromItem(newItemId);

        // 更新本地记录
        _slotAbilityIds[slot] = newAbilityId ?? "";

        // 执行顺序（硬契约）：先 Disable 旧能力，再 Enable 新能力
        if (!string.IsNullOrEmpty(oldAbilityId))
        {
            _abilitySystem.SetAbilityEnabled(oldAbilityId, false);
            Debug.Log($"[PlayerEquipmentController] 槽位 {slot}: 禁用旧能力 '{oldAbilityId}'");
        }

        if (!string.IsNullOrEmpty(newAbilityId))
        {
            _abilitySystem.SetAbilityEnabled(newAbilityId, true);
            Debug.Log($"[PlayerEquipmentController] 槽位 {slot}: 启用新能力 '{newAbilityId}'");
        }
    }

    /// <summary>
    /// 通过 itemId 查询对应的 abilityId
    /// </summary>
    private string GetAbilityIdFromItem(string itemId)
    {
        if (string.IsNullOrEmpty(itemId))
        {
            return null;
        }

        // TryGetItem 失败策略（硬契约）：
        // 若查询失败视为该槽 ability 为空，可选 Warning（一次性），不修改 Inventory 数据
        if (!_items.TryGetItem(itemId, out ItemDefinition def))
        {
            string key = $"EquipmentController_InvalidItemId_{itemId}";
            if (!_loggedWarnings.Contains(key))
            {
                Debug.LogWarning($"[PlayerEquipmentController] 无法解析 itemId '{itemId}'，视为槽位无能力", this);
                _loggedWarnings.Add(key);
            }
            return null;
        }

        // itemType 必须是 Ability
        if (def.itemType != ItemType.Ability)
        {
            string key = $"EquipmentController_NotAbility_{itemId}";
            if (!_loggedWarnings.Contains(key))
            {
                Debug.LogWarning($"[PlayerEquipmentController] Item '{itemId}' 类型为 {def.itemType}，不是 Ability，视为槽位无能力", this);
                _loggedWarnings.Add(key);
            }
            return null;
        }

        // abilityId 必须非空
        if (string.IsNullOrEmpty(def.abilityId))
        {
            string key = $"EquipmentController_EmptyAbilityId_{itemId}";
            if (!_loggedWarnings.Contains(key))
            {
                Debug.LogWarning($"[PlayerEquipmentController] Item '{itemId}' 的 abilityId 为空，视为槽位无能力", this);
                _loggedWarnings.Add(key);
            }
            return null;
        }

        return def.abilityId;
    }

    // ===== 初始同步 =====

    /// <summary>
    /// 同步所有槽位到 AbilitySystem（初始启用）
    /// 仅允许在 Start 或 Bootstrap 回调后执行
    /// </summary>
    public void SyncAllSlotsToAbilities()
    {
        if (!_initialized)
        {
            Debug.LogError("[PlayerEquipmentController] 未初始化，无法执行 SyncAllSlotsToAbilities", this);
            return;
        }

        Debug.Log("[PlayerEquipmentController] 开始初始同步（SyncAllSlotsToAbilities）");

        // 遍历所有槽位
        for (int slot = 0; slot < PlayerInventory.AbilitySlotCount; slot++)
        {
            string itemId = _inventory.GetAbilityItemId(slot);
            string abilityId = GetAbilityIdFromItem(itemId);

            // 记录当前槽位的 abilityId
            _slotAbilityIds[slot] = abilityId ?? "";

            // 若 abilityId 非空，启用该能力
            if (!string.IsNullOrEmpty(abilityId))
            {
                _abilitySystem.SetAbilityEnabled(abilityId, true);
                Debug.Log($"[PlayerEquipmentController] 初始同步：槽位 {slot} 启用能力 '{abilityId}'");
            }
        }

        Debug.Log("[PlayerEquipmentController] 初始同步完成");
    }
}
