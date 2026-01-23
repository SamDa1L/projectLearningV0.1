using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Reflection;
using CastleDB.Runtime;

/// <summary>
/// Replace 流程完整测试（固定场景 + 固定资源）
///
/// 契约 [C-Test-1] PlayMode 测试：
/// - 0.5：能力拾取改为“顺序覆盖槽位”，不再由 Inventory.TryPickup 返回 RequireReplace
/// - ReplaceController 仍保留：相关测试通过手动构造 PendingReplaceContext 进入替换流程
/// - 输入模式切换与恢复、Confirm/Cancel/失败路径清理
/// </summary>
public class ReplaceFlowIntegrationTests
{
    private const string TEST_SCENE = "Assets/Scenes/NPCTestScenes/TestEnemy.unity";

    private GameObject _playerObj;
    private PlayerInventory _inventory;
    private PlayerContext _playerContext;
    private ReplaceController _replaceController;
    private HudPresenter _hudPresenter;
    private PlayerEquipmentController _equipmentController;
    private AbilitySystem _abilitySystem;

    /// <summary>
    /// 在固定场景中运行测试
    /// </summary>
    [UnitySetUp]
    public IEnumerator Setup()
    {
        // 加载固定测试场景
        yield return SceneManager.LoadSceneAsync(TEST_SCENE);

        // 等待场景加载完成
        yield return null;

        // 查找 Player GameObject（假设场景中存在 "Player" 对象）
        _playerObj = GameObject.Find("Player");

        if (_playerObj == null)
        {
            Debug.LogWarning($"场景 {TEST_SCENE} 中未找到 Player，创建临时 Player");
            _playerObj = CreateTestPlayer();
            yield return null;
        }

        // 获取组件
        // 阶段2：Player 模块可能被拆到子节点（Systems/UI），这里统一用 GetComponentInChildren 查找
        _inventory = _playerObj.GetComponentInChildren<PlayerInventory>(true);
        _playerContext = _playerObj.GetComponentInChildren<PlayerContext>(true);
        _replaceController = _playerObj.GetComponentInChildren<ReplaceController>(true);
        _equipmentController = _playerObj.GetComponentInChildren<PlayerEquipmentController>(true);

        // PlayMode 测试不使用“红色 Error 日志”来验收失败路径
        if (_replaceController != null)
        {
            _replaceController.EnableUnityConsoleLogging = false;
        }

        // 查找 HudPresenter（通过类型查找，避免 HUDCanvas(Clone) 命名差异）
        _hudPresenter = Object.FindObjectOfType<HudPresenter>(true);

        // 等待所有初始化完成
        yield return new WaitForSeconds(0.5f);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        // 清理场景（仅在已加载时卸载）
        Scene scene = SceneManager.GetSceneByPath(TEST_SCENE);
        if (scene.IsValid() && scene.isLoaded)
        {
            yield return SceneManager.UnloadSceneAsync(scene);
        }
    }

