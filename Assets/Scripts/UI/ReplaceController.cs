using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.InputSystem;
using CastleDB.Runtime;

/// <summary>
/// 替换控制器（Phase 8 完整实现）
/// 契约 [C-Runtime-5]: 状态机 + 输入映射 + UI 面板控制
///
/// 职责：
/// - 管理槽满拾取的替换流程（Idle → Selecting → Confirm/Cancel → Idle）
/// - 切换输入模式（Gameplay ↔ UI）
/// - 渲染替换面板（当前槽位 + 待入槽物品）
/// - 处理用户选择（1~4 键选槽，ESC 取消）
///
/// 依赖：
/// - CastleDbService：查询 ItemDefinition
/// - HudRefs：获取 AbilityReplacePanel 引用
/// - PlayerContext：访问 Inventory/PlayerInput
/// - HudPresenter：复用 Sprite 缓存（GetSprite）
/// </summary>
public class ReplaceController : MonoBehaviour
{
    // ===== 状态 =====
    private enum State
    {
        Idle,
        Selecting
    }

    private State _state = State.Idle;

    /// <summary>
    /// 是否正在选择替换槽位（外部只读）
    /// </summary>
    public bool IsSelecting => _state == State.Selecting;

    // ===== 依赖（由 GameBootstrap 注入） =====
    private ICastleDbService _items;
    private HudRefs _refs;
    private PlayerContext _ctx;
    private HudPresenter _hud;
    private GameObject _panelRoot;
    private PlayerInput _playerInput;

    // ===== UI 节点缓存（Initialize 时查找） =====
    private Image _pendingIcon;
    private TMP_Text _pendingNameText;
    private Image[] _slotIcons = new Image[4];
    private GameObject[] _slotHighlights = new GameObject[4];

    // ===== 输入相关 =====
    private string _prevActionMap; // 记录进入 Selecting 前的 ActionMap

    // ===== 待替换上下文 =====
    private PendingReplaceContext _pendingContext;

    // ===== 初始化标记 =====
    private bool _initialized = false;

    /// <summary>
    /// 是否输出 Unity 控制台日志（Debug.Log/LogWarning/LogError）。
    /// - 默认开启：保留现有运行时行为
    /// - 自动化测试建议关闭：避免用“红色错误日志”来验收失败路径
    /// </summary>
    public bool EnableUnityConsoleLogging { get; set; } = true;

    // ===== 一次性日志去重 =====
    private static readonly HashSet<string> _loggedWarnings = new HashSet<string>();
    private static readonly HashSet<string> _loggedErrors = new HashSet<string>();

    // ===== 生命周期 =====

    private void Update()
    {
        if (_state != State.Selecting) return;

        // 监听 1~4 键选择槽位
        if (Keyboard.current != null)
        {
            if (Keyboard.current.digit1Key.wasPressedThisFrame)
            {
                OnSlotSelected(0);
            }
            else if (Keyboard.current.digit2Key.wasPressedThisFrame)
            {
                OnSlotSelected(1);
            }
            else if (Keyboard.current.digit3Key.wasPressedThisFrame)
            {
                OnSlotSelected(2);
            }
            else if (Keyboard.current.digit4Key.wasPressedThisFrame)
            {
                OnSlotSelected(3);
            }
            else if (Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                OnCancelPressed();
            }
        }
    }

    // ===== 初始化 =====

