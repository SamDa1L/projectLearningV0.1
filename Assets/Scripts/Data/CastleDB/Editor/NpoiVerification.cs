using UnityEditor;
using UnityEngine;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace CastleDB.Editor
{
    /// <summary>
    /// NPOI 验证脚本 - 验证 NPOI DLL 引入成功
    /// </summary>
    public class NpoiVerification
    {
        [MenuItem("Tools/CastleDB/Verify NPOI")]
        public static void VerifyNPOI()
        {
            try
            {
                // 创建一个简单的 workbook 来验证 NPOI 可用
                IWorkbook workbook = new XSSFWorkbook();
                ISheet sheet = workbook.CreateSheet("Test");
                IRow row = sheet.CreateRow(0);
                ICell cell = row.CreateCell(0);
                cell.SetCellValue("NPOI is working!");

                Debug.Log("[NPOI Verification] ✅ NPOI DLL 引入成功！可以正常使用 NPOI.SS.UserModel");
                Debug.Log($"[NPOI Verification] Workbook type: {workbook.GetType().FullName}");
                Debug.Log($"[NPOI Verification] Sheet count: {workbook.NumberOfSheets}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[NPOI Verification] ❌ NPOI 验证失败: {e}");
            }
        }
    }
}