    /// <summary>
    /// 测试：槽满时拾取 Ability 应直接覆盖一个槽位（不再触发 RequireReplace）
    /// </summary>
    [UnityTest]
    public IEnumerator TestFifthAbilityDoesNotRequireReplace()
    {
        if (_inventory == null)
        {
            Assert.Inconclusive("必需组件未初始化");
            yield break;
        }

        // 填满 4 个槽位（使用固定资源的 itemId）
        _inventory.EquipAbilityItemToSlot(0, "ability_arrow");
        _inventory.EquipAbilityItemToSlot(1, "ability_walk");
        _inventory.EquipAbilityItemToSlot(2, "ability_jump");
        _inventory.EquipAbilityItemToSlot(3, "ability_attack");

        yield return null;

        // 创建第 5 个 Ability Pickup（用来提供 sourcePickup 参数）
        var pickupObj = CreateAbilityPickup("ability_run", Vector3.zero);
        var pickup = pickupObj.GetComponent<ItemPickup>();

        // 触发拾取
        var request = new PickupRequest(pickup.itemId, pickup.amount, pickup);
        var result = _inventory.TryPickup(request, out var ctx);

        // 0.5：槽满拾取应直接 Success（顺序覆盖某个槽位），不再 RequireReplace
        Assert.AreEqual(PickupResult.Success, result, "0.5：槽满拾取应直接覆盖一个槽位，而不是 RequireReplace");

        // ctx 应保持默认值（因为不再需要 Replace 上下文）
        Assert.IsTrue(string.IsNullOrEmpty(ctx.pendingItemId), "0.5：Success 时 pendingItemId 应为空");
        Assert.AreEqual(0, ctx.pendingAmount, "0.5：Success 时 pendingAmount 应为 0");
        Assert.IsNull(ctx.sourcePickup, "0.5：Success 时 sourcePickup 应为空");

        // 验证：ability_run 必然已被写入到某个槽位
        bool found = false;
        for (int i = 0; i < PlayerInventory.AbilitySlotCount; i++)
        {
            if (_inventory.GetAbilityItemId(i) == "ability_run")
            {
                found = true;
                break;
            }
        }
        Assert.IsTrue(found, "0.5：拾取后 ability_run 应直接入槽");

        // ReplaceController 不应进入 Selecting（此测试未调用 BeginReplace）
        if (_replaceController != null)
        {
            Assert.IsFalse(_replaceController.IsSelecting, "0.5：拾取不应触发 ReplaceController 进入 Selecting");
        }

        // Cleanup
        Object.Destroy(pickupObj);
        yield return null;
    }