    /// <summary>
    /// 初始化 ReplaceController（由 GameBootstrap 调用）
    /// 契约 [C-Runtime-5]: 只允许调用一次，注入依赖并缓存 UI 节点
    /// </summary>
    public void Initialize(ICastleDbService items, HudRefs refs, PlayerContext ctx, HudPresenter hud)
    {
        if (_initialized)
        {
            if (EnableUnityConsoleLogging)
            {
                Debug.LogError($"[ReplaceController] Initialize 只允许调用一次");
            }
            return;
        }

        // 参数校验
        if (items == null || refs == null || ctx == null)
        {
            if (EnableUnityConsoleLogging)
            {
                Debug.LogError($"[ReplaceController] Initialize 参数不能为空：items={items}, refs={refs}, ctx={ctx}");
            }
            return;
        }

        if (hud == null)
        {
            string key = "ReplaceController_NullHudPresenter";
            if (!_loggedErrors.Contains(key))
            {
                if (EnableUnityConsoleLogging)
                {
                    Debug.LogError($"[ReplaceController] HudPresenter 为空，Replace 功能不可用");
                }
                _loggedErrors.Add(key);
            }
            return; // hud 为空则 Replace 不可用
        }

        // 注入依赖
        _items = items;
        _refs = refs;
        _ctx = ctx;
        _hud = hud;
        _panelRoot = refs.abilityReplacePanelRoot;
        // 阶段2：ReplaceController 可能被放在 Player/UI 子节点，PlayerInput 位于 Player 根对象
        _playerInput = GetComponentInParent<PlayerInput>();

        // 校验 panel 存在
        if (_panelRoot == null)
        {
            if (EnableUnityConsoleLogging)
            {
                Debug.LogError($"[ReplaceController] HudRefs.abilityReplacePanelRoot 为空，Replace 功能不可用");
            }
            return;
        }

        // 确保面板初始隐藏
        _panelRoot.SetActive(false);

        // 缓存 UI 节点（契约 [C-UI-2]）
        if (!CacheUINodes())
        {
            if (EnableUnityConsoleLogging)
            {
                Debug.LogError($"[ReplaceController] UI 节点缓存失败，Replace 功能不可用");
            }
            return;
        }

        _initialized = true;
        if (EnableUnityConsoleLogging)
        {
            Debug.Log($"[ReplaceController] 初始化完成");
        }
    }

    /// <summary>
    /// 缓存 UI 节点（按固定路径查找）
    /// 契约 [C-UI-2]: 节点路径和类型必须符合规范
    /// </summary>
    private bool CacheUINodes()
    {
        Transform panelTransform = _panelRoot.transform;

        // 缓存 Pending 节点
        Transform pendingIcon = panelTransform.Find("Pending/Icon");
        Transform pendingName = panelTransform.Find("Pending/NameText");

        if (pendingIcon == null || pendingName == null)
        {
            if (EnableUnityConsoleLogging)
            {
                Debug.LogError($"[ReplaceController] 缺少 Pending 节点");
            }
            return false;
        }

        _pendingIcon = pendingIcon.GetComponent<Image>();
        _pendingNameText = pendingName.GetComponent<TMP_Text>();

        if (_pendingIcon == null || _pendingNameText == null)
        {
            if (EnableUnityConsoleLogging)
            {
                Debug.LogError($"[ReplaceController] Pending 节点组件类型错误");
            }
            return false;
        }

        // 缓存 Slots 节点（4 个槽位）
        for (int i = 0; i < 4; i++)
        {
            Transform slotIcon = panelTransform.Find($"Slots/Slot_{i}/Icon");
            Transform slotHighlight = panelTransform.Find($"Slots/Slot_{i}/Highlight");

            if (slotIcon == null || slotHighlight == null)
            {
                if (EnableUnityConsoleLogging)
                {
                    Debug.LogError($"[ReplaceController] 缺少 Slot_{i} 节点");
                }
                return false;
            }

            _slotIcons[i] = slotIcon.GetComponent<Image>();
            _slotHighlights[i] = slotHighlight.gameObject;

            if (_slotIcons[i] == null)
            {
                if (EnableUnityConsoleLogging)
                {
                    Debug.LogError($"[ReplaceController] Slot_{i}/Icon 组件类型错误");
                }
                return false;
            }

            // 初始隐藏所有 Highlight
            _slotHighlights[i].SetActive(false);
        }

        return true;
    }

    // ===== 替换流程入口 =====

