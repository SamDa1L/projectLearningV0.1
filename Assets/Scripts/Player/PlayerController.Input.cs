using UnityEngine;
using UnityEngine.InputSystem;

public partial class PlayerController
{
    /// <summary>
    /// 移动输入回调函数（阶段 3B：适配器模式）
    /// 由Input System在输入事件发生时调用
    ///
    /// 适配器职责（阶段 3B）：
    /// - 当 usePlayerConfigFromCastleDb=true: 仅适配输入并派发到能力系统，不执行业务逻辑
    /// - 当 usePlayerConfigFromCastleDb=false: 执行原有业务逻辑（回退方案）
    ///
    /// 原有功能（回退模式）:
    /// - 读取移动输入的Vector2值(WASD或摇杆)
    /// - 分离处理水平输入(X轴/A/D)和垂直输入(Y轴/W/S)
    /// - 水平输入驱动行走/奔跑动画和角色朝向
    /// - 垂直输入用于爬墙系统(W/S控制上下爬行)
    /// </summary>
    public void OnMove(InputAction.CallbackContext context)
    {
        // 从输入事件读取Vector2值(来自WASD键或左摇杆)
        moveInput = context.ReadValue<Vector2>();

        // 阶段 3B: 适配器模式分支
        if (usePlayerConfigFromCastleDb && abilitySystem != null)
        {
            // 派发到能力系统（不执行业务逻辑）
            AbilityInput input = AbilityInput.Performed(moveInput, true);
            abilitySystem.Dispatch(AbilityHookType.Move, input);
            return; // 立即返回，不执行下方的原有逻辑
        }

        // 回退模式：执行原有业务逻辑
        if (IsAlive)
        {
            // 分离水平和垂直输入分量
            // 水平输入(A/D): 用于行走/奔跑
            moveInputHorizontal = moveInput.x;

            // 垂直输入(W/S): 用于爬墙系统
            moveInputVertical = moveInput.y;

            // 同时保存给爬墙使用
            climbInput = moveInput;
            // 爬墙逻辑判断
            // 条件: 接触墙壁 && 有垂直输入 && 允许移动
            if (touchingDirections.IsOnWall && moveInputVertical != 0 && CanMove)
            {
                // 进入爬墙状态
                IsClimbing = true;
            }
            else if (!touchingDirections.IsOnWall || moveInputVertical == 0)
            {
                // 退出爬墙状态: 离开墙壁 或 没有垂直输入
                IsClimbing = false;
            }

            // 根据爬墙状态更新行走状态和朝向
            if (!IsClimbing)
            {
                // 正常模式: 判断是否行走，更新朝向
                IsMoving = moveInputHorizontal != 0;
                SetFacingDirection(moveInput);
            }
            else
            {
                // 爬墙模式: 禁止水平移动，保持朝向
                IsMoving = false;
                // 朝向保持不变，不调用SetFacingDirection
            }
        }
        else
        {
            IsMoving = false;
        }
    }

    /// <summary>
    /// 设置角色朝向函数
    ///
    /// 根据水平输入的X分量判断角色应该朝向的方向
    /// - moveInput.x > 0 -> 朝向右侧
    /// - moveInput.x < 0 -> 朝向左侧
    /// - moveInput.x = 0 -> 保持当前朝向
    ///
    /// 说明: 仅检查X分量(水平方向)
    ///       Y分量(垂直方向/W/S)不影响朝向
    ///
    /// 参数:
    /// - moveInput: 移动输入向量
    /// </summary>
    private void SetFacingDirection(Vector2 moveInput)
    {
        // 如果输入向右且当前朝向左侧，则改为朝向右侧
        if (moveInput.x > 0 && !IsFacingRight)
        {
            // 设置朝向为右
            IsFacingRight = true;
        }
        // 如果输入向左且当前朝向右侧，则改为朝向左侧
        else if (moveInput.x < 0 && IsFacingRight)
        {
            // 设置朝向为左
            IsFacingRight = false;
        }
        // 注意: 当moveInput.x = 0(包括只按W/S)时，朝向不变
    }

    /// <summary>
    /// 奔跑输入回调函数（阶段 3B：适配器模式）
    /// 由Input System在按下/释放奔跑键时调用(默认Shift键)
    ///
    /// 适配器职责（阶段 3B）：
    /// - 当 usePlayerConfigFromCastleDb=true: 仅适配输入并派发到能力系统
    /// - 当 usePlayerConfigFromCastleDb=false: 执行原有业务逻辑（回退方案）
    ///
    /// 原有功能（回退模式）:
    /// - 按下时设置IsRunning=true，启用奔跑状态
    /// - 释放时设置IsRunning=false，返回行走状态
    /// </summary>
    public void OnRun(InputAction.CallbackContext context)
    {
        // 阶段 3B: 适配器模式分支
        if (usePlayerConfigFromCastleDb && abilitySystem != null)
        {
            // 适配输入阶段
            AbilityInput input;
            if (context.started)
            {
                input = AbilityInput.Started(isPressed: true);
            }
            else if (context.canceled)
            {
                input = AbilityInput.Canceled();
            }
            else
            {
                return; // 忽略其他阶段
            }

            // 派发到能力系统
            abilitySystem.Dispatch(AbilityHookType.Run, input);
            return;
        }

        // 回退模式：执行原有业务逻辑
        if (context.started)
        {
            // 奔跑键按下 - 启用奔跑
            IsRunning = true;
        }
        else if (context.canceled)
        {
            // 奔跑键释放 - 禁用奔跑
            IsRunning = false;
        }
    }

