using System.Collections.Generic;
using UnityEngine;

namespace CastleDB.Runtime
{
    /// <summary>
    /// 状态叠加规则（Phase 1-4）
    /// </summary>
    public enum StatusStackRule
    {
        /// <summary>
        /// 已存在则刷新持续时间（不加层数）
        /// </summary>
        Refresh = 0,

        /// <summary>
        /// 已存在则增加层数（到 maxStacks），并刷新持续时间
        /// </summary>
        Add = 1,

        /// <summary>
        /// 已存在则忽略本次 Apply（不刷新、不加层）
        /// </summary>
        Ignore = 2,

        /// <summary>
        /// 替换现有状态（重置层数与持续时间）
        /// </summary>
        Replace = 3
    }

    /// <summary>
    /// 状态修改器（Phase 1-4 最小集：只实现 MoveSpeedMultiplier）
    /// 约定：默认值 1 表示不修改。
    /// </summary>
    [System.Serializable]
    public struct StatusModifiers
    {
        [Tooltip("移速倍率（1=不变；0.5=减半；2=翻倍）")]
        public float moveSpeedMultiplier;

        public StatusModifiers(float moveSpeedMultiplier)
        {
            this.moveSpeedMultiplier = moveSpeedMultiplier;
        }

        public static StatusModifiers Default => new StatusModifiers(1f);
    }

    /// <summary>
    /// 状态定义（导入产物条目）
    /// </summary>
    [System.Serializable]
    public class StatusDefinition
    {
        public string id;
        public string displayName;

        [Tooltip("默认持续时间（秒）。<=0 表示永久（直到 Remove）。")]
        public float defaultDuration;

        public StatusStackRule stackRule;

        [Tooltip("最大层数（>=1）")]
        public int maxStacks = 1;

        public StatusModifiers modifiers = StatusModifiers.Default;

        public override string ToString()
        {
            return $"Status[id={id}, duration={defaultDuration}, rule={stackRule}, maxStacks={maxStacks}, moveSpeedMult={modifiers.moveSpeedMultiplier}]";
        }
    }

    /// <summary>
    /// 状态目录（Phase 1-4）
    ///
    /// 从 CastleDB Status Sheet 导入的状态配置资产。
    /// 运行时由 StatusEffectController 查询。
    ///
    /// 规范：
    /// - 此资产由 Tools/CastleDB/Import All 生成/覆盖，禁止手动编辑
    /// - 路径建议：Assets/Resources/Config/StatusCatalog.asset
    /// </summary>
    [CreateAssetMenu(fileName = "StatusCatalog", menuName = "CastleDB/StatusCatalog")]
    public class StatusCatalog : ScriptableObject
    {
        [SerializeField]
        public StatusDefinition[] statuses = new StatusDefinition[0];

        [System.NonSerialized]
        private Dictionary<string, StatusDefinition> byId;

        [System.NonSerialized]
        private bool _isValid = false;

        public bool IsValid => _isValid;

        private void OnEnable()
        {
            byId = new Dictionary<string, StatusDefinition>();
            _isValid = false;

            if (statuses == null || statuses.Length == 0)
            {
                _isValid = true;
                return;
            }

            foreach (var status in statuses)
            {
                if (status == null)
                {
                    Debug.LogError("[StatusCatalog] Found null status in catalog, resource is corrupted!", this);
                    byId.Clear();
                    byId = null;
                    _isValid = false;
                    return;
                }

                if (string.IsNullOrWhiteSpace(status.id))
                {
                    Debug.LogError("[StatusCatalog] Found status with empty id, resource is corrupted!", this);
                    byId.Clear();
                    byId = null;
                    _isValid = false;
                    return;
                }

                if (byId.ContainsKey(status.id))
                {
                    Debug.LogError($"[StatusCatalog] Duplicate status id detected: '{status.id}', resource is corrupted!", this);
                    byId.Clear();
                    byId = null;
                    _isValid = false;
                    return;
                }

                // 轻量归一化（避免运行时出现 NaN/0 stacks 等）
                status.maxStacks = Mathf.Max(1, status.maxStacks);
                status.modifiers.moveSpeedMultiplier = Mathf.Max(0f, status.modifiers.moveSpeedMultiplier);

                byId[status.id] = status;
            }

            _isValid = true;
        }

        public bool TryGetStatus(string statusId, out StatusDefinition def)
        {
            def = null;

            if (byId == null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(statusId))
            {
                return false;
            }

            return byId.TryGetValue(statusId, out def);
        }

        public System.Collections.Generic.IReadOnlyList<StatusDefinition> GetAllStatuses()
        {
            return statuses;
        }

        public void ApplyFromCastleDb(List<StatusDefinition> statusDefinitions)
        {
            if (statusDefinitions == null)
            {
                Debug.LogError("[StatusCatalog] ApplyFromCastleDb: statusDefinitions is null");
                statuses = new StatusDefinition[0];
                return;
            }

            statuses = statusDefinitions.ToArray();
            OnEnable();
        }

        private void OnValidate()
        {
#if UNITY_EDITOR
            Debug.LogWarning("[StatusCatalog] 此资产由 Tools/CastleDB/Import All 生成，请勿手动编辑。" +
                "如需修改状态配置，请在 CastleDB 中编辑并重新导入。", this);
#else
            Debug.LogWarning("[StatusCatalog] 此资产由 Tools/CastleDB/Import All 生成，请勿手动编辑。", this);
#endif
        }
    }
}
