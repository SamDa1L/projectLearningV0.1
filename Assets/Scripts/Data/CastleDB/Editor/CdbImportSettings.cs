using UnityEngine;

namespace CastleDB.Editor
{
    /// <summary>
    /// CastleDB 导入设置（Phase 12 - C-Tool-2）
    ///
    /// 职责：
    /// - 提供可配置的 CdbImportRoot 路径
    /// - 支持相对路径（相对 ProjectRoot）或绝对路径
    /// - 通过 Tools/CastleDB/Settings 菜单访问
    /// </summary>
    [CreateAssetMenu(fileName = "CdbImportSettings", menuName = "CastleDB/Import Settings", order = 1)]
    public class CdbImportSettings : ScriptableObject
    {
        /// <summary>
        /// CDB 导入根目录（相对 ProjectRoot 或绝对路径）
        /// 默认：Assets/Resources
        /// Excel 导出以此为基准计算镜像路径
        /// </summary>
        [Tooltip("CDB 导入根目录（相对 ProjectRoot 或绝对路径）\n默认：Assets/Resources\nExcel 导出以此为基准计算镜像路径")]
        public string cdbImportRoot = "Assets/Resources";
    }
}
