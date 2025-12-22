using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 场景实例批量刷新工具（阶段4）
///
/// 功能：
/// - 批量刷新场景中的敌人 Prefab 实例，使其与最新的 Prefab 资产保持一致
/// - 支持选择性刷新（保留场景中的手动覆盖）或强制刷新（完全恢复到 Prefab 状态）
/// - 自动备份场景文件
///
/// 使用方式：
/// Tools → CastleDB → Refresh Scene Instances
/// </summary>
public class SceneInstanceRefresher : EditorWindow
{
    // ===== 配置 =====
    private const string BACKUP_ROOT_DIR = "Logs/SceneRefresh/Backups";

    // ===== 状态 =====
    private Vector2 scrollPosition;
    private List<string> scenePaths = new List<string>();
    private List<bool> selectedScenes = new List<bool>();
    private bool refreshAll = true;
    private bool forceRevert = false; // false = 保留手动覆盖，true = 强制恢复
    private List<RefreshResult> refreshResults = new List<RefreshResult>();

    // ===== 数据结构 =====
    private class RefreshResult
    {
        public string scenePath;
        public int instanceCount;
        public int refreshedCount;
        public List<string> messages = new List<string>();
    }

    // ===== 菜单项 =====

    [MenuItem("Tools/CastleDB/Refresh Scene Instances")]
    public static void ShowWindow()
    {
        var window = GetWindow<SceneInstanceRefresher>("Refresh Scene Instances");
        window.minSize = new Vector2(500, 400);
        window.Initialize();
    }

    // ===== 初始化 =====

    private void Initialize()
    {
        // 查找所有场景文件
        scenePaths.Clear();
        selectedScenes.Clear();

        // 查找所有 .unity 场景
        var sceneGuids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" });
        foreach (var guid in sceneGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            scenePaths.Add(path);
            // 默认选中测试场景和主场景
            bool isDefault = path.Contains("TestEnemy") || path.Contains("GamePlayScene");
            selectedScenes.Add(isDefault);
        }
    }

    // ===== GUI =====

    private void OnGUI()
    {
        GUILayout.Label("场景实例批量刷新工具（阶段4）", EditorStyles.boldLabel);
        GUILayout.Space(10);

        EditorGUILayout.HelpBox(
            "此工具会刷新场景中所有敌人 Prefab 实例，使其与最新的 Prefab 资产保持一致。\n\n" +
            "注意：\n" +
            "• 保留覆盖：保持场景中手动修改的字段（推荐）\n" +
            "• 强制恢复：完全恢复到 Prefab 状态（会丢失手动修改）",
            MessageType.Info);

        GUILayout.Space(10);

        // 选项
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("刷新选项", EditorStyles.boldLabel);
        forceRevert = EditorGUILayout.Toggle("强制恢复（丢失手动修改）", forceRevert);
        refreshAll = EditorGUILayout.Toggle("刷新所有场景", refreshAll);
        EditorGUILayout.EndVertical();

        GUILayout.Space(10);

        // 场景列表
        if (!refreshAll)
        {
            GUILayout.Label("选择要刷新的场景", EditorStyles.boldLabel);
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(200));

            for (int i = 0; i < scenePaths.Count; i++)
            {
                string sceneName = System.IO.Path.GetFileNameWithoutExtension(scenePaths[i]);
                selectedScenes[i] = EditorGUILayout.Toggle(sceneName, selectedScenes[i]);
            }

            EditorGUILayout.EndScrollView();
        }

        GUILayout.Space(10);

        // 操作按钮
        if (GUILayout.Button("开始刷新", GUILayout.Height(30)))
        {
            ExecuteRefresh();
        }

        GUILayout.Space(10);

        // 结果显示
        if (refreshResults.Count > 0)
        {
            GUILayout.Label("刷新结果", EditorStyles.boldLabel);
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(150));

            foreach (var result in refreshResults)
            {
                string sceneName = System.IO.Path.GetFileNameWithoutExtension(result.scenePath);
                EditorGUILayout.LabelField($"{sceneName}: 刷新 {result.refreshedCount}/{result.instanceCount} 个实例");

                if (result.messages.Count > 0)
                {
                    foreach (var msg in result.messages)
                    {
                        EditorGUILayout.LabelField($"  • {msg}", EditorStyles.miniLabel);
                    }
                }
            }

