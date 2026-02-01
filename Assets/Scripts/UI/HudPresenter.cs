using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
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
    private AbilitySystem _abilitySystem;

    // Phase 8: DebugOverlay (optional)
    private StatusEffectController _statusCtrl;
    private StatModifierLayer _stats;
    private readonly StringBuilder _debugSb = new StringBuilder(512);
    private readonly StringBuilder _statusSb = new StringBuilder(128);
    private float _nextDebugOverlayUpdateTime = 0f;

    // Phase 8: cooldown UI throttling (avoid per-frame string churn)
    private readonly int[] _cooldownLastSeconds = new int[PlayerInventory.AbilitySlotCount];
    private readonly bool[] _cooldownWasVisible = new bool[PlayerInventory.AbilitySlotCount];

    private bool _initialized = false;

    // ========== Sprite 图标缓存（契约 [C-Runtime-6]）==========
    /// <summary>
    /// Sprite 缓存：iconPath → Sprite（失败缓存 null）
    /// 通过 IGameAssetProvider.Load<Sprite>(iconPath) 只加载一次
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
    public void Initialize(ICastleDbService items, HudRefs refs, PlayerInventory inv, Damageable dmg, PlayerRelicController relicCtrl, AbilitySystem abilitySystem = null)
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
        _abilitySystem = abilitySystem;

        // Optional: resolve extra runtime components for Phase 8 debug overlay (no Find/Tag/singleton).
        _statusCtrl = _dmg != null ? _dmg.GetComponent<StatusEffectController>() : null;
        if (_statusCtrl == null && _dmg != null)
        {
            _statusCtrl = _dmg.GetComponentInParent<StatusEffectController>();
        }

        _stats = _dmg != null ? _dmg.GetComponent<StatModifierLayer>() : null;
        if (_stats == null && _dmg != null)
        {
            _stats = _dmg.GetComponentInParent<StatModifierLayer>();
        }

        for (int i = 0; i < _cooldownLastSeconds.Length; i++)
        {
            _cooldownLastSeconds[i] = -1;
            _cooldownWasVisible[i] = false;
        }
        _initialized = true;

        // 订阅事件
        _inv.OnAbilitySlotChanged += OnAbilitySlotChanged;
        _inv.OnPotionCountChanged += OnPotionCountChanged;
        _dmg.OnHealthChanged += OnHealthChanged;

        if (_relicCtrl != null)
        {
            _relicCtrl.OnRelicChanged += OnRelicChanged;
        }


        if (_statusCtrl != null)
        {
            _statusCtrl.OnStatusApplied += OnStatusApplied;
            _statusCtrl.OnStatusRemoved += OnStatusRemoved;
            _statusCtrl.OnStatusExpired += OnStatusExpired;
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


        if (_statusCtrl != null)
        {
            _statusCtrl.OnStatusApplied -= OnStatusApplied;
            _statusCtrl.OnStatusRemoved -= OnStatusRemoved;
            _statusCtrl.OnStatusExpired -= OnStatusExpired;
        }
    }

    private void Update()
    {
        if (!_initialized)
        {
            return;
        }

        if (_abilitySystem != null)
        {
            UpdateAbilityCooldownUiAll();
        }

        UpdateDebugOverlay();
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
        Sprite sprite = ResourcesGameAssetProvider.Shared.Load<Sprite>(iconPath);

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
        UpdateAbilityCooldownUiAll();

        UpdatePotionCountInternal(_inv.PotionCount);

        // 3) 刷新血条
        UpdateHealthInternal(_dmg.CurrentHealth, Mathf.RoundToInt(_dmg.MaxHealth));

        // 4) 刷新遗物图标（Phase 7）
        string relicItemId = _relicCtrl != null ? _relicCtrl.EquippedRelicItemId : null;
        UpdateRelicIconInternal(relicItemId);

        UpdateStatusTextInternal();
    }

    // ========== 事件处理（私有）==========
    private void OnAbilitySlotChanged(int slot, string oldItemId, string newItemId)
    {
        UpdateAbilitySlot(slot, newItemId);
        UpdateAbilityCooldownUiSlot(slot);
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

    private void OnStatusApplied(string statusId, int stacks)
    {
        UpdateStatusTextInternal();
    }

    private void OnStatusRemoved(string statusId)
    {
        UpdateStatusTextInternal();
    }

    private void OnStatusExpired(string statusId)
    {
        UpdateStatusTextInternal();
    }

    private void UpdateStatusTextInternal()
    {
        if (_refs == null || _refs.statusText == null || _statusCtrl == null)
        {
            return;
        }

        var ids = _statusCtrl.ActiveStatusIds;
        if (ids == null || ids.Count == 0)
        {
            _refs.statusText.text = string.Empty;
            _refs.statusText.enabled = false;
            return;
        }

        _statusSb.Clear();
        for (int i = 0; i < ids.Count; i++)
        {
            if (i > 0)
            {
                _statusSb.Append("  ");
            }

            string id = ids[i];
            _statusSb.Append(id);
            int s = _statusCtrl.GetStacks(id);
            if (s > 1)
            {
                _statusSb.Append(" x");
                _statusSb.Append(s);
            }
        }

        _refs.statusText.text = _statusSb.ToString();
        _refs.statusText.enabled = true;
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
    private void UpdateAbilityCooldownUiAll()
    {
        if (!_initialized || _abilitySystem == null || _refs == null)
        {
            return;
        }

        bool hasAnyWidget =
            (_refs.abilitySlotCooldownFills != null && _refs.abilitySlotCooldownFills.Length == PlayerInventory.AbilitySlotCount) ||
            (_refs.abilitySlotCooldownTexts != null && _refs.abilitySlotCooldownTexts.Length == PlayerInventory.AbilitySlotCount);

        if (!hasAnyWidget)
        {
            return;
        }

        for (int i = 0; i < PlayerInventory.AbilitySlotCount; i++)
        {
            UpdateAbilityCooldownUiSlot(i);
        }
    }

    private void UpdateAbilityCooldownUiSlot(int slot)
    {
        if (!_initialized || _abilitySystem == null || _refs == null || _inv == null)
        {
            return;
        }

        if (slot < 0 || slot >= PlayerInventory.AbilitySlotCount)
        {
            return;
        }

        Image fill = (_refs.abilitySlotCooldownFills != null && _refs.abilitySlotCooldownFills.Length == PlayerInventory.AbilitySlotCount)
            ? _refs.abilitySlotCooldownFills[slot]
            : null;

        TMP_Text text = (_refs.abilitySlotCooldownTexts != null && _refs.abilitySlotCooldownTexts.Length == PlayerInventory.AbilitySlotCount)
            ? _refs.abilitySlotCooldownTexts[slot]
            : null;

        // Prefab 未提供冷却 UI（Phase 8 可选）：直接跳过
        if (fill == null && text == null)
        {
            return;
        }

        // 无能力：隐藏
        if (!_inv.TryGetAbilityIdInSlot(slot, out string abilityId) || string.IsNullOrWhiteSpace(abilityId))
        {
            SetCooldownUiVisible(slot, visible: false);
            return;
        }

        if (!_abilitySystem.TryGetAbility(abilityId, out IPlayerAbility ability) || ability == null)
        {
            SetCooldownUiVisible(slot, visible: false);
            return;
        }

        float duration = Mathf.Max(0f, ability.CooldownSeconds);
        float remaining = Mathf.Max(0f, ability.CooldownRemaining);

        // 无冷却/已就绪：隐藏
        if (duration <= 0f || remaining <= 0f)
        {
            SetCooldownUiVisible(slot, visible: false);
            return;
        }

        // Fill: 1 -> 刚释放，0 -> 就绪
        if (fill != null)
        {
            fill.enabled = true;
            fill.fillAmount = Mathf.Clamp01(remaining / duration);
        }

        if (text != null)
        {
            int seconds = Mathf.CeilToInt(remaining);
            if (!_cooldownWasVisible[slot] || seconds != _cooldownLastSeconds[slot])
            {
                text.text = seconds.ToString();
                _cooldownLastSeconds[slot] = seconds;
            }
            text.enabled = true;
        }

        _cooldownWasVisible[slot] = true;
    }

    private void SetCooldownUiVisible(int slot, bool visible)
    {
        if (_refs == null || slot < 0 || slot >= PlayerInventory.AbilitySlotCount)
        {
            return;
        }

        Image fill = (_refs.abilitySlotCooldownFills != null && _refs.abilitySlotCooldownFills.Length == PlayerInventory.AbilitySlotCount)
            ? _refs.abilitySlotCooldownFills[slot]
            : null;

        TMP_Text text = (_refs.abilitySlotCooldownTexts != null && _refs.abilitySlotCooldownTexts.Length == PlayerInventory.AbilitySlotCount)
            ? _refs.abilitySlotCooldownTexts[slot]
            : null;

        if (fill != null)
        {
            fill.enabled = visible;
            if (!visible)
            {
                fill.fillAmount = 0f;
            }
        }

        if (text != null)
        {
            text.enabled = visible;
            if (!visible)
            {
                text.text = string.Empty;
            }
        }

        if (!visible)
        {
            _cooldownWasVisible[slot] = false;
            _cooldownLastSeconds[slot] = -1;
        }
    }

    private void UpdateDebugOverlay()
    {
        if (_refs == null || _refs.debugOverlayText == null)
        {
            return;
        }

        // Only update when the widget is enabled & active (keeps this cheap in production).
        if (!_refs.debugOverlayText.isActiveAndEnabled)
        {
            return;
        }

        float now = Time.time;
        if (now < _nextDebugOverlayUpdateTime)
        {
            return;
        }
        _nextDebugOverlayUpdateTime = now + 0.25f;

        _debugSb.Clear();
        _debugSb.AppendLine("HUD DEBUG");

        if (_dmg != null)
        {
            _debugSb.Append("HP: ");
            _debugSb.Append(_dmg.CurrentHealth);
            _debugSb.Append('/');
            _debugSb.Append(Mathf.RoundToInt(_dmg.MaxHealth));
            _debugSb.AppendLine();
        }

        if (_stats != null)
        {
            _debugSb.Append("MoveSpeedMult: ");
            _debugSb.Append(_stats.MoveSpeedMultiplier.ToString("0.##"));
            _debugSb.Append("  AttackMult: ");
            _debugSb.Append(_stats.AttackMultiplier.ToString("0.##"));
            _debugSb.AppendLine();
        }

        if (_relicCtrl != null)
        {
            _debugSb.Append("Relic: ");
            _debugSb.Append(string.IsNullOrWhiteSpace(_relicCtrl.EquippedRelicItemId) ? "<none>" : _relicCtrl.EquippedRelicItemId);
            _debugSb.Append("  Shield: ");
            _debugSb.Append(_relicCtrl.ShieldHp);
            _debugSb.Append('/');
            _debugSb.Append(_relicCtrl.ShieldMaxHp);
            _debugSb.AppendLine();
        }

        if (_inv != null)
        {
            _debugSb.AppendLine("Slots:");
            for (int i = 0; i < PlayerInventory.AbilitySlotCount; i++)
            {
                _debugSb.Append(i);
                _debugSb.Append(": ");
                if (_inv.TryGetAbilityIdInSlot(i, out string abilityId) && !string.IsNullOrWhiteSpace(abilityId))
                {
                    _debugSb.Append(abilityId);
                }
                else
                {
                    _debugSb.Append("<empty>");
                }
                _debugSb.AppendLine();
            }
        }

        _debugSb.AppendLine("Abilities:");
        if (_abilitySystem != null)
        {
            foreach (var ability in _abilitySystem.EnumerateAllAbilities())
            {
                if (ability == null)
                {
                    continue;
                }

                _debugSb.Append(ability.AbilityId);
                _debugSb.Append(" enabled=");
                _debugSb.Append(ability.Enabled ? "1" : "0");

                float duration = Mathf.Max(0f, ability.CooldownSeconds);
                if (duration > 0f)
                {
                    float remaining = Mathf.Max(0f, ability.CooldownRemaining);
                    _debugSb.Append(" cd=");
                    _debugSb.Append(remaining.ToString("0.0"));
                    _debugSb.Append('/');
                    _debugSb.Append(duration.ToString("0.0"));
                }

                _debugSb.AppendLine();
            }
        }
        else
        {
            _debugSb.AppendLine("<no AbilitySystem>");
        }

        if (_statusCtrl != null)
        {
            _debugSb.AppendLine("Statuses:");
            var ids = _statusCtrl.ActiveStatusIds;
            if (ids != null && ids.Count > 0)
            {
                for (int i = 0; i < ids.Count; i++)
                {
                    string id = ids[i];
                    _debugSb.Append(id);
                    int stacks = _statusCtrl.GetStacks(id);
                    if (stacks > 1)
                    {
                        _debugSb.Append(" x");
                        _debugSb.Append(stacks);
                    }

                    float remaining = _statusCtrl.GetRemainingSeconds(id);
                    if (!float.IsPositiveInfinity(remaining))
                    {
                        _debugSb.Append(" (");
                        _debugSb.Append(remaining.ToString("0.0"));
                        _debugSb.Append("s)");
                    }
                    _debugSb.AppendLine();
                }
            }
            else
            {
                _debugSb.AppendLine("<none>");
            }
        }

        _refs.debugOverlayText.text = _debugSb.ToString();
    }

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