    /// <summary>
    /// 跳跃输入回调函数（阶段 3B：适配器模式）
    /// 由Input System在按下空格键时调用
    ///
    /// 适配器职责（阶段 3B）：
    /// - 当 usePlayerConfigFromCastleDb=true: 仅适配输入并派发到能力系统
    /// - 当 usePlayerConfigFromCastleDb=false: 执行原有业务逻辑（回退方案）
    ///
    /// 原有功能（回退模式）:
    /// - 支持地面跳跃和壁跳
    /// - 触发Animator的跳跃动画
    /// - 给予Y轴速度(jumpImpules)实现向上运动
    /// - 壁跳时给予横向冲力
    /// </summary>
    public void OnJump(InputAction.CallbackContext context)
    {
        // 阶段 3B: 适配器模式分支
        if (usePlayerConfigFromCastleDb && abilitySystem != null)
        {
            // 只处理 started 阶段
            if (context.started)
            {
                AbilityInput input = AbilityInput.Started(isPressed: true);
                abilitySystem.Dispatch(AbilityHookType.Jump, input);
            }
            return;
        }

        // 回退模式：执行原有业务逻辑
        if (context.started && CanMove)
        {
            // 支持地面跳跃或壁跳
            bool canJumpFromGround = touchingDirections.IsGrounded;
            bool canJumpFromWall = IsClimbing && touchingDirections.IsOnWall;

            if (canJumpFromGround || canJumpFromWall)
            {
                // 触发Animator的跳跃动画
                animator.SetTrigger(AnimationStrings.jumpTrigger);

                if (canJumpFromWall)
                {
                    // 壁跳逻辑: 给予离墙的横向冲力 + 向上冲力
                    float wallJumpForce = 8f;
                    float horizontalForce = IsFacingRight ? -wallJumpForce : wallJumpForce;

                    // 设置速度: 横向冲力 + 向上冲力
                    rb.velocity = new Vector2(horizontalForce, jumpImpules);

                    // 立即退出爬墙状态
                    IsClimbing = false;
                }
                else
                {
                    // 地面跳跃: 保持X轴速度，只改变Y轴
                    rb.velocity = new Vector2(rb.velocity.x, jumpImpules);
                }
            }
        }
    }

    /// <summary>
    /// 攻击输入回调函数（阶段 3B：适配器模式）
    /// 由Input System在按下攻击键时调用(默认Z键或J键)
    ///
    /// 适配器职责（阶段 3B）：
    /// - 当 usePlayerConfigFromCastleDb=true: 仅适配输入并派发到能力系统
    /// - 当 usePlayerConfigFromCastleDb=false: 执行原有业务逻辑（回退方案）
    ///
    /// 原有功能（回退模式）:
    /// - 触发Animator的攻击动画
    /// - 动画系统会自动控制CanMove参数，禁止攻击时的移动
    /// </summary>
    public void OnAttack(InputAction.CallbackContext context)
    {
        // 阶段 3B: 适配器模式分支
        if (usePlayerConfigFromCastleDb && abilitySystem != null)
        {
            // 只处理 started 阶段
            if (context.started)
            {
                AbilityInput input = AbilityInput.Started(isPressed: true);

                // 0.5：按槽位释放（方块/Attack 固定触发 slot0 当前装备的能力）
                if (_playerContext != null
                    && _playerContext.Inventory != null
                    && _playerContext.Inventory.TryGetAbilityIdInSlot(0, out string abilityId)
                    && !string.IsNullOrWhiteSpace(abilityId))
                {
                    abilitySystem.TryDispatchByAbilityId(abilityId, input);
                }
                else
                {
                    // 兜底：避免 Inventory 未初始化导致开局无法攻击
                    abilitySystem.Dispatch(AbilityHookType.Attack, input);
                }
            }
            return;
        }

        // 回退模式：执行原有业务逻辑
        if (context.started)
        {
            // 触发Animator的攻击动画
            animator.SetTrigger(AnimationStrings.attackTrigger);
        }
    }

