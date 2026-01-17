using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using CastleDB.Runtime;

/// <summary>
/// HUD 数据驱动呈现层（Phase 7）
/// 契约 [C-Runtime-6]: 订阅 Inventory/Damageable 事件，实时更新 HUD 显示
///
/// 职责：
/// - 订阅 PlayerInventory.OnAbilitySlotChanged / OnPotionCountChanged
/// - 订阅 Damageable.OnHealthChanged
/// - 订阅 PlayerRelicController.OnRelicChanged（Phase 7：遗物图标）
/// - 管理 Sprite 图标缓存（GetSprite 对外接口，复用于 ReplaceController）
/// - 更新 HUD 节点（Ability 槽图标、血瓶计数、血条、遗物图标）
///
/// 初始化流程（硬契约）：
/// 1. GameBootstrap.Awake() 调用 Initialize(items, refs, inv, dmg, relicCtrl)
/// 2. Initialize 内完成事件订阅
/// 3. Initialize 内立即执行 RefreshAll()（初始刷新）
///
/// 禁止：
/// - 轮询（必须使用事件驱动）
/// - 建立第二套 Sprite 缓存（GetSprite 必须复用内部缓存）
/// - 在 Awake/OnEnable 中查询 CastleDbService（必须等待 Initialize 注入）
/// </summary>
public class HudPresenter : MonoBehaviour
{
    // ========== 依赖注入字段（契约 [C-Runtime-6]）==========
    private ICastleDbService _items;
    private HudRefs _refs;
    private PlayerInventory _inv;
    private Damageable _dmg;
    private PlayerRelicController _relicCtrl;

    private bool _initialized = false;

    // ========== Sprite 图标缓存（契约 [C-Runtime-6]）==========
    /// <summary>
    /// Sprite 缓存：iconPath → Sprite（失败缓存 null）
    /// Resources.Load<Sprite>(iconPath) 只加载一次
    /// </summary>
    private Dictionary<string, Sprite> _spriteCache = new Dictionary<string, Sprite>();

    /// <summary>
    /// 缺失 Sprite 警告去重：已警告的 iconPath
    /// 仅 Warning 一次，避免重复日志噪音
    /// </summary>
    private HashSet<string> _missingSpriteWarned = new HashSet<string>();

    // HudRefs.relicIcon 缺失警告去重
    private bool _relicIconWarned = false;

    // ========== 初始化（契约 [C-Runtime-6]）==========
    /// <summary>
    /// 初始化 HudPresenter（由 GameBootstrap.Awake 调用）
    /// 契约：
    /// - 只允许调用一次；重复调用 Error 并忽略
    /// - 完成事件订阅并立即 RefreshAll()
    /// </summary>
    /// <param name="items">CastleDbService（查询 Item 定义）</param>
    /// <param name="refs">HudRefs（UI 节点引用）</param>
    /// <param name="inv">PlayerInventory（监听槽位/血瓶变化）</param>
    /// <param name="dmg">Damageable（监听生命值变化）</param>
    /// <param name="relicCtrl">PlayerRelicController（可选，监听遗物变更以更新图标）</param>
    public void Initialize(ICastleDbService items, HudRefs refs, PlayerInventory inv, Damageable dmg, PlayerRelicController relicCtrl)
    {
        // 重复初始化检查
        if (_initialized)
        {
            Debug.LogError("[HudPresenter] Initialize 只允许调用一次，忽略重复调用", this);
            return;
        }

        // 参数非空校验
        if (items == null)
        {
            Debug.LogError("[HudPresenter] items 参数为空，初始化失败", this);
            return;
        }

        if (refs == null)
        {
            Debug.LogError("[HudPresenter] refs 参数为空，初始化失败", this);
            return;
        }

        if (inv == null)
        {
            Debug.LogError("[HudPresenter] inv 参数为空，初始化失败", this);
            return;
        }

        if (dmg == null)
        {
            Debug.LogError("[HudPresenter] dmg 参数为空，初始化失败", this);
            return;
        }

        // 注入依赖
        _items = items;
        _refs = refs;
        _inv = inv;
        _dmg = dmg;
        _relicCtrl = relicCtrl;
        _initialized = true;

        // 订阅事件
        _inv.OnAbilitySlotChanged += OnAbilitySlotChanged;
        _inv.OnPotionCountChanged += OnPotionCountChanged;
        _dmg.OnHealthChanged += OnHealthChanged;

        if (_relicCtrl != null)
        {
            _relicCtrl.OnRelicChanged += OnRelicChanged;
        }

        Debug.Log("[HudPresenter] 初始化完成，已订阅 Inventory/Damageable 事件");

        // 立即执行初始刷新（契约 [C-Runtime-6]）
        RefreshAll();
    }

