using UnityEngine;
using UnityEditor;
using System.IO;

/// <summary>
/// CastleDB 文件监听服务（阶段4）
///
/// 功能：
/// - 自动监听 CastleDB 文件（.cdb）的变化
/// - 检测到变化后提示用户运行 Import All
/// - 支持启用/禁用监听
/// - 失败时自动降级为手动模式
///
/// 使用方式：
/// Tools → CastleDB → Toggle Auto Watch
/// </summary>
[InitializeOnLoad]
public class CastleDbWatchService
{
    // ===== 配置 =====
    // 0.3 版本：监听整个 Data 目录（排除 CastleDbDemo）
    private const string CASTLEDB_WATCH_DIR = "Assets/Resources/Data";
    private const string WATCH_ENABLED_KEY = "CastleDbWatchService_Enabled";
    private const string AUTO_CHAIN_KEY = "CastleDbWatchService_AutoChain";  // 自动执行完整链路
    private const string LOG_FILE = "Logs/CastleDbWatch.log";

    // ===== 状态 =====
    private static FileSystemWatcher watcher;
    private static bool isEnabled = false;
    private static bool autoChainEnabled = false;  // 是否自动执行 Import→Sync→Scene 完整链路
    private static bool isWatcherActive = false;
    private static string lastKnownHash = "";
    private static double lastChangeTime = 0;
    private static double changeDebounceTime = 2.0; // 2秒防抖

    // ===== 静态构造函数（自动启动） =====

    static CastleDbWatchService()
    {
        // 加载上次的启用状态
        isEnabled = EditorPrefs.GetBool(WATCH_ENABLED_KEY, false);
        autoChainEnabled = EditorPrefs.GetBool(AUTO_CHAIN_KEY, false);

        if (isEnabled)
        {
            EditorApplication.delayCall += () =>
            {
                StartWatching();
            };
        }

        // 监听编辑器更新
        EditorApplication.update += OnEditorUpdate;
    }

    // ===== 菜单项 =====

    [MenuItem("Tools/CastleDB/Toggle Auto Watch")]
    public static void ToggleAutoWatch()
    {
        isEnabled = !isEnabled;
        EditorPrefs.SetBool(WATCH_ENABLED_KEY, isEnabled);

        if (isEnabled)
        {
            StartWatching();
            Debug.Log("[CastleDbWatchService] 自动监听已启用");
            EditorUtility.DisplayDialog(
                "自动监听已启用",
                "CastleDB 文件监听已启用。\n\n" +
                "当检测到 .cdb 文件变化时，会自动提示你运行 Import All。\n\n" +
                "注意：如果文件监听权限受限，会自动降级为手动模式。",
                "确定");
        }
        else
        {
            StopWatching();
            Debug.Log("[CastleDbWatchService] 自动监听已禁用");
            EditorUtility.DisplayDialog(
                "自动监听已禁用",
                "CastleDB 文件监听已禁用。\n\n" +
                "你需要手动运行 Import All 来导入数据。",
                "确定");
        }
    }

    [MenuItem("Tools/CastleDB/Toggle Auto Watch", true)]
    public static bool ToggleAutoWatchValidate()
    {
        Menu.SetChecked("Tools/CastleDB/Toggle Auto Watch", isEnabled);
        return true;
    }

    [MenuItem("Tools/CastleDB/Toggle Auto Chain (Import+Sync+Refresh)")]
    public static void ToggleAutoChain()
    {
        autoChainEnabled = !autoChainEnabled;
        EditorPrefs.SetBool(AUTO_CHAIN_KEY, autoChainEnabled);

        if (autoChainEnabled)
        {
            Debug.Log("[CastleDbWatchService] 自动完整链路已启用（Import→Sync→Scene Refresh）");
            EditorUtility.DisplayDialog(
                "自动完整链路已启用",
                "CastleDB 文件变化时将自动执行：\n\n" +
                "1. Import All\n" +
                "2. Sync NPC Prefabs\n" +
                "3. Refresh Scene Instances\n\n" +
                "注意：这是完全自动化模式，适合快速迭代。\n" +
                "如果需要手动控制每一步，请关闭此选项。",
                "确定");
        }
        else
        {
            Debug.Log("[CastleDbWatchService] 自动完整链路已禁用（仅提示）");
            EditorUtility.DisplayDialog(
                "自动完整链路已禁用",
                "CastleDB 文件变化时将只提示你手动执行操作。",
                "确定");
        }
    }

    [MenuItem("Tools/CastleDB/Toggle Auto Chain (Import+Sync+Refresh)", true)]
    public static bool ToggleAutoChainValidate()
    {
        Menu.SetChecked("Tools/CastleDB/Toggle Auto Chain (Import+Sync+Refresh)", autoChainEnabled);
        return true;
    }