    /// <summary>
    /// 远程攻击输入回调函数（阶段 3B：适配器模式）
    /// 由Input System在按下远程攻击键时调用
    ///
    /// 适配器职责（阶段 3B）：
    /// - 当 usePlayerConfigFromCastleDb=true: 仅适配输入并派发到能力系统
    /// - 当 usePlayerConfigFromCastleDb=false: 执行原有业务逻辑（回退方案）
    ///
    /// 原有功能（回退模式）:
    /// - 触发Animator的远程攻击动画
    /// </summary>
    public void OnRangedAttack(InputAction.CallbackContext context)
    {
        // 兼容旧的输入动作命名（阶段 9 将输入动作命名为 Ability2/Ability3/Ability4）。
        OnAbility2(context);
    }

    public void OnAbility2(InputAction.CallbackContext context)
    {
        // 阶段 3B: 适配器模式分支
        if (usePlayerConfigFromCastleDb && abilitySystem != null)
        {
            // 只处理 started 阶段
            if (context.started)
            {
                AbilityInput input = AbilityInput.Started(isPressed: true);

                // 0.5 阶段 9：按槽位释放（Ability2 固定触发 slot1 当前装备的能力）
                if (_playerContext != null
                    && _playerContext.Inventory != null
                    && _playerContext.Inventory.TryGetAbilityIdInSlot(1, out string abilityId)
                    && !string.IsNullOrWhiteSpace(abilityId))
                {
                    abilitySystem.TryDispatchByAbilityId(abilityId, input);
                    return;
                }
            }
            return;
        }

        // 回退模式：执行原有业务逻辑（与旧远程攻击动画保持一致）
        if (context.started)
        {
            animator.SetTrigger(AnimationStrings.rangedAttackTrigger);
        }
    }

    public void OnAbility3(InputAction.CallbackContext context)
    {
        if (usePlayerConfigFromCastleDb && abilitySystem != null)
        {
            if (context.started)
            {
                AbilityInput input = AbilityInput.Started(isPressed: true);

                // 0.5 阶段9：按槽位释放（Ability3 固定触发 slot2 当前装备的能力）
                if (_playerContext != null
                    && _playerContext.Inventory != null
                    && _playerContext.Inventory.TryGetAbilityIdInSlot(2, out string abilityId)
                    && !string.IsNullOrWhiteSpace(abilityId))
                {
                    abilitySystem.TryDispatchByAbilityId(abilityId, input);
                }
            }
            return;
        }
    }

    public void OnAbility4(InputAction.CallbackContext context)
    {
        if (usePlayerConfigFromCastleDb && abilitySystem != null)
        {
            if (context.started)
            {
                AbilityInput input = AbilityInput.Started(isPressed: true);

                // 0.5 阶段9：按槽位释放（Ability4 固定触发 slot3 当前装备的能力）
                if (_playerContext != null
                    && _playerContext.Inventory != null
                    && _playerContext.Inventory.TryGetAbilityIdInSlot(3, out string abilityId)
                    && !string.IsNullOrWhiteSpace(abilityId))
                {
                    abilitySystem.TryDispatchByAbilityId(abilityId, input);
                }
            }
            return;
        }
    }

    // ===== 阶段 3B: 能力系统公开 API =====

    /// <summary>
    /// 应用移动输入（阶段 3B 能力系统专用 API）
    ///
    /// 功能：
    /// - 更新输入缓存（moveInput, moveInputHorizontal, moveInputVertical, climbInput）
    /// - 执行爬墙逻辑判断
    /// - 更新 IsMoving 状态（触发 Animator 同步）
    /// - 更新角色朝向（触发 Transform 翻转）
    ///
    /// 设计原则：
    /// - 能力实现不直接操作私有字段，通过此 API 复用现有逻辑
    /// - 保证 Animator 参数、Transform 翻转、物理输入缓存三者一致
    /// </summary>
    public void ApplyMoveInput(Vector2 move)
    {
        // 更新输入缓存
        moveInput = move;
        moveInputHorizontal = move.x;
        moveInputVertical = move.y;
        climbInput = move;

        if (!IsAlive)
        {
            IsMoving = false;
            return;
        }

        // 爬墙逻辑判断
        if (touchingDirections.IsOnWall && moveInputVertical != 0 && CanMove)
        {
            IsClimbing = true;
        }
        else if (!touchingDirections.IsOnWall || moveInputVertical == 0)
        {
            IsClimbing = false;
        }

        // 根据爬墙状态更新行走状态和朝向
        if (!IsClimbing)
        {
            IsMoving = moveInputHorizontal != 0;
            SetFacingDirection(moveInput);
        }
        else
        {
            IsMoving = false;
        }
    }

    /// <summary>
    /// 设置奔跑状态（阶段 3B 能力系统专用 API）
    ///
    /// 功能：
    /// - 使用 IsRunning 属性 setter，保证 Animator 参数同步
    ///
    /// 设计原则：
    /// - 能力实现不直接操作 _isRunning 字段，通过此 API 保证副作用一致
    /// </summary>
    public void SetRunning(bool running)
    {
        IsRunning = running;
    }
}
