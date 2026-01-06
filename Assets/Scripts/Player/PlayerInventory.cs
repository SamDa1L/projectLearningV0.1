using System;
using System.Collections.Generic;
using UnityEngine;
using CastleDB.Runtime;

/// <summary>
/// 玩家背包系统
/// 管理 4 个能力槽 + 血瓶计数
///
/// 规范（契约 [C-Runtime-2]）：
/// - 数据：4 槽 abilityItemIds + potionCount
/// - 依赖注入：Initialize(CastleDbService, GameplayConfig) 只允许调用一次
/// - 事件驱动：OnAbilitySlotChanged/OnPotionCountChanged 先写数据再触发
/// - 拾取流程：TryPickup 返回 Success/RequireReplace/Failed_XXX
/// - 装备管理：EquipAbilityItemToSlot/ClearAbilitySlot
/// </summary>
public class PlayerInventory : MonoBehaviour
{
    // ===== 常量 =====
    /// <summary>
    /// 能力槽位数量（固定为 4）
    /// </summary>
    public const int AbilitySlotCount = 4;

    // ===== 数据 =====
    /// <summary>
    /// 能力槽位 itemId 数组（索引 0~3）
    /// null 或空字符串表示空槽
    /// </summary>
    private string[] _abilityItemIds = new string[AbilitySlotCount];

    /// <summary>
    /// 当前血瓶数量
    /// </summary>
    private int _potionCount = 0;

    // ===== 依赖注入 =====
    /// <summary>
    /// CastleDB 服务（用于查询 ItemDefinition）
    /// </summary>
    private CastleDB.Runtime.ICastleDbService _items;

    /// <summary>
    /// 游戏配置（用于获取 potionMaxCount）
    /// </summary>
    private GameplayConfig _cfg;

    /// <summary>
    /// 初始化标记（防止重复初始化）
    /// </summary>
    private bool _initialized = false;

    // ===== 事件 =====
    /// <summary>
    /// 能力槽变更事件
    /// 参数：(slotIndex, oldItemId, newItemId)
    /// 触发时机：槽位实际变化后
    /// </summary>
    public event Action<int, string, string> OnAbilitySlotChanged;

    /// <summary>
    /// 血瓶数量变更事件
    /// 参数：(newPotionCount)
    /// 触发时机：potionCount 实际变化后
    /// </summary>
    public event Action<int> OnPotionCountChanged;

    // ===== 一次性日志去重 =====
    private readonly HashSet<string> _loggedErrors = new HashSet<string>();