    private void OnDestroy()
    {
        // 取消订阅，避免内存泄漏
        if (_inv != null)
        {
            _inv.OnAbilitySlotChanged -= OnAbilitySlotChanged;
            _inv.OnPotionCountChanged -= OnPotionCountChanged;
        }

        if (_dmg != null)
        {
            _dmg.OnHealthChanged -= OnHealthChanged;
        }

        if (_relicCtrl != null)
        {
            _relicCtrl.OnRelicChanged -= OnRelicChanged;
        }
    }

    // ========== Sprite 图标缓存（对外接口）==========
    /// <summary>
    /// 获取 Sprite（对外接口，Phase 8 ReplaceController 复用）
    /// 契约 [C-Runtime-6]：
    /// - 必须复用内部缓存（不得建第二套缓存）
    /// - iconPath 为空/加载失败返回 null
    /// - 缺失路径仅 Warning 一次
    /// </summary>
    /// <param name="iconPath">Resources 相对路径（无扩展名），如 "Icons/Items/potion_red"</param>
    /// <returns>加载的 Sprite，失败返回 null</returns>
    public Sprite GetSprite(string iconPath)
    {
        // iconPath 为空
        if (string.IsNullOrEmpty(iconPath))
        {
            return null;
        }

        // 已缓存（包括失败缓存 null）
        if (_spriteCache.TryGetValue(iconPath, out Sprite cached))
        {
            return cached;
        }

        // 加载 Sprite
        Sprite sprite = Resources.Load<Sprite>(iconPath);

        // 缓存结果（包括 null）
        _spriteCache[iconPath] = sprite;

        // 加载失败：仅 Warning 一次
        if (sprite == null && !_missingSpriteWarned.Contains(iconPath))
        {
            _missingSpriteWarned.Add(iconPath);
            Debug.LogWarning($"[HudPresenter] 无法加载 Sprite: {iconPath}");
        }

        return sprite;
    }

    // ========== HUD 初始刷新（契约 [C-Runtime-6]）==========
    /// <summary>
    /// RefreshAll: 初始刷新所有 HUD 显示
    /// 契约 [C-Runtime-6]：
    /// 1) 遍历 0~3 槽，读取 inv.GetAbilityItemId(i)，加载 icon 并写入 abilitySlotIcons[i]
    /// 2) refs.potionCountText.text = inv.PotionCount.ToString()
    /// 3) 执行 UpdateHealth(dmg.CurrentHealth, dmg.MaxHealth)
    /// </summary>
    private void RefreshAll()
    {
        Debug.Log("[HudPresenter] 执行 RefreshAll（初始刷新）");

        // 1) 刷新 Ability 槽图标
        for (int i = 0; i < PlayerInventory.AbilitySlotCount; i++)
        {
            string itemId = _inv.GetAbilityItemId(i);
            UpdateAbilitySlot(i, itemId);
        }

        // 2) 刷新血瓶计数
        UpdatePotionCountInternal(_inv.PotionCount);

        // 3) 刷新血条
        UpdateHealthInternal(_dmg.CurrentHealth, Mathf.RoundToInt(_dmg.MaxHealth));

        // 4) 刷新遗物图标（Phase 7）
        string relicItemId = _relicCtrl != null ? _relicCtrl.EquippedRelicItemId : null;
        UpdateRelicIconInternal(relicItemId);
    }

    // ========== 事件处理（私有）==========
    private void OnAbilitySlotChanged(int slot, string oldItemId, string newItemId)
    {
        UpdateAbilitySlot(slot, newItemId);
    }

    private void OnPotionCountChanged(int newCount)
    {
        UpdatePotionCountInternal(newCount);
    }

    private void OnHealthChanged(int current, int max)
    {
        UpdateHealthInternal(current, max);
    }

    private void OnRelicChanged(string oldItemId, string newItemId)
    {
        UpdateRelicIconInternal(newItemId);
    }

