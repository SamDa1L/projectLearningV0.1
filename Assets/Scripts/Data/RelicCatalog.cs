using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 遗物类型（Phase 7）
/// 与 CastleDB/Relic.cdb 的 Relic.kind 保持一致（字符串 -> 枚举映射由导入阶段校验）。
/// </summary>
public enum RelicKind
{
    Shield = 0
}

/// <summary>
/// 遗物定义（导入产物数据结构）
/// </summary>
[Serializable]
public class RelicDefinition
{
    public string id;
    public RelicKind kind;
    public string paramsJson;

    public override string ToString()
    {
        return $"Relic[id={id}, kind={kind}]";
    }
}

/// <summary>
/// 遗物目录（Phase 7）
///
/// 约束：
/// - 由 Tools/CastleDB/Import All 生成/覆盖；禁止手改
/// - Resources 路径：Assets/Resources/Config/RelicCatalog.asset（加载路径为 "Config/RelicCatalog"）
/// </summary>
[CreateAssetMenu(fileName = "RelicCatalog", menuName = "CastleDB/RelicCatalog")]
public class RelicCatalog : ScriptableObject
{
    [SerializeField]
    public List<RelicDefinition> entries = new List<RelicDefinition>();

    [NonSerialized]
    private Dictionary<string, RelicDefinition> _byId;

    [NonSerialized]
    private bool _isValid;

    public bool IsValid => _isValid;

    private void OnEnable()
    {
        RebuildCaches();
    }

    private void RebuildCaches()
    {
        _byId = new Dictionary<string, RelicDefinition>();
        _isValid = true;

        if (entries == null)
        {
            _isValid = false;
            return;
        }

        foreach (var e in entries)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.id))
            {
                _isValid = false;
                continue;
            }

            if (_byId.ContainsKey(e.id))
            {
                _isValid = false;
                continue;
            }

            _byId.Add(e.id, e);
        }
    }

    public bool TryGetRelic(string relicId, out RelicDefinition def)
    {
        def = null;

        if (_byId == null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(relicId))
        {
            return false;
        }

        return _byId.TryGetValue(relicId, out def);
    }

    /// <summary>
    /// 导入阶段调用：写入最新数据并重建缓存。
    /// </summary>
    public void ApplyFromCastleDb(List<RelicDefinition> relicDefinitions)
    {
        if (relicDefinitions == null)
        {
            Debug.LogError("[RelicCatalog] ApplyFromCastleDb 失败：relicDefinitions 为 null");
            entries = new List<RelicDefinition>();
            RebuildCaches();
            return;
        }

        entries = relicDefinitions;
        RebuildCaches();
    }

    private void OnValidate()
    {
#if UNITY_EDITOR
        Debug.LogWarning("[RelicCatalog] 此资产由 Tools/CastleDB/Import All 生成，请勿手动编辑。", this);
#endif
    }
}