    // ===== 只读访问接口 =====
    /// <summary>
    /// 获取指定槽位的 itemId
    /// </summary>
    /// <param name="slotIndex">槽位索引（0~3）</param>
    /// <returns>itemId，空槽返回 null，索引非法返回 null（不输出日志）</returns>
    public string GetAbilityItemId(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= AbilitySlotCount)
            return null;
        return _abilityItemIds[slotIndex];
    }

    /// <summary>
    /// 当前血瓶数量
    /// </summary>
    public int PotionCount => _potionCount;

    /// <summary>
    /// 获取血瓶最大数量
    /// 优先取 GameplayConfig.potionMaxCount，配置错误时回退 99
    /// </summary>
    public int GetPotionMaxCount()
    {
        if (_cfg != null && _cfg.potionMaxCount > 0)
            return _cfg.potionMaxCount;

        // 配置错误回退
        if (_cfg != null && _cfg.potionMaxCount <= 0)
        {
            string key = "PotionMaxCount_ConfigError";
            if (!_loggedErrors.Contains(key))
            {
                Debug.LogWarning($"[PlayerInventory] GameplayConfig.potionMaxCount <= 0，回退默认值 99");
                _loggedErrors.Add(key);
            }
        }

        return 99; // 默认值
    }

    // ===== 初始化 =====
    /// <summary>
    /// 初始化背包系统
    /// 只允许调用一次，重复调用 Error 并忽略
    /// </summary>
    /// <param name="items">CastleDB 服务</param>
    /// <param name="cfg">游戏配置（允许为 null）</param>
    public void Initialize(CastleDB.Runtime.ICastleDbService items, GameplayConfig cfg)
    {
        if (_initialized)
        {
            Debug.LogError($"[PlayerInventory] Initialize 只允许调用一次，重复调用已忽略");
            return;
        }

        _items = items;
        _cfg = cfg;
        _initialized = true;

        Debug.Log($"[PlayerInventory] 初始化完成 - PotionMaxCount={GetPotionMaxCount()}");
    }

    // ===== 拾取流程 =====
    /// <summary>
    /// 尝试拾取物品
    /// </summary>
    /// <param name="req">拾取请求</param>
    /// <param name="ctx">待替换上下文（仅 RequireReplace 时有效）</param>
    /// <returns>拾取结果</returns>
    public PickupResult TryPickup(PickupRequest req, out PendingReplaceContext ctx)
    {
        ctx = default;

        // 未初始化检查
        if (!_initialized)
        {
            Debug.LogError($"[PlayerInventory] 未初始化，禁止调用 TryPickup");
            return PickupResult.Failed_NotSupported;
        }

        // 参数校验：itemId
        if (string.IsNullOrWhiteSpace(req.itemId))
        {
            string key = $"InvalidItemId_{req.itemId}";
            if (!_loggedErrors.Contains(key))
            {
                Debug.LogWarning($"[PlayerInventory] itemId 非法: '{req.itemId}'");
                _loggedErrors.Add(key);
            }
            return PickupResult.Failed_InvalidItemId;
        }

        // 参数校验：amount
        if (req.amount <= 0)
        {
            Debug.LogError($"[PlayerInventory] amount 非法: {req.amount}，itemId={req.itemId}");
            return PickupResult.Failed_NotSupported;
        }

        // 查询 ItemDefinition
        if (!_items.TryGetItem(req.itemId, out ItemDefinition def))
        {
            string key = $"InvalidItemId_{req.itemId}";
            if (!_loggedErrors.Contains(key))
            {
                Debug.LogWarning($"[PlayerInventory] itemId 不存在: '{req.itemId}'");
                _loggedErrors.Add(key);
            }
            return PickupResult.Failed_InvalidItemId;
        }

        // 根据 itemType 分流处理
        switch (def.itemType)
        {
            case ItemType.Ability:
                return TryPickupAbility(req, def, out ctx);

            case ItemType.Consumable:
                return TryPickupConsumable(req, def);

            case ItemType.Material:
                // 0.4 不支持 Material 拾取
                return PickupResult.Failed_NotSupported;

            default:
                Debug.LogError($"[PlayerInventory] 未知 itemType: {def.itemType}, itemId={req.itemId}");
                return PickupResult.Failed_NotSupported;
        }
    }

    /// <summary>
    /// 拾取 Ability 类物品
    /// </summary>
    private PickupResult TryPickupAbility(PickupRequest req, ItemDefinition def, out PendingReplaceContext ctx)
    {
        ctx = default;

        // 1) amount 必须为 1
        if (req.amount != 1)
        {
            Debug.LogError($"[PlayerInventory] Ability 的 amount 必须为 1，实际 {req.amount}，itemId={req.itemId}");
            return PickupResult.Failed_NotSupported;
        }

        // 2) abilityId 必须非空
        if (string.IsNullOrWhiteSpace(def.abilityId))
        {
            Debug.LogError($"[PlayerInventory] Ability 类 itemId={req.itemId} 的 abilityId 为空");
            return PickupResult.Failed_NotSupported;
        }

        // 3) 重复检查优先：若任意槽位已有同一 abilityId
        for (int i = 0; i < AbilitySlotCount; i++)
        {
            string slotItemId = _abilityItemIds[i];
            if (string.IsNullOrEmpty(slotItemId))
                continue;

            if (_items.TryGetItem(slotItemId, out ItemDefinition slotDef))
            {
                if (slotDef.abilityId == def.abilityId)
                {
                    string key = $"AlreadyEquipped_{req.itemId}";
                    if (!_loggedErrors.Contains(key))
                    {
                        Debug.LogFormat(LogType.Log, LogOption.None, null,
                            $"[PlayerInventory] Ability '{def.abilityId}' 已装备在槽位 {i}，拒绝重复拾取 itemId={req.itemId}");
                        _loggedErrors.Add(key);
                    }
                    return PickupResult.Failed_AlreadyEquipped;
                }
            }
        }

        // 4) 若存在空槽：找到首个空槽并写入
        for (int i = 0; i < AbilitySlotCount; i++)
        {
            if (string.IsNullOrEmpty(_abilityItemIds[i]))
            {
                string oldItemId = _abilityItemIds[i];
                _abilityItemIds[i] = req.itemId;
                OnAbilitySlotChanged?.Invoke(i, oldItemId, req.itemId);
                return PickupResult.Success;
            }
        }

        // 5) 槽满且 sourcePickup 为空：Error 并返回 Failed_NotSupported
        if (req.sourcePickup == null)
        {
            string key = $"RequireReplace_NoSource_{req.itemId}";
            if (!_loggedErrors.Contains(key))
            {
                Debug.LogError($"[PlayerInventory] 槽位已满但 sourcePickup 为空，无法 RequireReplace，itemId={req.itemId}");
                _loggedErrors.Add(key);
            }
            return PickupResult.Failed_NotSupported;
        }

        // 6) 槽满：返回 RequireReplace
        ctx = new PendingReplaceContext(req.itemId, req.amount, req.sourcePickup);
        return PickupResult.RequireReplace;
    }

    /// <summary>
    /// 拾取 Consumable 类物品
    /// </summary>
    private PickupResult TryPickupConsumable(PickupRequest req, ItemDefinition def)
    {
        int maxCount = GetPotionMaxCount();

        // 若已达上限：Failed_NotSupported
        if (_potionCount >= maxCount)
        {
            return PickupResult.Failed_NotSupported;
        }

        // 累加并触发事件
        int newCount = Mathf.Min(_potionCount + req.amount, maxCount);
        if (newCount != _potionCount)
        {
            _potionCount = newCount;
            OnPotionCountChanged?.Invoke(_potionCount);
        }

        return PickupResult.Success;
    }

    // ===== 装备管理 =====
    /// <summary>
    /// 装备 Ability 到指定槽位
    /// </summary>
    /// <param name="slotIndex">槽位索引（0~3）</param>
    /// <param name="itemId">要装备的 itemId</param>
    /// <returns>true=槽位发生变化且已触发事件；false=未变化（参数非法/物品非法/重复写同值）</returns>
    public bool EquipAbilityItemToSlot(int slotIndex, string itemId)
    {
        if (!_initialized)
        {
            Debug.LogError($"[PlayerInventory] 未初始化，禁止调用 EquipAbilityItemToSlot");
            return false;
        }

        // 槽位索引校验
        if (slotIndex < 0 || slotIndex >= AbilitySlotCount)
        {
            string key = $"EquipSlot_InvalidIndex_{slotIndex}";
            if (!_loggedErrors.Contains(key))
            {
                Debug.LogError($"[PlayerInventory] EquipAbilityItemToSlot 槽位索引非法: {slotIndex}");
                _loggedErrors.Add(key);
            }
            return false;
        }

        // 空字符串视为 null
        if (string.IsNullOrWhiteSpace(itemId))
            itemId = null;

        // 查询 ItemDefinition
        if (itemId != null)
        {
            if (!_items.TryGetItem(itemId, out ItemDefinition def))
            {
                Debug.LogError($"[PlayerInventory] EquipAbilityItemToSlot itemId 不存在: '{itemId}'");
                return false;
            }

            // itemType 必须为 Ability
            if (def.itemType != ItemType.Ability)
            {
                Debug.LogError($"[PlayerInventory] EquipAbilityItemToSlot itemId={itemId} 的 itemType 不是 Ability: {def.itemType}");
                return false;
            }

            // abilityId 必须非空
            if (string.IsNullOrWhiteSpace(def.abilityId))
            {
                Debug.LogError($"[PlayerInventory] EquipAbilityItemToSlot itemId={itemId} 的 abilityId 为空");
                return false;
            }

            // 重复检查：若其他槽位已有同一 abilityId
            for (int i = 0; i < AbilitySlotCount; i++)
            {
                if (i == slotIndex)
                    continue; // 跳过当前槽位

                string otherItemId = _abilityItemIds[i];
                if (string.IsNullOrEmpty(otherItemId))
                    continue;

                if (_items.TryGetItem(otherItemId, out ItemDefinition otherDef))
                {
                    if (otherDef.abilityId == def.abilityId)
                    {
                        string key = $"EquipSlot_DuplicateAbility_{def.abilityId}";
                        if (!_loggedErrors.Contains(key))
                        {
                            Debug.LogFormat(LogType.Warning, LogOption.None, null,
                                $"[PlayerInventory] Ability '{def.abilityId}' 已装备在槽位 {i}，拒绝装备到槽位 {slotIndex}");
                            _loggedErrors.Add(key);
                        }
                        return false;
                    }
                }
            }
        }

        // 检查是否重复写同一个 itemId
        string oldItemId = _abilityItemIds[slotIndex];
        if (oldItemId == itemId)
        {
            return false; // 未变化
        }

        // 写入并触发事件
        _abilityItemIds[slotIndex] = itemId;
        OnAbilitySlotChanged?.Invoke(slotIndex, oldItemId, itemId);
        return true;
    }

    /// <summary>
    /// 清空指定槽位
    /// </summary>
    /// <param name="slotIndex">槽位索引（0~3）</param>
    /// <returns>true=从非空变为空并触发事件；false=索引非法或本来为空</returns>
    public bool ClearAbilitySlot(int slotIndex)
    {
        if (!_initialized)
        {
            Debug.LogError($"[PlayerInventory] 未初始化，禁止调用 ClearAbilitySlot");
            return false;
        }

        // 槽位索引校验
        if (slotIndex < 0 || slotIndex >= AbilitySlotCount)
        {
            string key = $"ClearSlot_InvalidIndex_{slotIndex}";
            if (!_loggedErrors.Contains(key))
            {
                Debug.LogError($"[PlayerInventory] ClearAbilitySlot 槽位索引非法: {slotIndex}");
                _loggedErrors.Add(key);
            }
            return false;
        }

        // 检查是否本来为空
        string oldItemId = _abilityItemIds[slotIndex];
        if (string.IsNullOrEmpty(oldItemId))
        {
            return false; // 本来为空
        }

        // 清空并触发事件
        _abilityItemIds[slotIndex] = null;
        OnAbilitySlotChanged?.Invoke(slotIndex, oldItemId, null);
        return true;
    }

    // ===== 药水使用 (0.45) =====
    public bool TryGetConsumableHealAmount(string itemId, out int healAmount)
    {
        healAmount = 0;

        if (!_initialized)
            return false;

        if (string.IsNullOrWhiteSpace(itemId))
            return false;

        if (!_items.TryGetItem(itemId, out ItemDefinition def))
            return false;

        if (def.itemType != ItemType.Consumable)
            return false;

        healAmount = def.consumeEffect.heal;
        return true;
    }

    /// <summary>
    /// 使用药水（拾取 Consumable 后自动触发回血）
    /// 0.45 版本：拾取药水时立即回血，不消耗计数
    /// </summary>
    /// <param name="itemId">药水 itemId</param>
    /// <param name="damageable">目标 Damageable 组件</param>
    /// <param name="actualHeal">实际回血量</param>
    /// <returns>true=成功回血；false=失败（itemId 非法/非 Consumable/目标无效）</returns>
    public bool UsePotion(string itemId, Damageable damageable, out int actualHeal)
    {
        actualHeal = 0;

        if (!_initialized)
        {
            Debug.LogError($"[PlayerInventory] 未初始化，禁止调用 UsePotion");
            return false;
        }

        // 参数校验
        if (string.IsNullOrWhiteSpace(itemId))
        {
            Debug.LogError($"[PlayerInventory] UsePotion itemId 非法");
            return false;
        }

        if (damageable == null)
        {
            Debug.LogError($"[PlayerInventory] UsePotion damageable 为空");
            return false;
        }

        // 查询 ItemDefinition
        if (!_items.TryGetItem(itemId, out ItemDefinition def))
        {
            Debug.LogWarning($"[PlayerInventory] UsePotion itemId 不存在: '{itemId}'");
            return false;
        }

        // 0.45 修正：itemType 不是 Consumable 时静默返回（避免拾取 Ability 时污染日志）
        if (def.itemType != ItemType.Consumable)
        {
            return false; // 静默返回，不输出日志
        }

        int healAmount = def.consumeEffect.heal;

        if (healAmount <= 0)
        {
            Debug.LogWarning($"[PlayerInventory] UsePotion itemId={itemId} heal 值 <= 0: {healAmount}");
            return false;
        }

        // 调用 Damageable.TryHeal()，以实际回血量为准
        actualHeal = damageable.TryHeal(healAmount);
        if (actualHeal > 0)
        {
            Debug.Log($"[PlayerInventory] 使用药水 {itemId} 成功，回血 {actualHeal}");
            return true;
        }

        return false;
    }

    public bool UsePotion(string itemId, Damageable damageable)
    {
        return UsePotion(itemId, damageable, out _);
    }
}
