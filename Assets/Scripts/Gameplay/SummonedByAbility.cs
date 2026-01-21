using UnityEngine;

/// <summary>
/// 召唤标记组件：由 SummonAbility 生成的实例会挂载该组件（0.5 扩展）。
/// 用途：
/// - 调试/测试统计
/// - 当能力被禁用时，SummonAbility 可安全清理对应召唤物
/// </summary>
public class SummonedByAbility : MonoBehaviour
{
    [Tooltip("生成该实例的 AbilityId。")]
    public string abilityId = "";
}
