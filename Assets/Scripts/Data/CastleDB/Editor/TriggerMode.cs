namespace CastleDB.Editor
{
    /// <summary>
    /// Excel 导出触发模式
    /// </summary>
    public enum TriggerMode
    {
        /// <summary>
        /// Import All 触发：导出所有已导入的 .cdb 文件
        /// </summary>
        All,

        /// <summary>
        /// Import This File 触发：仅导出当前文件
        /// </summary>
        ThisFile
    }
}
