using UnityEngine;
using UnityEditor;
using System;
using System.Reflection;

namespace CastleDB.Editor
{
    /// <summary>
    /// NPOI DLL 依赖验证工具
    /// 用于验证 NPOI .NET Framework 4.5 版本及其依赖是否正确安装
    /// </summary>
    public static class NpoiDllVerification
    {
        [MenuItem("Tools/CastleDB/Verify NPOI Dependencies")]
        public static void VerifyNpoiDependencies()
        {
            Debug.Log("[NPOI Verification] 开始验证 NPOI DLL 依赖...");
            bool allValid = true;

            // 验证 BouncyCastle.Crypto.dll
            allValid &= VerifyAssembly("BouncyCastle.Crypto", "Org.BouncyCastle.Crypto.CipherKeyGenerator");

            // 验证 ICSharpCode.SharpZipLib.dll
            allValid &= VerifyAssembly("ICSharpCode.SharpZipLib", "ICSharpCode.SharpZipLib.Zip.ZipFile");

            // 验证 NPOI.dll
            allValid &= VerifyAssembly("NPOI", "NPOI.HSSF.UserModel.HSSFWorkbook");

            // 验证 NPOI.OOXML.dll
            allValid &= VerifyAssembly("NPOI.OOXML", "NPOI.XSSF.UserModel.XSSFWorkbook");

            // 验证 NPOI.OpenXml4Net.dll
            allValid &= VerifyAssembly("NPOI.OpenXml4Net", "NPOI.OpenXml4Net.OPC.OPCPackage");

            // 验证 NPOI.OpenXmlFormats.dll
            allValid &= VerifyAssembly("NPOI.OpenXmlFormats", "NPOI.OpenXmlFormats.Spreadsheet.CT_Workbook");

            if (allValid)
            {
                Debug.Log("<color=green>[NPOI Verification] ✓ 所有 NPOI DLL 及依赖验证通过！</color>");
                EditorUtility.DisplayDialog("NPOI 验证成功", "所有 NPOI DLL 及依赖验证通过！\n\n您可以安全地使用 Excel 导出功能。", "确定");
            }
            else
            {
                Debug.LogError("[NPOI Verification] ✗ 存在依赖问题，请检查上述错误信息。");
                EditorUtility.DisplayDialog("NPOI 验证失败", "存在依赖问题，请检查 Console 日志。", "确定");
            }
        }

        private static bool VerifyAssembly(string assemblyName, string testTypeName)
        {
            try
            {
                // 尝试加载程序集
                Assembly assembly = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name == assemblyName)
                    {
                        assembly = asm;
                        break;
                    }
                }

                if (assembly == null)
                {
                    Debug.LogError($"[NPOI Verification] ✗ 程序集未加载: {assemblyName}");
                    return false;
                }

                // 尝试加载测试类型
                Type testType = assembly.GetType(testTypeName);
                if (testType == null)
                {
                    Debug.LogError($"[NPOI Verification] ✗ 类型未找到: {testTypeName} (在程序集 {assemblyName} 中)");
                    return false;
                }

                // 获取程序集版本
                string version = assembly.GetName().Version.ToString();
                Debug.Log($"[NPOI Verification] ✓ {assemblyName} (v{version}) - 已加载并验证");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NPOI Verification] ✗ 验证失败: {assemblyName}\n异常: {ex.Message}");
                return false;
            }
        }
    }
}
