using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 敌人Prefab创建向导
/// 自动生成敌人Prefab的标准结构，包括所需的组件和检测区
///
/// 功能：
/// 1. 弹出配置对话框，要求输入敌人名称和类型
/// 2. 自动创建敌人GameObject及其标准结构
/// 3. 为每个检测区创建子物体和Collider2D
/// 4. 保存为Prefab到指定目录
///
/// 使用方式：
/// Tools → Detection Zone → Create Enemy Prefab
/// </summary>
public class CreateEnemyPrefabWizard : EditorWindow
{
    // ===== 配置字段 =====

    private string enemyName = "NewEnemy";
    private List<bool> selectedDetectionRoles = new List<bool>
    {
        true,   // PrimaryAttack
        false,  // SecondaryAttack
        true,   // Cliff
        false,  // Alert
        false,  // Lookout
    };

    private Vector2 scrollPosition = Vector2.zero;

    // ===== 菜单项 =====

    [MenuItem("Tools/Detection Zone/Create Enemy Prefab")]
    public static void ShowWindow()
    {
        GetWindow<CreateEnemyPrefabWizard>("Create Enemy Prefab");
    }

    // ===== GUI绘制 =====

    private void OnGUI()
    {
        GUILayout.Label("敌人Prefab创建向导", EditorStyles.boldLabel);
        GUILayout.Space(10);

        // 敌人名称输入
        GUILayout.Label("基本信息", EditorStyles.boldLabel);
        enemyName = EditorGUILayout.TextField("敌人名称", enemyName);

        if (string.IsNullOrWhiteSpace(enemyName))
        {
            EditorGUILayout.HelpBox("敌人名称不能为空", MessageType.Warning);
        }

        GUILayout.Space(10);

        // 检测区选择
        GUILayout.Label("检测区配置", EditorStyles.boldLabel);
        GUILayout.Label("选择需要创建的检测区：");

        string[] roleNames = new string[]
        {
            "DZ_Attack (主攻击检测)",
            "DZ_Ability (法术检测)",
            "DZ_Cliff (崖边检测)",
            "DZ_Alert (警戒范围)",
            "DZ_Lookout (视野范围)"
        };

        scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.Height(150));

        for (int i = 0; i < selectedDetectionRoles.Count; i++)
        {
            selectedDetectionRoles[i] = EditorGUILayout.Toggle(roleNames[i], selectedDetectionRoles[i]);
        }

        GUILayout.EndScrollView();

        GUILayout.Space(10);

        // 验证至少选择一个检测区
        bool hasDetectionZone = selectedDetectionRoles.Any(x => x);
        if (!hasDetectionZone)
        {
            EditorGUILayout.HelpBox("至少需要选择一个检测区", MessageType.Warning);
        }

        GUILayout.Space(10);

        // 创建按钮
        GUI.enabled = !string.IsNullOrWhiteSpace(enemyName) && hasDetectionZone;

        if (GUILayout.Button("创建Prefab", GUILayout.Height(40)))
        {
            CreateEnemyPrefab();
        }

        GUI.enabled = true;

        GUILayout.Space(10);

