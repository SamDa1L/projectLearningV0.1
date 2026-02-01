using System.Collections.Generic;
using UnityEngine;

namespace CastleDB.Runtime
{
    /// <summary>
    /// Item 目录（0.4 版本）
    ///
    /// 从 CastleDB Item Sheet 导入的 Item 配置资产
    /// 运行时通过 CastleDbService.TryGetItem() 查询
    ///
    /// 规范（[C-Data-4]）：
    /// - 此资产由 Tools/CastleDB/Import All 生成/覆盖，禁止手动编辑
    /// - 路径：Assets/Resources/Config/ItemCatalog.asset
    /// - 结构：ItemDefinition[] items（序列化）+ Dictionary<string, ItemDefinition> byId（OnEnable 构建）
    /// - 如发现重复 id 视为资源损坏：Error 并终止构建
    /// </summary>
    [CreateAssetMenu(fileName = "ItemCatalog", menuName = "CastleDB/ItemCatalog")]
    public class ItemCatalog : ScriptableObject
    {
        /// <summary>
        /// Item 条目列表（序列化）
        /// </summary>
        [SerializeField]
        public ItemDefinition[] items = new ItemDefinition[0];

        /// <summary>
        /// 通过 id 快速查询的字典（OnEnable 构建，不序列化）
        /// </summary>
        [System.NonSerialized]
        private Dictionary<string, ItemDefinition> byId;

        /// <summary>
        /// 资源有效性标记
        /// 重复 id 等资源损坏时为 false
        /// </summary>
        [System.NonSerialized]
        private bool _isValid = false;

        /// <summary>
        /// 资源是否有效
        /// </summary>
        public bool IsValid => _isValid;

        /// <summary>
        /// OnEnable: 构建 byId 字典
        /// 如发现重复 id 视为资源损坏：Error 并终止构建（byId 置空）
        /// </summary>
        private void OnEnable()
        {
            byId = new Dictionary<string, ItemDefinition>();
            _isValid = false;

            if (items == null || items.Length == 0)
            {
                Debug.LogWarning("[ItemCatalog] No items found in catalog", this);
                _isValid = true; // 空目录视为有效（允许 Bootstrap 检测）
                return;
            }

            // 构建 byId 字典，检测重复 id
            foreach (var item in items)
            {
                if (item == null)
                {
                    Debug.LogError("[ItemCatalog] Found null item in catalog, resource is corrupted!", this);
                    byId.Clear();
                    byId = null;
                    _isValid = false;
                    return;
                }

                if (string.IsNullOrEmpty(item.id))
                {
                    Debug.LogError("[ItemCatalog] Found item with empty id, resource is corrupted!", this);
                    byId.Clear();
                    byId = null;
                    _isValid = false;
                    return;
                }

                if (byId.ContainsKey(item.id))
                {
                    Debug.LogError($"[ItemCatalog] Duplicate item id detected: '{item.id}', resource is corrupted!", this);
                    byId.Clear();
                    byId = null;
                    _isValid = false;
                    return;
                }

                byId[item.id] = item;
            }

            _isValid = true;
            Debug.Log($"[ItemCatalog] Loaded {items.Length} items");
        }

        /// <summary>
        /// 尝试获取 Item 定义
        /// 规范（[C-Runtime-1]）：
        /// - 不存在返回 false，不写日志（调用方可选 Debug 一次性）
        /// - 如资源损坏（byId 为空）则返回 false
        /// </summary>
        /// <param name="itemId">Item ID</param>
        /// <param name="def">Item 定义（输出参数）</param>
        /// <returns>是否找到</returns>
        public bool TryGetItem(string itemId, out ItemDefinition def)
        {
            def = null;

            if (byId == null)
            {
                // 资源损坏，已在 OnEnable 中 Error
                return false;
            }

            if (string.IsNullOrWhiteSpace(itemId))
            {
                return false;
            }

            return byId.TryGetValue(itemId, out def);
        }

        /// <summary>
        /// 获取所有 Item 定义
        /// 规范（[C-Runtime-1]）：
        /// - 直接返回 items 数组本体（以 IReadOnlyList<ItemDefinition> 暴露）
        /// - 禁止 Array.AsReadOnly 等包装以避免额外分配
        /// </summary>
        /// <returns>所有 Item 定义（只读）</returns>
        public System.Collections.Generic.IReadOnlyList<ItemDefinition> GetAllItems()
        {
            return items;
        }

        /// <summary>
        /// 从 CastleDB DTO 应用数据（Import 阶段调用）
        /// </summary>
        /// <param name="itemEntries">CastleDB Item Sheet 的所有条目</param>
        public void ApplyFromCastleDb(List<ItemDefinition> itemDefinitions)
        {
            if (itemDefinitions == null)
            {
                Debug.LogError("[ItemCatalog] ApplyFromCastleDb: itemDefinitions is null");
                items = new ItemDefinition[0];
                return;
            }

            // 直接赋值（由 Provider 负责转换 DTO → ItemDefinition）
            items = itemDefinitions.ToArray();

            Debug.Log($"[ItemCatalog] Applied {items.Length} item definitions from CastleDB");

            // 立即触发 OnEnable 重建索引
            OnEnable();
        }

        private void OnValidate()
        {
            // 警告：此资产应由 Import All 生成，不应手动编辑
#if UNITY_EDITOR
            Debug.LogWarning("[ItemCatalog] 此资产由 Tools/CastleDB/Import All 生成，请勿手动编辑。" +
                "如需修改 Item 配置，请在 CastleDB 中编辑 Item Sheet 并重新导入。", this);
#else
            Debug.LogWarning("[ItemCatalog] 此资产由 Tools/CastleDB/Import All 生成，请勿手动编辑。", this);
#endif
        }
    }
}
