using UnityEngine;
using UnityEditor;
using System;
using System.IO;
using System.Text;

public class ConsoleExporter
{
    private static StringBuilder logBuffer = new StringBuilder();
    private static string logFilePath;
    private static bool isInitialized = false;

    [InitializeOnLoadMethod]
    private static void Initialize()
    {
        if (isInitialized) return;

        isInitialized = true;
        logFilePath = Path.Combine(Application.dataPath, "Logs", "console_" + System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".log");

        // 确保Logs文件夹存在
        string logsDir = Path.Combine(Application.dataPath, "Logs");
        if (!Directory.Exists(logsDir))
        {
            Directory.CreateDirectory(logsDir);
        }

        // 注册日志回调
        Application.logMessageReceived += OnLogMessageReceived;

        Debug.Log($"Console Exporter initialized. Logs will be saved to: {logFilePath}");
    }

    private static void OnLogMessageReceived(string logString, string stackTrace, LogType type)
    {
        string timestamp = System.DateTime.Now.ToString("HH:mm:ss.fff");
        string logEntry = $"[{timestamp}] [{type}] {logString}";

        if (!string.IsNullOrEmpty(stackTrace) && type != LogType.Log)
        {
            logEntry += $"\n{stackTrace}";
        }

        logBuffer.AppendLine(logEntry);

        // 每10条日志写入一次文件，避免频繁I/O
        if (logBuffer.Length > 5000)
        {
            FlushLogs();
        }
    }

    private static void FlushLogs()
    {
        if (logBuffer.Length == 0) return;

        try
        {
            File.AppendAllText(logFilePath, logBuffer.ToString());
            logBuffer.Clear();
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to write logs: {e.Message}");
        }
    }

    [MenuItem("Tools/Console Exporter/Flush Logs Now")]
    private static void FlushLogsManually()
    {
        FlushLogs();
        Debug.Log($"Logs flushed to: {logFilePath}");
    }

    [MenuItem("Tools/Console Exporter/Open Logs Folder")]
    private static void OpenLogsFolder()
    {
        string logsDir = Path.Combine(Application.dataPath, "Logs");
        if (Directory.Exists(logsDir))
        {
            EditorUtility.RevealInFinder(logsDir);
        }
    }

    private static void OnDestroy()
    {
        FlushLogs();
        Application.logMessageReceived -= OnLogMessageReceived;
    }
}
