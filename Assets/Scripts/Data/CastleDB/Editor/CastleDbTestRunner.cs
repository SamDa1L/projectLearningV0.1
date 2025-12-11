using UnityEditor;
using UnityEngine;

namespace CastleDB.Editor
{
    /// <summary>
    /// CastleDB 测试运行器
    /// 提供菜单快捷方式和工具函数
    /// </summary>
    public class CastleDbTestRunner
    {
        [MenuItem("Tools/CastleDB/Reimport All")]
        public static void ReimportAll()
        {
            Debug.Log("[CastleDbTestRunner] 重新导入所有资源...");
            AssetDatabase.Refresh();
            Debug.Log("[CastleDbTestRunner] 重新导入完成");
        }

        [MenuItem("Tools/CastleDB/Open Test Runner")]
        public static void OpenTestRunner()
        {
            Debug.Log("[CastleDbTestRunner] 打开 Test Runner 窗口...");
            EditorWindow.GetWindow(System.Type.GetType("UnityEditor.TestTools.TestRunner.TestRunnerWindow,UnityEditor.TestRunner"));
        }

        [MenuItem("Tools/CastleDB/Open Logs Folder")]
        public static void OpenLogsFolder()
        {
            string logsPath = System.IO.Path.Combine(Application.persistentDataPath, "..", "Logs");
            logsPath = System.IO.Path.GetFullPath(logsPath);

            if (System.IO.Directory.Exists(logsPath))
            {
                System.Diagnostics.Process.Start(logsPath);
                Debug.Log($"[CastleDbTestRunner] 打开日志文件夹: {logsPath}");
            }
            else
            {
                Debug.LogWarning($"[CastleDbTestRunner] 日志文件夹不存在: {logsPath}");
            }
        }
    }
}