    // ===== 核心功能 =====

    private static void StartWatching()
    {
        if (isWatcherActive)
        {
            Debug.LogWarning("[CastleDbWatchService] 监听器已经在运行");
            return;
        }

        try
        {
            // 0.3 版本：监听整个 Data 目录（排除 CastleDbDemo）
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string fullPath = Path.Combine(projectRoot, CASTLEDB_WATCH_DIR);

            if (!Directory.Exists(fullPath))
            {
                Debug.LogWarning($"[CastleDbWatchService] CastleDB 数据目录不存在: {fullPath}");
                WriteLog($"启动失败：目录不存在 {fullPath}");
                return;
            }

            // 创建 FileSystemWatcher 监听 .cdb 文件
            watcher = new FileSystemWatcher(fullPath, "*.cdb");
            watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size;
            watcher.IncludeSubdirectories = true;
            watcher.Changed += OnFileChanged;
            watcher.EnableRaisingEvents = true;

            isWatcherActive = true;
            Debug.Log($"[CastleDbWatchService] 开始监听: {CASTLEDB_WATCH_DIR}/**/*.cdb (排除 CastleDbDemo)");
            WriteLog($"开始监听: {CASTLEDB_WATCH_DIR}/**/*.cdb");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[CastleDbWatchService] 启动监听失败: {ex.Message}");
            WriteLog($"启动失败: {ex.Message}");

            // 自动降级为手动模式
            isEnabled = false;
            EditorPrefs.SetBool(WATCH_ENABLED_KEY, false);
            isWatcherActive = false;

            EditorUtility.DisplayDialog(
                "监听启动失败",
                $"无法启动 CastleDB 文件监听。\n\n" +
                $"错误：{ex.Message}\n\n" +
                $"已自动降级为手动模式。请手动运行 Import All。",
                "确定");
        }
    }

    private static void StopWatching()
    {
        if (watcher != null)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Changed -= OnFileChanged;
            watcher.Dispose();
            watcher = null;
        }

