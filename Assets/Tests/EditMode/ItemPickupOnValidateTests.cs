using NUnit.Framework;
using CastleDB.Runtime;

/// <summary>
/// ItemPickup 编辑器校验规则测试（不依赖日志输出）。
///
/// 约定：
/// - 规则本体由 ItemPickup.ValidateAndFixAmountForEditor 提供（纯逻辑，不输出 Debug.Log）
/// - OnValidate 负责把结果转换为编辑器提示（日志/修正字段等）
/// </summary>
public class ItemPickupOnValidateTests
{
    [Test]
    public void TestAmountZeroAutoCorrect()
    {
        int amount = 0;
        var severity = ItemPickup.ValidateAndFixAmountForEditor(ItemType.Consumable, ref amount, out string message);

        Assert.AreEqual(ItemPickup.EditorValidationSeverity.Warning, severity);
        Assert.AreEqual(1, amount);
        Assert.IsNotEmpty(message);
    }

    [Test]
    public void TestAmountNegativeAutoCorrect()
    {
        int amount = -5;
        var severity = ItemPickup.ValidateAndFixAmountForEditor(ItemType.Consumable, ref amount, out string message);

        Assert.AreEqual(ItemPickup.EditorValidationSeverity.Warning, severity);
        Assert.AreEqual(1, amount);
        Assert.IsNotEmpty(message);
    }

    [Test]
    public void TestAbilityTypeForceAmountOne()
    {
        int amount = 5;
        var severity = ItemPickup.ValidateAndFixAmountForEditor(ItemType.Ability, ref amount, out string message);

        Assert.AreEqual(ItemPickup.EditorValidationSeverity.Warning, severity);
        Assert.AreEqual(1, amount);
        Assert.IsNotEmpty(message);
    }

    [Test]
    public void TestConsumableAllowsAmountGreaterThanOne()
    {
        int amount = 10;
        var severity = ItemPickup.ValidateAndFixAmountForEditor(ItemType.Consumable, ref amount, out string message);

        Assert.AreEqual(ItemPickup.EditorValidationSeverity.None, severity);
        Assert.AreEqual(10, amount);
        Assert.IsTrue(string.IsNullOrEmpty(message));
    }

    [Test]
    public void TestMaterialTypeReturnsError()
    {
        int amount = 1;
        var severity = ItemPickup.ValidateAndFixAmountForEditor(ItemType.Material, ref amount, out string message);

        Assert.AreEqual(ItemPickup.EditorValidationSeverity.Error, severity);
        Assert.AreEqual(1, amount, "Material 类型不应自动改 amount（只提示错误）");
        Assert.IsNotEmpty(message);
        Assert.IsTrue(message.Contains("Material") && message.Contains("不支持拾取"));
    }
}

