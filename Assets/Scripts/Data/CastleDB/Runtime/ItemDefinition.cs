using System;
using UnityEngine;

namespace CastleDB.Runtime
{
    /// <summary>
    /// Item 消耗效果结构体
    /// 0.4 版本仅支持 heal 效果
    ///
    /// 规范（[C-Data-3]）：
    /// - consumeEffectJson 必须是 JSON 对象 {...}
    /// - 允许键：heal (number)
    /// - 多余键 Warning 不阻断
    /// - 非对象/类型不符硬失败
    /// </summary>
    [System.Serializable]
    public struct ItemConsumeEffect
    {
        /// <summary>
        /// 治疗量（整数）
        /// 默认值 0 表示无治疗效果
        /// </summary>
        public int heal;

        public ItemConsumeEffect(int heal)
        {
            this.heal = heal;
        }

        public override string ToString()
        {
            return $"ConsumeEffect[heal={heal}]";
        }
    }

    /// <summary>
    /// Item 定义（运行时数据结构）
    /// 对应导入产物 ItemCatalog.asset 中的单个 Item 条目
    ///
    /// 规范（[C-Data-4]）：
    /// - 由 ItemDataProvider.Import() 生成
    /// - 存储在 ItemCatalog.asset 中
    /// - 通过 CastleDbService.TryGetItem(itemId, out ItemDefinition) 查询
    /// </summary>
    [System.Serializable]
    public class ItemDefinition
    {
        /// <summary>
        /// Item 唯一标识符（非空唯一）
        /// </summary>
        [SerializeField]
        public string id;

        /// <summary>
        /// 显示名称
        /// </summary>
        [SerializeField]
        public string displayName;

        /// <summary>
        /// Item 类型（Ability/Consumable/Material）
        /// </summary>
        [SerializeField]
        public ItemType itemType;

        /// <summary>
        /// 图标路径（Resources 相对路径，无扩展名）
        /// 例如："UI/Items/Potion_Red"
        /// </summary>
        [SerializeField]
        public string icon;

        /// <summary>
        /// 能力 ID（itemType=Ability 时必填，引用 PlayerAbility.id）
        /// </summary>
        [SerializeField]
        public string abilityId;

        /// <summary>
        /// 遗物 ID（itemType=Relic 时必填，引用 Relic.id）
        /// </summary>
        [SerializeField]
        public string relicId;

        /// <summary>
        /// 最大堆叠数量
        /// </summary>
        [SerializeField]
        public int maxStack;

        /// <summary>
        /// 消耗效果（从 consumeEffectJson 解析而来）
        /// </summary>
        [SerializeField]
        public ItemConsumeEffect consumeEffect;

        /// <summary>
        /// 原始 consumeEffectJson（可选，用于调试）
        /// Runtime 优先使用结构化的 consumeEffect 字段
        /// </summary>
        [SerializeField]
        public string consumeEffectRawJson;

        /// <summary>
        /// UI 标签（可选）
        /// </summary>
        [SerializeField]
        public string uiTag;

        public override string ToString()
        {
            string abilityStr = !string.IsNullOrEmpty(abilityId) ? $", abilityId={abilityId}" : "";
            string relicStr = !string.IsNullOrEmpty(relicId) ? $", relicId={relicId}" : "";
            return $"Item[id={id}, displayName={displayName}, type={itemType}, icon={icon}{abilityStr}{relicStr}, maxStack={maxStack}]";
        }
    }

    /// <summary>
    /// Item 表条目（对应 CastleDB 中 Item Sheet 的一行数据）
    /// 用于反序列化 Item.cdb 的 JSON 数据
    ///
    /// 规范（[C-Data-2]）：
    /// - id: Identifier（非空唯一）
    /// - displayName: string
    /// - itemType: "Ability"|"Consumable"|"Material"|"Relic"（字符串，导入时转换为枚举）
    /// - icon: Resources 相对路径（无扩展名）
    /// - maxStack: int
    /// - abilityId: string（itemType=Ability 必填）
    /// - relicId: string（itemType=Relic 必填）
    /// - consumeEffectJson: string（JSON 对象）
    /// - uiTag: string（可选）
    /// </summary>
    [System.Serializable]
    public class ItemEntry
    {
        [SerializeField]
        public string id;

        [SerializeField]
        public string displayName;

        /// <summary>
        /// CastleDB 中存储为字符串："Ability"|"Consumable"|"Material"|"Relic"
        /// 导入时会转换为 ItemType 枚举
        /// </summary>
        [SerializeField]
        public string itemType;

        [SerializeField]
        public string icon;

        [SerializeField]
        public int maxStack;

        [SerializeField]
        public string abilityId;

        [SerializeField]
        public string relicId;

        [SerializeField]
        public string consumeEffectJson;

        [SerializeField]
        public string uiTag;

        public override string ToString()
        {
            return $"ItemEntry[id={id}, displayName={displayName}, itemType={itemType}, icon={icon}]";
        }
    }
}