        GUILayout.Label("说明", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "此向导将创建一个标准的敌人Prefab，包括：\n" +
            "• 根GameObject (配置脚本)\n" +
            "• DetectionZones容器\n" +
            "• 各个检测区子物体 (DZ_*格式)\n" +
            "• 必需的组件 (Rigidbody2D, Animator, Damageable等)\n\n" +
            "Prefab将保存到：Assets/Resources/Prefabs/Enemy/",
            MessageType.Info
        );
    }

    // ===== 核心功能 =====

    private void CreateEnemyPrefab()
    {
        // 1. 验证目录
        string prefabDir = "Assets/Resources/Prefabs/Enemy";
        if (!AssetDatabase.IsValidFolder(prefabDir))
        {
            Debug.LogError($"目录不存在：{prefabDir}");
            EditorUtility.DisplayDialog("错误", $"目录不存在：{prefabDir}", "确定");
            return;
        }

        // 2. 创建根GameObject
        GameObject enemyRoot = new GameObject(enemyName);
        enemyRoot.name = enemyName;

        // 3. 添加必需的组件
        AddRequiredComponents(enemyRoot);

        // 4. 创建DetectionZones容器
        GameObject detectionZonesContainer = new GameObject("DetectionZones");
        detectionZonesContainer.transform.SetParent(enemyRoot.transform);
        detectionZonesContainer.transform.localPosition = Vector3.zero;

        // 5. 创建各个检测区
        List<DetectionZone> createdZones = new List<DetectionZone>();
        CreateDetectionZones(detectionZonesContainer, createdZones);

        // 6. 配置EnemyAgentBase组件
        ConfigureEnemyAgentBase(enemyRoot, createdZones);

        // 7. 保存为Prefab
        string prefabPath = $"{prefabDir}/{enemyName}.prefab";

        // 检查是否已存在
        if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
        {
            if (!EditorUtility.DisplayDialog("确认", $"Prefab已存在：{prefabPath}，是否覆盖？", "是", "否"))
            {
                DestroyImmediate(enemyRoot);
                return;
            }
        }

        PrefabUtility.SaveAsPrefabAsset(enemyRoot, prefabPath);
        Debug.Log($"✓ 敌人Prefab创建成功：{prefabPath}");

        // 8. 清理场景中的临时GameObject
        DestroyImmediate(enemyRoot);

        // 9. 刷新AssetDatabase
        AssetDatabase.Refresh();

        // 10. 显示成功对话框
        EditorUtility.DisplayDialog(
            "成功",
            $"敌人Prefab创建完成！\n\n路径：{prefabPath}\n\n" +
            $"接下来您需要：\n" +
            $"1. 为Prefab分配EnemyTuningProfile\n" +
            $"2. 调整检测区的大小和位置\n" +
            $"3. 添加Animator Controller\n" +
            $"4. 自定义敌人脚本逻辑",
            "确定"
        );

        Close();
    }

    /// <summary>
    /// 添加必需的组件到敌人根GameObject
    /// </summary>
    private void AddRequiredComponents(GameObject enemyRoot)
    {
        // Rigidbody2D
        var rb2d = enemyRoot.AddComponent<Rigidbody2D>();
        rb2d.gravityScale = 1f;
        rb2d.constraints = RigidbodyConstraints2D.FreezeRotation;

        // CircleCollider2D（作为敌人本身的碰撞体）
        var collider = enemyRoot.AddComponent<CircleCollider2D>();
        collider.radius = 0.4f;

        // Animator
        enemyRoot.AddComponent<Animator>();

        // Damageable
        enemyRoot.AddComponent<Damageable>();

        // SpriteRenderer（可选，用于显示敌人）
        var spriteRenderer = enemyRoot.AddComponent<SpriteRenderer>();
        spriteRenderer.sortingOrder = 1;

        Debug.Log($"已添加必需组件到 {enemyRoot.name}");
    }

    /// <summary>
    /// 根据选择创建检测区子物体
    /// </summary>
    private void CreateDetectionZones(GameObject container, List<DetectionZone> createdZones)
    {
        DetectionZoneBinding.Role[] roles = new[]
        {
            DetectionZoneBinding.Role.PrimaryAttack,
            DetectionZoneBinding.Role.SecondaryAttack,
            DetectionZoneBinding.Role.Cliff,
            DetectionZoneBinding.Role.Alert,
            DetectionZoneBinding.Role.Lookout
        };

        for (int i = 0; i < selectedDetectionRoles.Count; i++)
        {
            if (!selectedDetectionRoles[i])
                continue;

            string zoneName = roles[i] switch
            {
                DetectionZoneBinding.Role.PrimaryAttack => "DZ_Attack",
                DetectionZoneBinding.Role.SecondaryAttack => "DZ_Ability",
                _ => $"DZ_{roles[i]}"
            };
            GameObject zoneGO = new GameObject(zoneName);
            zoneGO.transform.SetParent(container.transform);
            zoneGO.transform.localPosition = Vector3.zero;

            // 添加BoxCollider2D（1x1，IsTrigger=true）
            var boxCollider = zoneGO.AddComponent<BoxCollider2D>();
            boxCollider.size = Vector2.one;
            boxCollider.isTrigger = true;

            // 添加DetectionZone脚本
            var detectionZone = zoneGO.AddComponent<DetectionZone>();
            createdZones.Add(detectionZone);

            Debug.Log($"✓ 创建检测区：{zoneName}");
        }
    }

    /// <summary>
    /// 配置EnemyAgentBase组件（若继承）
    /// </summary>
    private void ConfigureEnemyAgentBase(GameObject enemyRoot, List<DetectionZone> createdZones)
    {
        // 添加EnemyAgentBase的基础配置（如果需要）
        // 注意：由于EnemyAgentBase是抽象类，实际需要子类
        // 这里主要是配置zoneBindings列表

        // 由于EnemyAgentBase是抽象的，这里只做准备
        // 真正的配置需要在Prefab中手动指定脚本类型和绑定

        if (createdZones.Count > 0)
        {
            Debug.Log($"✓ 已创建 {createdZones.Count} 个检测区");
            Debug.Log("✓ 请在Prefab中手动配置：");
            Debug.Log("  1. 添加继承EnemyAgentBase的脚本组件");
            Debug.Log("  2. 将检测区拖拽到zoneBindings列表中");
            Debug.Log("  3. 分配EnemyTuningProfile资源");
        }
    }
}