        isWatcherActive = false;
        Debug.Log("[CastleDbWatchService] 停止监听");
        WriteLog("停止监听");
    }

    private static void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        // 0.3 版本：过滤 CastleDbDemo 目录
        string normalizedPath = e.FullPath.Replace("\\", "/");
        if (normalizedPath.Contains("/CastleDbDemo/"))
        {
            WriteLog($"忽略 Legacy 文件变化: {e.FullPath}");
            return;
        }

        // 记录变化时间（用于防抖）
        lastChangeTime = EditorApplication.timeSinceStartup;

        WriteLog($"检测到文件变化: {e.ChangeType}, {e.FullPath}");
    }

    private static void OnEditorUpdate()
    {
        // 防抖处理：只有在最后一次变化后的指定时间才触发
        if (lastChangeTime > 0 &&
            EditorApplication.timeSinceStartup - lastChangeTime >= changeDebounceTime)
        {
            lastChangeTime = 0; // 重置

            // 0.3 版本：检测到任何 .cdb 文件变化后触发 Import All
            WriteLog($"文件变化稳定，提示用户导入");

            // 提示用户
            EditorApplication.delayCall += () =>
            {
                PromptUserToImport();
            };
        }
    }

    private static void PromptUserToImport()
    {
        if (autoChainEnabled)
        {
            // 自动完整链路模式：直接执行 Import→Sync→Refresh
            Debug.Log("[CastleDbWatchService] 检测到文件变化，自动执行完整链路（Import→Sync→Refresh）...");
            WriteLog("检测到文件变化，自动执行完整链路");

            EditorApplication.delayCall += () =>
            {
                ExecuteAutoChain();
            };
        }
        else
        {
            // 手动模式：提示用户
            bool shouldImport = EditorUtility.DisplayDialog(
                "CastleDB 文件已更新",
                "检测到 CastleDB 文件发生了变化。\n\n" +
                "是否立即运行 Import All？",
                "立即导入",
                "稍后手动导入"
            );

            if (shouldImport)
            {
                Debug.Log("[CastleDbWatchService] 用户选择立即导入，正在执行 Import All...");
                WriteLog("用户选择立即导入");
                EditorApplication.ExecuteMenuItem("Tools/CastleDB/Import All");
            }
            else
            {
                Debug.Log("[CastleDbWatchService] 用户选择稍后手动导入");
                WriteLog("用户选择稍后手动导入");
            }
        }
    }

    /// <summary>
    /// 执行自动完整链路：Import→Sync→Refresh
    /// </summary>
    private static void ExecuteAutoChain()
    {
        try
        {
            Debug.Log("[CastleDbWatchService] ========== 开始自动完整链路 ==========");
            WriteLog("开始自动完整链路");

            // 第1步：Import All
            Debug.Log("[CastleDbWatchService] [1/3] 执行 Import All...");
            WriteLog("[1/3] 执行 Import All");
            EditorApplication.ExecuteMenuItem("Tools/CastleDB/Import All");

            // 延迟执行后续步骤，等待 Import 完成
            EditorApplication.delayCall += () =>
            {
                // 第2步：Sync NPC Prefabs
                Debug.Log("[CastleDbWatchService] [2/3] 执行 Sync NPC Prefabs...");
                WriteLog("[2/3] 执行 Sync NPC Prefabs");

                // 注意：这里需要通过代码调用 Sync，而不是 ExecuteMenuItem
                // 因为 Sync 工具是窗口，需要程序化调用
                ExecuteSyncPrefabs();

                // 延迟执行第3步
                EditorApplication.delayCall += () =>
                {
                    // 第3步：Refresh Scene Instances
                    Debug.Log("[CastleDbWatchService] [3/3] 执行 Refresh Scene Instances...");
                    WriteLog("[3/3] 执行 Refresh Scene Instances");

                    // 同样需要程序化调用
                    ExecuteRefreshScenes();

                    Debug.Log("[CastleDbWatchService] ========== 自动完整链路完成 ==========");
                    WriteLog("自动完整链路完成");
                };
            };
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[CastleDbWatchService] 自动完整链路执行失败: {ex.Message}");
            WriteLog($"自动完整链路失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 程序化执行 Sync Prefabs（无需打开窗口）
    /// </summary>
    private static void ExecuteSyncPrefabs()
    {
        try
        {
            // 反射调用 CastleDbPrefabSyncer 的同步逻辑
            // 注意：这需要 CastleDbPrefabSyncer 提供静态 API
            // 如果没有静态 API，则需要打开窗口或修改 CastleDbPrefabSyncer
            var syncerType = System.Type.GetType("CastleDbPrefabSyncer");
            if (syncerType != null)
            {
                // 尝试调用静态方法（如果存在）
                var method = syncerType.GetMethod("ExecuteSyncStatic", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (method != null)
                {
                    method.Invoke(null, null);
                    Debug.Log("[CastleDbWatchService] Sync Prefabs 完成");
                }
                else
                {
                    // 降级方案：打开窗口
                    Debug.LogWarning("[CastleDbWatchService] CastleDbPrefabSyncer 未提供静态 API，降级为打开窗口");
                    EditorApplication.ExecuteMenuItem("Tools/CastleDB/Sync NPC Prefabs");
                }
            }
            else
            {
                Debug.LogWarning("[CastleDbWatchService] 未找到 CastleDbPrefabSyncer 类型");
                EditorApplication.ExecuteMenuItem("Tools/CastleDB/Sync NPC Prefabs");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[CastleDbWatchService] Sync Prefabs 失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 程序化执行 Refresh Scene Instances（无需打开窗口）
    /// </summary>
    private static void ExecuteRefreshScenes()
    {
        try
        {
            // 反射调用 SceneInstanceRefresher 的刷新逻辑
            var refresherType = System.Type.GetType("SceneInstanceRefresher");
            if (refresherType != null)
            {
                var method = refresherType.GetMethod("ExecuteRefreshStatic", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (method != null)
                {
                    method.Invoke(null, null);
                    Debug.Log("[CastleDbWatchService] Refresh Scene Instances 完成");
                }
                else
                {
                    // 降级方案：打开窗口
                    Debug.LogWarning("[CastleDbWatchService] SceneInstanceRefresher 未提供静态 API，降级为打开窗口");
                    EditorApplication.ExecuteMenuItem("Tools/CastleDB/Refresh Scene Instances");
                }
            }
            else
            {
                Debug.LogWarning("[CastleDbWatchService] 未找到 SceneInstanceRefresher 类型");
                EditorApplication.ExecuteMenuItem("Tools/CastleDB/Refresh Scene Instances");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[CastleDbWatchService] Refresh Scene Instances 失败: {ex.Message}");
        }
    }

    // ===== 工具方法 =====

    private static string ComputeFileHash(string filePath)
    {
        try
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                using (var stream = File.OpenRead(filePath))
                {
                    byte[] hash = md5.ComputeHash(stream);
                    return System.BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[CastleDbWatchService] 计算文件哈希失败: {ex.Message}");
            return "";
        }
    }

    private static void WriteLog(string message)
    {
        try
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string absLogPath = Path.Combine(projectRoot, LOG_FILE);

            string logDir = Path.GetDirectoryName(absLogPath);
            if (!Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir);
            }

            string logEntry = $"[{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n";
            File.AppendAllText(absLogPath, logEntry);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[CastleDbWatchService] 写入日志失败: {ex.Message}");
        }
    }
}