    /// <summary>
    /// 测试：Confirm 后 SourcePickup 被销毁
    /// </summary>
    [UnityTest]
    public IEnumerator TestConfirmDestroysSourcePickup()
    {
        if (_inventory == null || _replaceController == null)
        {
            Assert.Inconclusive("必需组件未初始化");
            yield break;
        }

        // 填满 4 个槽位
        _inventory.EquipAbilityItemToSlot(0, "ability_arrow");
        _inventory.EquipAbilityItemToSlot(1, "ability_walk");
        _inventory.EquipAbilityItemToSlot(2, "ability_jump");
        _inventory.EquipAbilityItemToSlot(3, "ability_attack");

        yield return null;

        // 创建一个 Ability Pickup 作为 sourcePickup
        var pickupObj = CreateAbilityPickup("ability_run", Vector3.zero);
        var pickup = pickupObj.GetComponent<ItemPickup>();

        // 构造 PendingReplaceContext（模拟进入 Replace 流程）
        var ctx = new PendingReplaceContext("ability_run", 1, pickup);

        // 锁定 pickup（模拟 ItemPickup 行为）
        pickup.SetLocked(true);

        // BeginReplace
        _replaceController.BeginReplace(ctx);

        yield return null;

        // 验证：pickup 对象存在
        Assert.IsNotNull(pickupObj, "拾取物应存在");

        // 反射调用 Confirm（选择槽位 0）
        var confirmMethod = typeof(ReplaceController).GetMethod("Confirm", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(confirmMethod, "Confirm method not found");
        confirmMethod.Invoke(_replaceController, new object[] { 0 });

        // 等待销毁完成
        yield return null;

        // 验证：pickup 对象已被销毁（Unity Destroy 后下一帧变为 null）
        Assert.IsTrue(pickupObj == null, "Confirm 后 sourcePickup.gameObject 应被销毁");
        Assert.IsFalse(_replaceController.IsSelecting, "Confirm 后应退出 Selecting 状态");

        // Cleanup（如果未销毁，手动清理）
        if (pickupObj != null)
        {
            Object.Destroy(pickupObj);
        }
        yield return null;
    }

    /// <summary>
    /// 测试：Cancel 后 pickup 解锁
    /// </summary>
    [UnityTest]
    public IEnumerator TestCancelUnlocksPickup()
    {
        if (_inventory == null || _replaceController == null)
        {
            Assert.Inconclusive("必需组件未初始化");
            yield break;
        }

        // 填满 4 个槽位
        _inventory.EquipAbilityItemToSlot(0, "ability_arrow");
        _inventory.EquipAbilityItemToSlot(1, "ability_walk");
        _inventory.EquipAbilityItemToSlot(2, "ability_jump");
        _inventory.EquipAbilityItemToSlot(3, "ability_attack");

        yield return null;

        // 创建第 5 个 Ability Pickup
        var pickupObj = CreateAbilityPickup("ability_run", Vector3.zero);
        var pickup = pickupObj.GetComponent<ItemPickup>();

        // 构造 PendingReplaceContext（模拟进入 Replace 流程）
        var ctx = new PendingReplaceContext("ability_run", 1, pickup);

        // 锁定 pickup（模拟 ItemPickup 行为）
        pickup.SetLocked(true);

        // BeginReplace
        _replaceController.BeginReplace(ctx);
        yield return null;

        // 验证：pickup 被锁定
        Assert.IsTrue(pickup.IsLocked, "拾取物应被锁定");

        var cancelMethod = typeof(ReplaceController).GetMethod("Cancel", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(cancelMethod, "Cancel method not found");
        cancelMethod.Invoke(_replaceController, null);

        // 等待解锁完成
        yield return null;

        // 验证：pickup 已解锁
        Assert.IsFalse(pickup.IsLocked, "拾取物应已解锁");

        // 验证：ReplaceController 回到 Idle 状态
        Assert.IsFalse(_replaceController.IsSelecting, "ReplaceController 应回到 Idle");

        // Cleanup
        Object.Destroy(pickupObj);
        yield return null;
    }

    /// <summary>
    /// 测试：BeginReplace 失败时的清理流程
    /// 契约 [C-Test-1]：BeginReplace 失败 → 输入恢复/面板关闭/pending 清空/pickup 解锁
    /// </summary>
    [UnityTest]
    public IEnumerator TestBeginReplaceFailureCleansUp()
    {
        if (_inventory == null || _replaceController == null || _playerContext == null)
        {
            Assert.Inconclusive("必需组件未初始化");
            yield break;
        }

        // 填满 4 个槽位
        _inventory.EquipAbilityItemToSlot(0, "ability_arrow");
        _inventory.EquipAbilityItemToSlot(1, "ability_walk");
        _inventory.EquipAbilityItemToSlot(2, "ability_jump");
        _inventory.EquipAbilityItemToSlot(3, "ability_attack");

        yield return null;

        // 创建第 5 个 Ability Pickup
        var pickupObj = CreateAbilityPickup("ability_run", Vector3.zero);
        var pickup = pickupObj.GetComponent<ItemPickup>();

        // 构造 PendingReplaceContext（模拟进入 Replace 流程）
        var ctx = new PendingReplaceContext("ability_run", 1, pickup);

        // 锁定 pickup
        pickup.SetLocked(true);

        // 调用 BeginReplace
        _replaceController.BeginReplace(ctx);
        yield return null;

        // 验证：应进入 Selecting 状态
        Assert.IsTrue(_replaceController.IsSelecting, "ReplaceController 应进入 Selecting 状态");

        // 现在通过反射调用 Confirm 方法，传入一个无效的 itemId 导致失败
        // 这将触发 CancelInternal(true)，应该清理所有状态

        var confirmMethod = typeof(ReplaceController).GetMethod("Confirm", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(confirmMethod, "Confirm method not found");

        // 传入槽位 0，但由于 pending 的 itemId 是 ability_run，应该会成功
        // 为了测试失败路径，我们需要模拟一个会失败的场景
        // 先销毁 pickup，模拟 ValidatePending 失败
        Object.Destroy(pickupObj);
        yield return null;

        // 调用 Confirm（由于 sourcePickup 已失效，ValidatePending 应返回 false）
        confirmMethod.Invoke(_replaceController, new object[] { 0 });

        yield return null;

        // 验证：应已退出 Selecting 状态
        Assert.IsFalse(_replaceController.IsSelecting, "ReplaceController 应退出 Selecting 状态");

        // 验证：输入应已恢复（无法直接验证，但可以检查状态）
        // 验证：面板应已关闭（无法直接访问 _panelRoot，但可以通过 IsSelecting 间接验证）

        yield return null;
    }

    /// <summary>
    /// 测试：Confirm 失败后清理验证（TryGetItem 失败）
    /// 契约 [C-Test-1]：Confirm 执行失败的任何一步 → CancelInternal(true) → 清理完整
    /// </summary>
    [UnityTest]
    public IEnumerator TestConfirmFailureOnInvalidItemCleansUp()
    {
        if (_inventory == null || _replaceController == null)
        {
            Assert.Inconclusive("必需组件未初始化");
            yield break;
        }

        // 填满 4 个槽位
        _inventory.EquipAbilityItemToSlot(0, "ability_arrow");
        _inventory.EquipAbilityItemToSlot(1, "ability_walk");
        _inventory.EquipAbilityItemToSlot(2, "ability_jump");
        _inventory.EquipAbilityItemToSlot(3, "ability_attack");

        yield return null;

        // 创建第 5 个 Ability Pickup，但使用一个不存在的 itemId
        var pickupObj = CreateAbilityPickup("invalid_item_id", Vector3.zero);
        var pickup = pickupObj.GetComponent<ItemPickup>();

        // 构造一个手动的 PendingReplaceContext（模拟 TryPickup 返回 RequireReplace）
        var ctx = new PendingReplaceContext("invalid_item_id", 1, pickup);

        // 锁定 pickup
        pickup.SetLocked(true);

        // 调用 BeginReplace
        _replaceController.BeginReplace(ctx);
        yield return null;

        // 验证：应进入 Selecting 状态
        Assert.IsTrue(_replaceController.IsSelecting, "ReplaceController 应进入 Selecting 状态");

        // 调用 Confirm（由于 itemId 无效，TryGetItem 应失败）
        var confirmMethod = typeof(ReplaceController).GetMethod("Confirm", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(confirmMethod, "Confirm method not found");

        confirmMethod.Invoke(_replaceController, new object[] { 0 });

        yield return null;

        // 验证：应已退出 Selecting 状态
        Assert.IsFalse(_replaceController.IsSelecting, "ReplaceController 应退出 Selecting 状态");

        // 验证：pickup 应已解锁（Confirm 失败 → CancelInternal(true)）
        Assert.IsFalse(pickup.IsLocked, "Confirm 失败后 pickup 应解锁");

        // Cleanup
        Object.Destroy(pickupObj);
        yield return null;
    }

    // ===== 辅助方法 =====

    /// <summary>
    /// 创建测试用 Player GameObject
    /// </summary>
    private GameObject CreateTestPlayer()
    {
        var player = new GameObject("TestPlayer");

        // 添加必需组件
        player.AddComponent<PlayerInventory>();
        player.AddComponent<PlayerContext>();
        player.AddComponent<Damageable>();
        player.AddComponent<ReplaceController>();
        player.AddComponent<PlayerEquipmentController>();

        // 添加 Rigidbody2D 和 Collider2D（拾取物检测需要）
        var rb = player.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;

        var collider = player.AddComponent<CircleCollider2D>();
        collider.radius = 1f;

        return player;
    }

    /// <summary>
    /// 创建测试用 Ability Pickup
    /// </summary>
    private GameObject CreateAbilityPickup(string itemId, Vector3 position)
    {
        var pickupObj = new GameObject($"Pickup_{itemId}");
        pickupObj.transform.position = position;

        // 添加必需组件
        var spriteRenderer = pickupObj.AddComponent<SpriteRenderer>();
        var collider = pickupObj.AddComponent<BoxCollider2D>();
        collider.isTrigger = true;

        var pickup = pickupObj.AddComponent<ItemPickup>();
        pickup.itemId = itemId;
        pickup.amount = 1;
        pickup.autoPickup = true;

        return pickupObj;
    }
}