    /// <summary>
    /// 开始替换流程（由 ItemPickup RequireReplace 触发）
    /// 契约 [C-Runtime-5a]: 参数校验 → 状态检查 → 进入 Selecting
    /// </summary>
    public void BeginReplace(PendingReplaceContext ctx)
    {
        // 1) ctx.sourcePickup 必须非空（硬契约，先于状态检查）
        if (ctx.sourcePickup == null)
        {
            string key = "ReplaceController_NullSourcePickup";
            if (!_loggedErrors.Contains(key))
            {
                if (EnableUnityConsoleLogging)
                {
                    Debug.LogError($"[ReplaceController] BeginReplace 失败：ctx.sourcePickup 为空");
                }
                _loggedErrors.Add(key);
            }
            return; // 不得进入 Selecting，也不得尝试解锁
        }

        // 2) 状态检查：仅 Idle 可调用
        if (_state != State.Idle)
        {
            if (EnableUnityConsoleLogging)
            {
                Debug.LogError($"[ReplaceController] BeginReplace 失败：当前状态为 {_state}，仅 Idle 可调用。pendingItemId={ctx.pendingItemId}");
            }
            ctx.sourcePickup.SetLocked(false); // 解锁新请求的 pickup
            return;
        }

        // 3) 未初始化检查（降级：直接解锁）
        if (!_initialized)
        {
            string key = "ReplaceController_NotInitialized";
            if (!_loggedWarnings.Contains(key))
            {
                if (EnableUnityConsoleLogging)
                {
                    Debug.LogWarning($"[ReplaceController] 尚未初始化，降级处理：直接解锁 pickup。pendingItemId={ctx.pendingItemId}");
                    _loggedWarnings.Add(key);
                }
            }
            ctx.sourcePickup.SetLocked(false);
            return;
        }

        // 4) 进入 Selecting 状态
        _state = State.Selecting;
        _pendingContext = ctx;

        // 5) 切换输入到 UI ActionMap（契约 [C-Input-1]）
        if (!SwitchToUIInput())
        {
            // 切换失败，取消流程
            CancelInternal(true);
            return;
        }

        // 6) 渲染替换面板
        RenderPanel();

        // 7) 显示面板
        _panelRoot.SetActive(true);

        if (EnableUnityConsoleLogging)
        {
            Debug.Log($"[ReplaceController] 进入 Selecting 状态，等待用户选择槽位。pendingItemId={ctx.pendingItemId}");
        }
    }

    // ===== 输入切换 =====

    /// <summary>
    /// 切换到 UI ActionMap（契约 [C-Input-1]）
    /// </summary>
    private bool SwitchToUIInput()
    {
        if (_playerInput == null)
        {
            string key = "ReplaceController_MissingPlayerInput";
            if (!_loggedWarnings.Contains(key))
            {
                if (EnableUnityConsoleLogging)
                {
                    Debug.LogWarning($"[ReplaceController] 缺少 PlayerInput，无法切换输入映射，将使用键盘监听替换选择/取消。", this);
                    _loggedWarnings.Add(key);
                }
            }
            return true;
        }

        // 记录当前 ActionMap
        _prevActionMap = _playerInput.currentActionMap?.name;

        // 切换到 UI ActionMap
        try
        {
            _playerInput.SwitchCurrentActionMap("UI");
            if (EnableUnityConsoleLogging)
            {
                Debug.Log($"[ReplaceController] 输入切换：{_prevActionMap} → UI");
            }
            return true;
        }
        catch (System.Exception ex)
        {
            if (EnableUnityConsoleLogging)
            {
                Debug.LogError($"[ReplaceController] 切换到 UI ActionMap 失败: {ex.Message}");
            }
            return false;
        }
    }