            EditorGUILayout.EndScrollView();
        }
    }

    // ===== 核心功能 =====

    private void ExecuteRefresh()
    {
        refreshResults.Clear();

        // 保存当前场景
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            Debug.LogWarning("[SceneInstanceRefresher] 用户取消了操作");
            return;
        }

        string currentScenePath = EditorSceneManager.GetActiveScene().path;

        try
        {
            Debug.Log("\n========== 场景实例刷新开始 ==========\n");

            // 确定要刷新的场景
            var scenesToRefresh = new List<string>();
            if (refreshAll)
            {
                scenesToRefresh.AddRange(scenePaths);
            }
            else
            {
                for (int i = 0; i < scenePaths.Count; i++)
                {
                    if (selectedScenes[i])
                    {
                        scenesToRefresh.Add(scenePaths[i]);
                    }
                }
            }

            if (scenesToRefresh.Count == 0)
            {
                Debug.LogWarning("[SceneInstanceRefresher] 没有选择任何场景");
                return;
            }

            // 备份场景
            BackupScenes(scenesToRefresh);

            // 刷新每个场景
            foreach (var scenePath in scenesToRefresh)
            {
                RefreshSceneInstances(scenePath);
            }

            // 恢复原始场景
            if (!string.IsNullOrEmpty(currentScenePath))
            {
                EditorSceneManager.OpenScene(currentScenePath);
            }

            Debug.Log("\n========== 场景实例刷新完成 ==========\n");

            // 显示摘要
            int totalInstances = refreshResults.Sum(r => r.instanceCount);
            int totalRefreshed = refreshResults.Sum(r => r.refreshedCount);

            EditorUtility.DisplayDialog(
                "刷新完成",
                $"刷新完成！\n\n" +
                $"场景数: {scenesToRefresh.Count}\n" +
                $"实例总数: {totalInstances}\n" +
                $"刷新数: {totalRefreshed}",
                "确定");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[SceneInstanceRefresher] 刷新失败: {ex.Message}\n{ex.StackTrace}");
            EditorUtility.DisplayDialog("错误", $"刷新失败：{ex.Message}", "确定");
        }
        finally
        {
            Repaint();
        }
    }

    private void RefreshSceneInstances(string scenePath)
    {
        var result = new RefreshResult { scenePath = scenePath };

        try
        {
            // 打开场景
            var scene = EditorSceneManager.OpenScene(scenePath);
            Debug.Log($"[SceneInstanceRefresher] 刷新场景: {scenePath}");

            // 查找所有带 EnemyAgentBase 的 GameObject
            var rootObjects = scene.GetRootGameObjects();
            var enemyInstances = new List<GameObject>();

            foreach (var root in rootObjects)
            {
                var enemies = root.GetComponentsInChildren<EnemyAgentBase>(true);
                foreach (var enemy in enemies)
                {
                    // 只处理 Prefab 实例
                    if (PrefabUtility.IsPartOfPrefabInstance(enemy.gameObject))
                    {
                        enemyInstances.Add(enemy.gameObject);
                    }
                }
            }

            result.instanceCount = enemyInstances.Count;
            Debug.Log($"[SceneInstanceRefresher] 找到 {result.instanceCount} 个敌人实例");

            // 刷新每个实例
            foreach (var instance in enemyInstances)
            {
                try
                {
                    string instanceName = instance.name;

                    if (forceRevert)
                    {
                        // 强制恢复：完全恢复到 Prefab 状态
                        PrefabUtility.RevertPrefabInstance(instance, InteractionMode.AutomatedAction);
                        result.refreshedCount++;
                        result.messages.Add($"强制恢复: {instanceName}");
                    }
                    else
                    {
                        // 保留覆盖：同步 Prefab 变更，但保留场景实例的手动覆盖
                        var source = PrefabUtility.GetCorrespondingObjectFromSource(instance);
                        if (source != null)
                        {
                            // 检查是否有覆盖
                            var overrides = PrefabUtility.GetObjectOverrides(instance);
                            if (overrides == null || overrides.Count == 0)
                            {
                                // 没有覆盖，直接 revert 使其与 Prefab 一致
                                PrefabUtility.RevertPrefabInstance(instance, InteractionMode.AutomatedAction);
                                result.refreshedCount++;
                                result.messages.Add($"刷新（无覆盖）: {instanceName}");
                            }
                            else
                            {
                                // 有覆盖时的正确做法：
                                // Unity 会在场景重新加载时自动同步 Prefab 变更并保留覆盖
                                // 这里标记需要重新加载，稍后统一处理
                                result.refreshedCount++;
                                result.messages.Add($"标记重载（保留 {overrides.Count} 个覆盖）: {instanceName}");
                            }
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[SceneInstanceRefresher] 刷新实例失败: {instance.name}, {ex.Message}");
                    result.messages.Add($"失败: {instance.name} - {ex.Message}");
                }
            }

            // 保存场景
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[SceneInstanceRefresher] 场景已保存: {scenePath}");

            // 如果是"保留覆盖"模式且有实例被标记需要同步，重新加载场景
            // 这样 Unity 会自动同步 Prefab 变更并保留覆盖
            if (!forceRevert && result.refreshedCount > 0)
            {
                Debug.Log($"[SceneInstanceRefresher] 重新加载场景以同步 Prefab 变更（保留覆盖）");
                EditorSceneManager.CloseScene(scene, true);
                scene = EditorSceneManager.OpenScene(scenePath);
                result.messages.Add("已重新加载场景（同步 Prefab 变更）");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[SceneInstanceRefresher] 处理场景失败: {scenePath}, {ex.Message}");
            result.messages.Add($"错误: {ex.Message}");
        }

        refreshResults.Add(result);
    }

    // ===== 备份 =====

    private void BackupScenes(List<string> scenePaths)
    {
        try
        {
            string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string backupDir = System.IO.Path.Combine(BACKUP_ROOT_DIR, $"Backup_{timestamp}");
            string projectRoot = System.IO.Path.GetDirectoryName(Application.dataPath);
            string absBackupDir = System.IO.Path.Combine(projectRoot, backupDir);

            if (!System.IO.Directory.Exists(absBackupDir))
            {
                System.IO.Directory.CreateDirectory(absBackupDir);
            }

            int backupCount = 0;
            foreach (var scenePath in scenePaths)
            {
                string absSrcPath = System.IO.Path.Combine(projectRoot, scenePath);
                string relativePath = scenePath.Replace("Assets/", "");
                string destPath = System.IO.Path.Combine(absBackupDir, relativePath);

                string destDir = System.IO.Path.GetDirectoryName(destPath);
                if (!System.IO.Directory.Exists(destDir))
                {
                    System.IO.Directory.CreateDirectory(destDir);
                }

                System.IO.File.Copy(absSrcPath, destPath, true);
                backupCount++;
            }

            Debug.Log($"[SceneInstanceRefresher] 已备份 {backupCount} 个场景到 {backupDir}");
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[SceneInstanceRefresher] 备份失败: {ex.Message}");
        }
    }
}
