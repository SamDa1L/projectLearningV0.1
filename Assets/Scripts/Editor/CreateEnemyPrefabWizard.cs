using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 敌人 Prefab 创建向导（Phase 4）
/// - 目标：让新建敌人 Prefab“生成后可直接使用”，而不是只生成骨架。
/// - 产物：Prefab + AnimatorController + DetectionZones + zoneBindings + 默认事件绑定。
/// </summary>
public class CreateEnemyPrefabWizard : EditorWindow
{
    private const string PrefabRootDir = "Assets/Resources/Prefabs/Enemy";
    private const string AnimatorTemplatePath = "Assets/Resources/Prefabs/Enemy/KnightEnemy/AC_Knight.controller";
    private const string GroundControllerTemplatePrefabPath = "Assets/Resources/Prefabs/Enemy/KnightEnemy/KnightEnemy.prefab";

    private string enemyName = "NewEnemy";

    private bool createPrimaryAttackZone = true;
    private bool createSecondaryAttackZone = true;
    private bool createCliffZone = true;
    private bool createAlertZone = false;
    private bool createLookoutZone = false;

    private Vector2 scrollPosition = Vector2.zero;

    [MenuItem("Tools/Detection Zone/Create Enemy Prefab")]
    public static void ShowWindow()
    {
        GetWindow<CreateEnemyPrefabWizard>("Create Enemy Prefab");
    }