    /// <summary>
    /// 恢复之前的 ActionMap（契约 [C-Input-1]）
    /// </summary>
    private void RestoreInput()
    {
        if (_playerInput == null || string.IsNullOrEmpty(_prevActionMap))
        {
            return;
        }

        try
        {
            _playerInput.SwitchCurrentActionMap(_prevActionMap);
            if (EnableUnityConsoleLogging)
            {
                Debug.Log($"[ReplaceController] 输入恢复：UI → {_prevActionMap}");
            }
        }
        catch (System.Exception ex)
        {
            if (EnableUnityConsoleLogging)
            {
                Debug.LogError($"[ReplaceController] 恢复 ActionMap 失败: {ex.Message}");
            }
        }
    }

    // ===== UI 渲染 =====

    /// <summary>
    /// 渲染替换面板（契约 [C-Runtime-5]）
    /// 显示当前 4 槽图标 + 待入槽物品信息
    /// </summary>
    private void RenderPanel()
    {
        // 渲染待入槽物品（Pending）
        RenderPendingItem();

        // 渲染当前 4 槽
        for (int i = 0; i < 4; i++)
        {
            RenderSlot(i);
        }
    }

    /// <summary>
    /// 渲染待入槽物品（Pending 区域）
    /// </summary>
    private void RenderPendingItem()
    {
        string itemId = _pendingContext.pendingItemId;

        if (!_items.TryGetItem(itemId, out ItemDefinition def))
        {
            if (EnableUnityConsoleLogging)
            {
                Debug.LogError($"[ReplaceController] 待入槽物品不存在：{itemId}");
            }
            _pendingIcon.enabled = false;
            _pendingNameText.text = "???";
            return;
        }

        // 加载图标（复用 HudPresenter 缓存）
        Sprite sprite = _hud.GetSprite(def.icon);
        if (sprite != null)
        {
            _pendingIcon.sprite = sprite;
            _pendingIcon.enabled = true;
        }
        else
        {
            _pendingIcon.enabled = false;
        }

        // 显示名称
        _pendingNameText.text = def.displayName;
    }

    /// <summary>
    /// 渲染槽位图标（Slots 区域）
    /// </summary>
    private void RenderSlot(int slotIndex)
    {
        string itemId = _ctx.Inventory.GetAbilityItemId(slotIndex);

        if (string.IsNullOrEmpty(itemId))
        {
            // 空槽
            _slotIcons[slotIndex].enabled = false;
            return;
        }

        if (!_items.TryGetItem(itemId, out ItemDefinition def))
        {
            // 查询失败（数据损坏）
            _slotIcons[slotIndex].enabled = false;
            return;
        }

        // 加载图标（复用 HudPresenter 缓存）
        Sprite sprite = _hud.GetSprite(def.icon);
        if (sprite != null)
        {
            _slotIcons[slotIndex].sprite = sprite;
            _slotIcons[slotIndex].enabled = true;
        }
        else
        {
            _slotIcons[slotIndex].enabled = false;
        }
    }

    // ===== 用户输入处理 =====

    /// <summary>
    /// 用户选择了槽位（1~4 键）
    /// </summary>
    private void OnSlotSelected(int slotIndex)
    {
        if (EnableUnityConsoleLogging)
        {
            Debug.Log($"[ReplaceController] 用户选择槽位 {slotIndex + 1}");
        }

        // 高亮选中槽位（可选，视觉反馈）
        for (int i = 0; i < 4; i++)
        {
            _slotHighlights[i].SetActive(i == slotIndex);
        }

        // 执行 Confirm
        Confirm(slotIndex);
    }

    /// <summary>
    /// 用户取消（ESC 键）
    /// </summary>
    private void OnCancelPressed()
    {
        if (EnableUnityConsoleLogging)
        {
            Debug.Log($"[ReplaceController] 用户取消替换");
        }
        Cancel();
    }

    // ===== Confirm/Cancel 流程 =====