    // ========== HUD 更新逻辑（私有）==========
    /// <summary>
    /// 更新 Ability 槽位图标
    /// - itemId 为空：禁用 image.enabled = false
    /// - itemId 非空：TryGetItem 获取 icon，加载 Sprite 并设置
    /// </summary>
    private void UpdateAbilitySlot(int slot, string itemId)
    {
        if (slot < 0 || slot >= _refs.abilitySlotIcons.Length)
        {
            Debug.LogError($"[HudPresenter] UpdateAbilitySlot 槽位索引非法: {slot}");
            return;
        }

        Image iconImage = _refs.abilitySlotIcons[slot];
        if (iconImage == null)
        {
            Debug.LogError($"[HudPresenter] abilitySlotIcons[{slot}] 为空，无法更新");
            return;
        }

        // 空槽：禁用 icon
        if (string.IsNullOrEmpty(itemId))
        {
            iconImage.enabled = false;
            iconImage.sprite = null;
            return;
        }

        // 非空槽：查询 Item 定义
        if (!_items.TryGetItem(itemId, out ItemDefinition def))
        {
            Debug.LogWarning($"[HudPresenter] 槽位 {slot} itemId 不存在: {itemId}，禁用图标");
            iconImage.enabled = false;
            iconImage.sprite = null;
            return;
        }

        // 加载 icon Sprite
        Sprite sprite = GetSprite(def.icon);
        if (sprite == null)
        {
            Debug.LogWarning($"[HudPresenter] 槽位 {slot} 无法加载 icon: {def.icon}，禁用图标");
            iconImage.enabled = false;
            iconImage.sprite = null;
            return;
        }

        // 设置 icon 并启用
        iconImage.sprite = sprite;
        iconImage.enabled = true;
    }

    /// <summary>
    /// 更新血瓶计数文本
    /// </summary>
    private void UpdatePotionCountInternal(int count)
    {
        if (_refs.potionCountText == null)
        {
            Debug.LogError("[HudPresenter] potionCountText 为空，无法更新");
            return;
        }

        _refs.potionCountText.text = count.ToString();
    }

    /// <summary>
    /// 更新血条填充
    /// 契约 [C-Runtime-6]：
    /// - 若 max <= 0 则填充 0
    /// - 否则 fillAmount = Mathf.Clamp01((float)current / max)
    /// - current 需 clamp 至 0..max
    /// </summary>
    private void UpdateHealthInternal(int current, int max)
    {
        if (_refs.healthFill == null)
        {
            Debug.LogError("[HudPresenter] healthFill 为空，无法更新");
            return;
        }

        // current 超出范围：Warning（一次性）
        if (current < 0 || current > max)
        {
            Debug.LogWarning($"[HudPresenter] Health 超出范围: current={current}, max={max}，将 clamp");
            current = Mathf.Clamp(current, 0, max);
        }

        // 计算 fillAmount
        float fillAmount = 0f;
        if (max > 0)
        {
            fillAmount = Mathf.Clamp01((float)current / max);
        }

        _refs.healthFill.fillAmount = fillAmount;
    }

    /// <summary>
    /// 更新遗物图标（Phase 7）
    /// - itemId 为空：隐藏图标
    /// - itemId 非空：读取 Item.icon，加载 Sprite 并显示
    /// </summary>
    private void UpdateRelicIconInternal(string itemId)
    {
        if (_refs.relicIcon == null)
        {
            if (!_relicIconWarned)
            {
                Debug.LogWarning("[HudPresenter] HudRefs.relicIcon 为空，遗物图标将不会显示（可通过 HUD Quick Config 绑定 TopLeft/RelicWidget/Icon）", this);
                _relicIconWarned = true;
            }
            return;
        }

        // 无遗物：隐藏
        if (string.IsNullOrEmpty(itemId))
        {
            _refs.relicIcon.enabled = false;
            _refs.relicIcon.sprite = null;
            return;
        }

        // 查询 Item 定义
        if (!_items.TryGetItem(itemId, out ItemDefinition def) || def == null)
        {
            Debug.LogWarning($"[HudPresenter] 遗物 itemId 不存在：{itemId}，将隐藏图标");
            _refs.relicIcon.enabled = false;
            _refs.relicIcon.sprite = null;
            return;
        }

        // 加载 icon Sprite（复用内部缓存）
        Sprite sprite = GetSprite(def.icon);
        if (sprite == null)
        {
            Debug.LogWarning($"[HudPresenter] 遗物无法加载 icon: {def.icon}，将隐藏图标");
            _refs.relicIcon.enabled = false;
            _refs.relicIcon.sprite = null;
            return;
        }

        _refs.relicIcon.sprite = sprite;
        _refs.relicIcon.enabled = true;
    }
}
