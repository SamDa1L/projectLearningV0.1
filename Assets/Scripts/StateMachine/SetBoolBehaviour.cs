using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 动画状态机行为脚本
///
/// 功能说明:
/// - 在动画状态或状态机进入/退出时自动设置布尔参数
/// - 通过编辑器配置而不是代码，实现高度灵活的动画控制
/// - 用于在特定动画播放期间禁用某些功能(如攻击时禁止移动)
///
/// 继承关系:
/// - 继承自StateMachineBehaviour
/// - StateMachineBehaviour是Unity Animator的内置行为脚本
/// - 可以直接添加到动画状态或状态机中
///
/// 配置参数(Inspector中可见):
/// - boolName: 要控制的Bool参数名称
/// - updateOnState: 是否在状态级别生效
/// - updateOnStateMachine: 是否在状态机级别生效
/// - valueOnEnter: 进入时设置的值
/// - valueOnExit: 退出时设置的值
///
/// 常见用法示例:
/// 1. 攻击状态禁止移动:
///    - boolName = "canMove"
///    - valueOnEnter = false (攻击开始时禁止移动)
///    - valueOnExit = true (攻击结束时允许移动)
///
/// 2. 翻滚状态无敌:
///    - boolName = "isInvincible"
///    - valueOnEnter = true (翻滚开始时无敌)
///    - valueOnExit = false (翻滚结束时可被伤害)
/// </summary>
public class SetBoolBehaviour : StateMachineBehaviour
{
    /// <summary>
    /// 要控制的Bool参数的名称
    /// 说明: 必须与Animator中创建的参数名称完全一致
    /// 示例: "canMove", "isInvincible", "isAttacking" 等
    /// </summary>
    public string boolName;

    /// <summary>
    /// 是否在状态级别生效
    /// 说明:
    /// - true: 当动画状态进入/退出时触发
    /// - false: 忽略状态级别的事件
    ///
    /// 使用场景:
    /// - 添加到具体的动画状态时，设置为true
    /// - 例如添加到"Attack"状态上
    /// </summary>
    public bool updateOnState;

    /// <summary>
    /// 是否在状态机级别生效
    /// 说明:
    /// - true: 当进入/退出整个状态机时触发
    /// - false: 忽略状态机级别的事件
    ///
    /// 使用场景:
    /// - 添加到状态机入口/出口时，设置为true
    /// - 用于处理状态机级别的全局状态变化
    /// </summary>
    public bool updateOnStateMachine;

    /// <summary>
    /// 进入时设置的bool值
    /// 说明: 当状态或状态机被进入时，boolName参数会被设置为此值
    /// 示例: 攻击状态进入时，canMove设置为false
    /// </summary>
    public bool valueOnEnter;

    /// <summary>
    /// 退出时设置的bool值
    /// 说明: 当状态或状态机被退出时，boolName参数会被设置为此值
    /// 示例: 攻击状态退出时，canMove设置为true
    /// </summary>
    public bool valueOnExit;

    /// <summary>
    /// 当进入动画状态时调用
    /// 由Animator系统自动调用(在OnStateEnter事件之前)
    ///
    /// 参数说明:
    /// - animator: 触发此回调的Animator组件
    /// - stateInfo: 当前状态的信息(如哈希值、归一化时间等)
    /// - layerIndex: 所在的动画层级(0=默认层)
    ///
    /// 功能:
    /// - 如果updateOnState为true，则在进入状态时设置Bool参数
    /// - 用于启用某些功能(如禁止移动、启用无敌等)
    /// </summary>
    override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        // 检查是否启用了状态级别的更新
        if (updateOnState)
        {
            // 设置指定的Bool参数为valueOnEnter的值
            animator.SetBool(boolName, valueOnEnter);
        }
    }

    /// <summary>
    /// 当更新动画状态时调用
    /// 每帧在状态更新时被调用
    ///
    /// 说明: 此方法已注释，如果需要每帧执行逻辑，可以取消注释
    /// </summary>
    //override public void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    //{
    //
    //}

    /// <summary>
    /// 当退出动画状态时调用
    /// 由Animator系统自动调用(在OnStateExit事件时)
    ///
    /// 参数说明:
    /// - animator: 触发此回调的Animator组件
    /// - stateInfo: 当前状态的信息
    /// - layerIndex: 所在的动画层级
    ///
    /// 功能:
    /// - 如果updateOnState为true，则在退出状态时设置Bool参数
    /// - 用于禁用某些功能(如恢复移动、解除无敌等)
    ///
    /// 注意: 当前实现中使用了valueOnEnter而不是valueOnExit
    ///       这可能是一个bug，应该改为valueOnExit
    /// </summary>
    override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        // 检查是否启用了状态级别的更新
        if (updateOnState)
        {
            // 退出状态时也设置为valueOnEnter(这里可能需要改为valueOnExit)
            animator.SetBool(boolName, valueOnExit);
        }
    }

    /// <summary>
    /// 当执行状态的运动时调用
    /// 用于IK(逆向动力学)和位置/旋转的计算
    ///
    /// 说明: 此方法已注释，通常用于高级动作系统
    /// </summary>
    //override public void OnStateMove(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    //{
    //
    //}

    /// <summary>
    /// 当执行IK(逆向动力学)时调用
    /// 用于手脚IK位置计算
    ///
    /// 说明: 此方法已注释，仅在使用IK系统时需要
    /// </summary>
    //override public void OnStateIK(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    //{
    //
    //}

    /// <summary>
    /// 当通过Entry Node进入状态机时调用
    /// Entry Node是状态机的默认入口点
    ///
    /// 参数说明:
    /// - animator: Animator组件引用
    /// - stateMachinePathHash: 状态机路径的哈希值(用于标识具体的状态机)
    ///
    /// 功能:
    /// - 如果updateOnStateMachine为true，则在进入状态机时设置Bool参数
    /// - 用于处理状态机级别的全局逻辑
    ///
    /// 示例: 进入"Combat"状态机时，设置"isInCombat"为true
    /// </summary>
    override public void OnStateMachineEnter(Animator animator, int stateMachinePathHash)
    {
        // 检查是否启用了状态机级别的更新
        if (updateOnStateMachine)
            // 进入状态机时设置Bool参数
            animator.SetBool(boolName, valueOnEnter);
    }

    /// <summary>
    /// 当通过Exit Node退出状态机时调用
    /// Exit Node是状态机的退出点
    ///
    /// 参数说明:
    /// - animator: Animator组件引用
    /// - stateMachinePathHash: 状态机路径的哈希值
    ///
    /// 功能:
    /// - 如果updateOnStateMachine为true，则在退出状态机时设置Bool参数
    /// - 用于清理状态机级别的全局状态
    ///
    /// 示例: 退出"Combat"状态机时，设置"isInCombat"为false
    /// </summary>
    override public void OnStateMachineExit(Animator animator, int stateMachinePathHash)
    {
        // 检查是否启用了状态机级别的更新
        if (updateOnStateMachine)
            // 退出状态机时设置Bool参数(使用valueOnExit值)
            animator.SetBool(boolName, valueOnExit);
    }
}
