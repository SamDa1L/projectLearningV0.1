using System.Collections.Generic;
using UnityEngine;
using CastleDB.Runtime;

/// <summary>
/// 能力目录条目（阶段 3B）
/// 运行时可执行的能力配置
/// </summary>
[System.Serializable]
public class AbilityCatalogEntry
{
    /// <summary>能力 ID（对应 CastleDB Ability.id，用于 Registry/Factory 映射）</summary>
    public string id;

    /// <summary>Hook 类型</summary>
    public AbilityHookType hookType;

    /// <summary>优先级（数值越大越先执行）</summary>
    public int priority;

    /// <summary>是否启用</summary>
    public bool enabled;

    /// <summary>参数 JSON（Phase 1-2：运行时会消费 kind 等配置；Import 阶段负责校验）</summary>
    public string paramsJson;

    public override string ToString()
    {
        return $"AbilityCatalogEntry[id={id}, hookType={hookType}, priority={priority}, enabled={enabled}]";
    }
}

/// <summary>
/// 能力目录（阶段 3B）
///
/// 从 CastleDB Ability Sheet 导入的能力配置资产
/// 运行时根据此资产构建能力系统
///
/// 设计约束：
/// - 此资产由 Tools/CastleDB/Import All 生成/覆盖，禁止手动编辑
/// - OnValidate 检测手动编辑并提示
/// </summary>
[CreateAssetMenu(fileName = "AbilityCatalog", menuName = "CastleDB/AbilityCatalog")]
public class AbilityCatalog : ScriptableObject
{
    /// <summary>
    /// 能力条目列表
    /// </summary>
    [SerializeField]
    public List<AbilityCatalogEntry> entries = new List<AbilityCatalogEntry>();

    /// <summary>
    /// 从 CastleDB DTO 应用数据（Import 阶段调用）
    /// </summary>
    /// <param name="abilityEntries">CastleDB Ability Sheet 的所有条目</param>
    public void ApplyFromCastleDb(List<AbilityEntry> abilityEntries)
    {
        if (abilityEntries == null)
        {
            Debug.LogError("[AbilityCatalog] ApplyFromCastleDb: abilityEntries is null");
            return;
        }

        // 清空现有条目
        entries.Clear();

        // 转换 DTO 到运行时格式
        foreach (var dto in abilityEntries)
        {
            var entry = new AbilityCatalogEntry
            {
                id = dto.id,
                hookType = (AbilityHookType)dto.hookType,
                priority = dto.priority,
                enabled = dto.enabled,
                paramsJson = dto.paramsJson ?? ""
            };

            entries.Add(entry);
        }

        Debug.Log($"[AbilityCatalog] Applied {entries.Count} ability entries from CastleDB");
    }

    private void OnValidate()
    {
        // 警告：此资产应由 Import All 生成，不应手动编辑
        // 0.2 在编辑器和运行时都输出警告，不强制回退（避免 Inspector 编辑时频繁弹窗）
#if UNITY_EDITOR
        Debug.LogWarning("[AbilityCatalog] 此资产由 Tools/CastleDB/Import All 生成，请勿手动编辑。" +
            "如需修改能力配置，请在 CastleDB 中编辑 Ability Sheet 并重新导入。", this);
#else
        Debug.LogWarning("[AbilityCatalog] 此资产由 Tools/CastleDB/Import All 生成，请勿手动编辑。" +
            "如需修改能力配置，请在 CastleDB 中编辑 Ability Sheet 并重新导入。");
#endif
    }
}