    private void OnGUI()
    {
        GUILayout.Label("敌人 Prefab 创建向导", EditorStyles.boldLabel);
        GUILayout.Space(8);

        enemyName = EditorGUILayout.TextField("敌人名称", enemyName);
        if (!IsEnemyNameValid(enemyName, out string nameError))
        {
            EditorGUILayout.HelpBox(nameError, MessageType.Warning);
        }

        GUILayout.Space(8);
        GUILayout.Label("检测区配置", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Phase 4：默认生成可直接使用的地面敌人模板（NpcGroundController + AnimatorController + DetectionZones + zoneBindings + 默认事件绑定）。\n" +
            "注意：DZ_Wall 为必选（墙壁检测）。",
            MessageType.Info);

        scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.Height(150));
        createPrimaryAttackZone = EditorGUILayout.Toggle("DZ_Attack（主攻击检测）", createPrimaryAttackZone);
        createSecondaryAttackZone = EditorGUILayout.Toggle("DZ_Ability（法术检测）", createSecondaryAttackZone);
        createCliffZone = EditorGUILayout.Toggle("DZ_Cliff（悬崖检测）", createCliffZone);

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.Toggle("DZ_Wall（墙壁检测，必选）", true);
        }

        createAlertZone = EditorGUILayout.Toggle("DZ_Alert（警戒范围，可选）", createAlertZone);
        createLookoutZone = EditorGUILayout.Toggle("DZ_Lookout（视野范围，可选）", createLookoutZone);
        GUILayout.EndScrollView();

        if (!createPrimaryAttackZone)
        {
            EditorGUILayout.HelpBox("PrimaryAttack 是 EnemyAgentBase 的最小必需检测区（Plan A）。", MessageType.Warning);
        }

        GUILayout.Space(8);

        bool canCreate = IsEnemyNameValid(enemyName, out _) && createPrimaryAttackZone;
        using (new EditorGUI.DisabledScope(!canCreate))
        {
            if (GUILayout.Button("创建 Prefab", GUILayout.Height(40)))
            {
                CreateEnemyPrefab();
            }
        }

        GUILayout.Space(8);
        GUILayout.Label("输出路径", EditorStyles.boldLabel);

        string normalizedName = (enemyName ?? string.Empty).Trim();
        EditorGUILayout.HelpBox(
            $"生成目录：{PrefabRootDir}/{normalizedName}/\n" +
            $"- {normalizedName}.prefab\n" +
            $"- AC_{normalizedName}.controller",
            MessageType.None);
    }

    private void CreateEnemyPrefab()
    {
        string normalizedName = (enemyName ?? string.Empty).Trim();
        if (!IsEnemyNameValid(normalizedName, out string nameError))
        {
            EditorUtility.DisplayDialog("错误", nameError, "确定");
            return;
        }

        if (!AssetDatabase.IsValidFolder(PrefabRootDir))
        {
            EditorUtility.DisplayDialog("错误", $"目录不存在：{PrefabRootDir}", "确定");
            return;
        }

        EnsureFolder(PrefabRootDir, normalizedName);
        string enemyDir = $"{PrefabRootDir}/{normalizedName}";

        string prefabPath = $"{enemyDir}/{normalizedName}.prefab";
        string animatorPath = $"{enemyDir}/AC_{normalizedName}.controller";

        bool prefabExists = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null;
        bool animatorExists = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(animatorPath) != null;

        if (prefabExists || animatorExists)
        {
            string msg =
                $"已存在同名资源：\n" +
                $"- Prefab: {(prefabExists ? "存在" : "不存在")}\n" +
                $"- AnimatorController: {(animatorExists ? "存在" : "不存在")}\n\n" +
                $"是否删除并重新生成？";
            if (!EditorUtility.DisplayDialog("确认覆盖", msg, "覆盖", "取消"))
            {
                return;
            }

            if (prefabExists)
            {
                AssetDatabase.DeleteAsset(prefabPath);
            }

            if (animatorExists)
            {
                AssetDatabase.DeleteAsset(animatorPath);
            }
        }

        AnimatorController animatorController = CreateAnimatorController(animatorPath, out string animatorError);
        if (animatorController == null)
        {
            EditorUtility.DisplayDialog("错误", animatorError, "确定");
            return;
        }

        GameObject enemyRoot = new GameObject(normalizedName);
        try
        {
            var npcController = AddRequiredComponents(enemyRoot, animatorController, createSecondaryAttackZone);

            var zonesContainer = new GameObject("DetectionZones");
            zonesContainer.transform.SetParent(enemyRoot.transform);
            zonesContainer.transform.localPosition = Vector3.zero;

            var zonesByRole = new Dictionary<DetectionZoneBinding.Role, DetectionZone>();

            if (createPrimaryAttackZone)
            {
                zonesByRole[DetectionZoneBinding.Role.PrimaryAttack] = CreateZone(
                    zonesContainer.transform,
                    "DZ_Attack",
                    GetLayerOrFallback("EnemyHitBox", 10),
                    new Vector2(1.5f, 0f),
                    new Vector2(1.8f, 1.8f),
                    Vector2.zero);
            }

            if (createSecondaryAttackZone)
            {
                zonesByRole[DetectionZoneBinding.Role.SecondaryAttack] = CreateZone(
                    zonesContainer.transform,
                    "DZ_Ability",
                    GetLayerOrFallback("EnemyHitBox", 10),
                    Vector2.zero,
                    new Vector2(1.6f, 1.6f),
                    new Vector2(3f, 0f));
            }

            if (createCliffZone)
            {
                zonesByRole[DetectionZoneBinding.Role.Cliff] = CreateZone(
                    zonesContainer.transform,
                    "DZ_Cliff",
                    GetLayerOrFallback("GroundDetection", 11),
                    new Vector2(1.06f, -1.7f),
                    Vector2.one,
                    Vector2.zero);
            }

            zonesByRole[DetectionZoneBinding.Role.Wall] = CreateZone(
                zonesContainer.transform,
                "DZ_Wall",
                GetLayerOrFallback("GroundDetection", 11),
                new Vector2(1.06f, 0f),
                Vector2.one,
                Vector2.zero);

            if (createAlertZone)
            {
                zonesByRole[DetectionZoneBinding.Role.Alert] = CreateZone(
                    zonesContainer.transform,
                    "DZ_Alert",
                    GetLayerOrFallback("EnemyHitBox", 10),
                    Vector2.zero,
                    Vector2.one,
                    Vector2.zero);
            }

            if (createLookoutZone)
            {
                zonesByRole[DetectionZoneBinding.Role.Lookout] = CreateZone(
                    zonesContainer.transform,
                    "DZ_Lookout",
                    GetLayerOrFallback("EnemyHitBox", 10),
                    Vector2.zero,
                    Vector2.one,
                    Vector2.zero);
            }

            BindDefaultZoneEvents(npcController, zonesByRole);
            WriteZoneBindings(npcController, zonesByRole);

            PrefabUtility.SaveAsPrefabAsset(enemyRoot, prefabPath);
        }
        finally
        {
            DestroyImmediate(enemyRoot);
        }

        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog(
            "成功",
            $"敌人 Prefab 创建完成：\n{prefabPath}\n\n" +
            "接下来建议：\n" +
            "1. 给根物体分配 EnemyTuningProfile\n" +
            "2. 调整各 DZ_* 的大小/位置\n" +
            "3. 替换 AnimatorController 中的动画剪辑（参数已按模板创建）",
            "确定");

        Close();
    }

    private static NpcGroundController AddRequiredComponents(
        GameObject enemyRoot,
        RuntimeAnimatorController animatorController,
        bool includeAbilityZone)
    {
        enemyRoot.layer = GetLayerOrFallback("Enemy", 8);

        var rb2d = enemyRoot.AddComponent<Rigidbody2D>();
        rb2d.gravityScale = 1f;
        rb2d.constraints = RigidbodyConstraints2D.FreezeRotation;

        var capsule = enemyRoot.AddComponent<CapsuleCollider2D>();
        capsule.direction = CapsuleDirection2D.Vertical;
        capsule.size = new Vector2(0.9f, 2.3f);

        var spriteRenderer = enemyRoot.AddComponent<SpriteRenderer>();
        spriteRenderer.sortingOrder = 1;

        var animator = enemyRoot.AddComponent<Animator>();
        animator.runtimeAnimatorController = animatorController;

        enemyRoot.AddComponent<Damageable>();

        var touchingDirections = enemyRoot.AddComponent<TouchingDirections>();
        touchingDirections.castFilter.useTriggers = false;
        touchingDirections.castFilter.useLayerMask = true;
        touchingDirections.castFilter.layerMask = LayerMask.GetMask("Ground");
        touchingDirections.groundDistance = 0.05f;
        touchingDirections.wallDistance = 0.2f;
        touchingDirections.ceilingDistance = 0.05f;

        var controller = enemyRoot.AddComponent<NpcGroundController>();

        // 默认移动参数从 Knight 模板拷贝，避免新建敌人因为加速度太小而“原地跑步不位移”。
        ApplyGroundControllerMovementDefaults(controller);

        // 阶段4新增：勾选了 DZ_Ability（法术检测区）时，Prefab 需要自带 NpcAbilityController + FirePoint。
        // 目的：
        // 1) AnimationEvent 下拉列表可直接选到 OnAbilityRelease()（不依赖运行时 AddComponent）。
        // 2) 统一发射点命名约定：NpcAbilityController 默认查找根节点下名为 "FirePoint" 的子物体。
        if (includeAbilityZone)
        {
            EnsureNpcAbilityController(enemyRoot);
            EnsureFirePoint(enemyRoot.transform);
        }

        return controller;
    }

    private static void EnsureNpcAbilityController(GameObject enemyRoot)
    {
        if (enemyRoot == null)
        {
            return;
        }

        if (enemyRoot.GetComponent<NpcAbilityController>() != null)
        {
            return;
        }

        enemyRoot.AddComponent<NpcAbilityController>();
    }

    private static Transform EnsureFirePoint(Transform enemyRoot)
    {
        if (enemyRoot == null)
        {
            return null;
        }

        Transform existing = enemyRoot.Find("FirePoint");
        if (existing != null)
        {
            return existing;
        }

        var firePoint = new GameObject("FirePoint");
        firePoint.layer = enemyRoot.gameObject.layer;
        firePoint.transform.SetParent(enemyRoot);
        firePoint.transform.localPosition = new Vector3(1.0f, 0.2f, 0f);
        firePoint.transform.localRotation = Quaternion.identity;
        firePoint.transform.localScale = Vector3.one;
        return firePoint.transform;
    }

    private static void ApplyGroundControllerMovementDefaults(NpcGroundController controller)
    {
        if (controller == null)
        {
            return;
        }

        // 兜底值：参考 Knight 模板（当前 walkAcceleration=30）。
        const float fallbackWalkAcceleration = 30f;
        const float fallbackWalkStopRate = 0.05f;
        const float fallbackMaxSpeed = 3f;

        // 尝试从 KnightEnemy.prefab 读取模板值（后续模板调整时，工具可自动跟随）。
        var templatePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GroundControllerTemplatePrefabPath);
        if (templatePrefab != null)
        {
            var templateController = templatePrefab.GetComponent<NpcGroundController>();
            if (templateController != null)
            {
                controller.walkAcceleration = templateController.walkAcceleration;
                controller.walkStopRate = templateController.walkStopRate;
                controller.maxSpeed = templateController.maxSpeed;
                return;
            }
        }

        // 模板缺失/读取失败时，使用兜底值保证敌人至少能移动。
        controller.walkAcceleration = fallbackWalkAcceleration;
        controller.walkStopRate = fallbackWalkStopRate;
        controller.maxSpeed = fallbackMaxSpeed;
    }

    private static DetectionZone CreateZone(
        Transform parent,
        string zoneName,
        int layer,
        Vector2 localPosition,
        Vector2 colliderSize,
        Vector2 colliderOffset)
    {
        var zoneGO = new GameObject(zoneName);
        zoneGO.layer = layer;
        zoneGO.transform.SetParent(parent);
        zoneGO.transform.localPosition = localPosition;
        zoneGO.transform.localRotation = Quaternion.identity;
        zoneGO.transform.localScale = Vector3.one;

        var detectionZone = zoneGO.AddComponent<DetectionZone>();

        var box = zoneGO.AddComponent<BoxCollider2D>();
        box.isTrigger = true;
        box.size = colliderSize;
        box.offset = colliderOffset;

        return detectionZone;
    }

    private static void BindDefaultZoneEvents(
        NpcGroundController controller,
        IReadOnlyDictionary<DetectionZoneBinding.Role, DetectionZone> zonesByRole)
    {
        if (controller == null)
        {
            return;
        }

        if (zonesByRole.TryGetValue(DetectionZoneBinding.Role.Cliff, out var cliffZone) && cliffZone != null)
        {
            if (cliffZone.NoColliderRemain == null)
            {
                cliffZone.NoColliderRemain = new UnityEvent();
            }

            UnityEventTools.AddPersistentListener(cliffZone.NoColliderRemain, controller.OnCliffDetected);
        }

        if (zonesByRole.TryGetValue(DetectionZoneBinding.Role.Wall, out var wallZone) && wallZone != null)
        {
            if (wallZone.OnTargetEnter == null)
            {
                wallZone.OnTargetEnter = new UnityEvent();
            }

            UnityEventTools.AddPersistentListener(wallZone.OnTargetEnter, controller.OnWallDetected);
        }
    }

    private static void WriteZoneBindings(
        EnemyAgentBase agent,
        IReadOnlyDictionary<DetectionZoneBinding.Role, DetectionZone> zonesByRole)
    {
        if (agent == null)
        {
            return;
        }

        var serializedObject = new SerializedObject(agent);
        var zoneBindingsProp = serializedObject.FindProperty("zoneBindings");
        if (zoneBindingsProp == null)
        {
            Debug.LogError($"[{agent.name}] 写入 zoneBindings 失败：找不到序列化字段 zoneBindings。", agent);
            return;
        }

        zoneBindingsProp.arraySize = 0;

        AddBindingIfPresent(zoneBindingsProp, zonesByRole, DetectionZoneBinding.Role.PrimaryAttack, "主攻击检测区");
        AddBindingIfPresent(zoneBindingsProp, zonesByRole, DetectionZoneBinding.Role.SecondaryAttack, "法术检测区");
        AddBindingIfPresent(zoneBindingsProp, zonesByRole, DetectionZoneBinding.Role.Cliff, "悬崖检测区");
        AddBindingIfPresent(zoneBindingsProp, zonesByRole, DetectionZoneBinding.Role.Wall, "墙壁检测区");
        AddBindingIfPresent(zoneBindingsProp, zonesByRole, DetectionZoneBinding.Role.Alert, "警戒范围（可选）");
        AddBindingIfPresent(zoneBindingsProp, zonesByRole, DetectionZoneBinding.Role.Lookout, "视野范围（可选）");

        serializedObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void AddBindingIfPresent(
        SerializedProperty zoneBindingsProp,
        IReadOnlyDictionary<DetectionZoneBinding.Role, DetectionZone> zonesByRole,
        DetectionZoneBinding.Role role,
        string note)
    {
        if (!zonesByRole.TryGetValue(role, out var zone) || zone == null)
        {
            return;
        }

        int index = zoneBindingsProp.arraySize;
        zoneBindingsProp.InsertArrayElementAtIndex(index);

        var element = zoneBindingsProp.GetArrayElementAtIndex(index);
        var roleProp = element.FindPropertyRelative("role");
        var zoneProp = element.FindPropertyRelative("zone");
        var noteProp = element.FindPropertyRelative("note");

        roleProp.enumValueIndex = (int)role;
        zoneProp.objectReferenceValue = zone;
        noteProp.stringValue = note;
    }

    private static AnimatorController CreateAnimatorController(string targetPath, out string error)
    {
        error = "";

        var template = AssetDatabase.LoadAssetAtPath<AnimatorController>(AnimatorTemplatePath);
        if (template == null)
        {
            error = $"找不到 AnimatorController 模板：{AnimatorTemplatePath}";
            return null;
        }

        var controller = AnimatorController.CreateAnimatorControllerAtPath(targetPath);

        foreach (var param in controller.parameters.ToArray())
        {
            controller.RemoveParameter(param);
        }

        foreach (var param in template.parameters)
        {
            var copied = new AnimatorControllerParameter
            {
                name = param.name,
                type = param.type,
                defaultBool = param.defaultBool,
                defaultFloat = param.defaultFloat,
                defaultInt = param.defaultInt
            };
            controller.AddParameter(copied);
        }

        AssetDatabase.SaveAssets();
        return controller;
    }

    private static int GetLayerOrFallback(string layerName, int fallback)
    {
        int layer = LayerMask.NameToLayer(layerName);
        return layer >= 0 ? layer : fallback;
    }

    private static void EnsureFolder(string parentFolder, string childFolderName)
    {
        string childPath = $"{parentFolder}/{childFolderName}";
        if (AssetDatabase.IsValidFolder(childPath))
        {
            return;
        }

        AssetDatabase.CreateFolder(parentFolder, childFolderName);
    }

    private static bool IsEnemyNameValid(string name, out string error)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            error = "敌人名称不能为空。";
            return false;
        }

        string trimmed = name.Trim();
        if (trimmed.Length <= 0)
        {
            error = "敌人名称不能为空。";
            return false;
        }

        char[] invalidChars = Path.GetInvalidFileNameChars();
        if (trimmed.IndexOfAny(invalidChars) >= 0)
        {
            error = "敌人名称包含非法字符，请修改后再试。";
            return false;
        }

        if (trimmed.Contains("/") || trimmed.Contains("\\"))
        {
            error = "敌人名称不能包含路径分隔符。";
            return false;
        }

        error = "";
        return true;
    }
}
