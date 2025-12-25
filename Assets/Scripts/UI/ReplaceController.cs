using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 替换控制器
/// Phase 4 职责：基本降级处理（参数校验、解锁机制）
/// Phase 8 职责：完整实现（状态机、输入映射、UI 面板控制）
///
/// 规范（契约 [C-Runtime-5a]）：
/// - BeginReplace 参数校验：sourcePickup 必须非空
/// - 未初始化时降级：Error + 解锁 sourcePickup
/// - Phase 4 降级策略：直接解锁 pickup，不进入 Selecting（避免拾取物永久锁定）
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
    /// 是否正在选择替换槽位
    /// </summary>
    public bool IsSelecting => _state == State.Selecting;

    // ===== 初始化标记 =====
    private bool _initialized = false;

    // ===== 一次性日志去重 =====
    private static readonly HashSet<string> _loggedWarnings = new HashSet<string>();

    // ===== Phase 8 依赖（暂未注入） =====
    // private CastleDbService _items;
    // private HudRefs _refs;
    // private PlayerContext _ctx;
    // private HudPresenter _hud;
    // private GameObject _panelRoot;

    /// <summary>
    /// 开始替换流程
    /// Phase 4 实现：参数校验 + 降级处理（直接解锁 pickup）
    /// Phase 8 实现：完整状态机 + UI 面板 + 输入映射
    /// </summary>
    /// <param name="ctx">待替换上下文</param>
    public void BeginReplace(PendingReplaceContext ctx)
    {
        // 1) ctx.sourcePickup 必须非空（硬契约，先于状态检查）
        if (ctx.sourcePickup == null)
        {
            string key = "ReplaceController_NullSourcePickup";
            if (!_loggedWarnings.Contains(key))
            {
                Debug.LogError($"[ReplaceController] BeginReplace 失败：ctx.sourcePickup 为空，无法进入 Replace 流程");
                _loggedWarnings.Add(key);
            }
            return; // 不得进入 Selecting，也不得尝试解锁
        }

        // 2) 状态检查：仅 Idle 可调用
        if (_state != State.Idle)
        {
            Debug.LogError($"[ReplaceController] BeginReplace 失败：当前状态为 {_state}，仅 Idle 可调用。pendingItemId={ctx.pendingItemId}");
            ctx.sourcePickup.SetLocked(false); // 解锁新请求的 pickup
            return;
        }

        // 3) 未初始化检查（Phase 4 降级：直接解锁）
        if (!_initialized)
        {
            string key = "ReplaceController_NotInitialized";
            if (!_loggedWarnings.Contains(key))
            {
                Debug.LogWarning($"[ReplaceController] 尚未初始化（Phase 8 实现 Initialize），降级处理：直接解锁 pickup。pendingItemId={ctx.pendingItemId}");
                _loggedWarnings.Add(key);
            }
            ctx.sourcePickup.SetLocked(false);
            return;
        }

        // Phase 8 TODO: 完整实现
        // 4) 进入 Selecting 状态
        // 5) 切换输入到 UI ActionMap
        // 6) 显示替换面板
        // 7) 等待用户选择槽位（1~4 键）或取消（ESC）

        // Phase 4 降级：直接解锁（避免 pickup 永久锁定）
        Debug.LogWarning($"[ReplaceController] BeginReplace 尚未完整实现（Phase 8），降级处理：直接解锁 pickup。pendingItemId={ctx.pendingItemId}");
        ctx.sourcePickup.SetLocked(false);
    }

    /// <summary>
    /// 初始化（Phase 8 实现）
    /// </summary>
    public void Initialize(/* CastleDbService items, HudRefs refs, PlayerContext ctx, HudPresenter hud */)
    {
        if (_initialized)
        {
            Debug.LogError($"[ReplaceController] Initialize 只允许调用一次");
            return;
        }

        // Phase 8 实现依赖注入
        // _items = items;
        // _refs = refs;
        // _ctx = ctx;
        // _hud = hud;
        // _panelRoot = refs.abilityReplacePanelRoot;

        _initialized = true;
        Debug.Log($"[ReplaceController] 初始化完成（Phase 8 完整实现时注入依赖）");
    }

    // Phase 8 TODO:
    // - 实现 Confirm(int slotIndex) 方法
    // - 实现 Cancel() 方法
    // - 实现 ExitSelecting() 统一清理
    // - 实现 CancelInternal(bool unlock) 失败路径
    // - 实现输入监听（1~4 键、ESC 键）
    // - 实现面板渲染
}