    /// <summary>
    /// 确认替换（契约 [C-Runtime-5] 硬步骤）
    /// </summary>
    private void Confirm(int slotIndex)
    {
        // 1) ValidatePending：校验上下文有效性
        if (!ValidatePending())
        {
            CancelInternal(false); // sourcePickup 已失效，不解锁
            return;
        }

        // 2) TryGetItem：校验待入槽物品存在
        if (!_items.TryGetItem(_pendingContext.pendingItemId, out ItemDefinition def))
        {
            if (EnableUnityConsoleLogging)
            {
                Debug.LogError($"[ReplaceController] Confirm 失败：待入槽物品不存在 {_pendingContext.pendingItemId}");
            }
            CancelInternal(true);
            return;
        }

        // 3) 校验 itemType 必须是 Ability
        if (def.itemType != ItemType.Ability)
        {
            if (EnableUnityConsoleLogging)
            {
                Debug.LogError($"[ReplaceController] Confirm 失败：待入槽物品不是 Ability 类型 {_pendingContext.pendingItemId}");
            }
            CancelInternal(true);
            return;
        }

        // 4) EquipAbilityItemToSlot：尝试装备到指定槽位
        bool success = _ctx.Inventory.EquipAbilityItemToSlot(slotIndex, _pendingContext.pendingItemId);
        if (!success)
        {
            if (EnableUnityConsoleLogging)
            {
                Debug.LogError($"[ReplaceController] Confirm 失败：EquipAbilityItemToSlot 返回 false。slot={slotIndex}, itemId={_pendingContext.pendingItemId}");
            }
            CancelInternal(true); // 装备失败，解锁 pickup
            return;
        }

        // 5) 销毁拾取物
        Destroy(_pendingContext.sourcePickup.gameObject);
        if (EnableUnityConsoleLogging)
        {
            Debug.Log($"[ReplaceController] Confirm 成功：槽位 {slotIndex + 1} 已装备 {_pendingContext.pendingItemId}");
        }

        // 6) ExitSelecting（统一清理）
        ExitSelecting();
    }

    /// <summary>
    /// 取消替换（用户按 ESC）
    /// </summary>
    private void Cancel()
    {
        CancelInternal(true); // 解锁 pickup
    }

    /// <summary>
    /// 取消流程内部实现（契约 [C-Runtime-5]）
    /// 失败路径唯一出口：解锁 pickup（可选）→ ExitSelecting
    /// </summary>
    /// <param name="unlock">是否解锁 sourcePickup</param>
    private void CancelInternal(bool unlock)
    {
        if (unlock && _pendingContext.sourcePickup != null)
        {
            _pendingContext.sourcePickup.SetLocked(false);
            if (EnableUnityConsoleLogging)
            {
                Debug.Log($"[ReplaceController] 已解锁 sourcePickup");
            }
        }

        ExitSelecting();
    }

    /// <summary>
    /// 退出 Selecting 状态（契约 [C-Runtime-5] 统一清理）
    /// 1) 恢复输入
    /// 2) 隐藏面板
    /// 3) 清空 pending
    /// 4) 状态回 Idle
    /// </summary>
    private void ExitSelecting()
    {
        // 1) 恢复输入
        RestoreInput();

        // 2) 隐藏面板
        if (_panelRoot != null)
        {
            _panelRoot.SetActive(false);
        }

        // 3) 清空 pending
        _pendingContext = default;

        // 4) 状态回 Idle
        _state = State.Idle;

        if (EnableUnityConsoleLogging)
        {
            Debug.Log($"[ReplaceController] 已退出 Selecting 状态");
        }
    }

    /// <summary>
    /// 校验 pending 上下文有效性
    /// </summary>
    private bool ValidatePending()
    {
        if (_pendingContext.sourcePickup == null)
        {
            if (EnableUnityConsoleLogging)
            {
                Debug.LogError($"[ReplaceController] ValidatePending 失败：sourcePickup 为空");
            }
            return false;
        }

        if (string.IsNullOrEmpty(_pendingContext.pendingItemId))
        {
            if (EnableUnityConsoleLogging)
            {
                Debug.LogError($"[ReplaceController] ValidatePending 失败：pendingItemId 为空");
            }
            return false;
        }

        return true;
    }
}
