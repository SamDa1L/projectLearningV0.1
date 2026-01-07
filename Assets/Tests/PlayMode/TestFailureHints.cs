using NUnit.Framework;

/// <summary>
/// 统一的测试失败报告（原因 + 修复建议），用于同时在 TestRunner 与 Test Runner+ 中展示。
/// </summary>
public static class TestFailureHints
{
    public static void Require(bool condition, string reason, string fix, string context = null)
    {
        if (condition)
        {
            return;
        }

        Fail(reason, fix, context);
    }

    public static void Fail(string reason, string fix, string context = null)
    {
        var message = BuildMessage(reason, fix, context);

        // 写入 NUnit 输出流（Test Runner+ 会显示 Output；Test Runner 也可查看到）。
        try
        {
            TestContext.Error.WriteLine(message);
        }
        catch
        {
            // Best effort.
        }

        Assert.Fail(message);
    }

    public static string BuildMessage(string reason, string fix, string context = null)
    {
        reason ??= "";
        fix ??= "";
        context ??= "";

        if (string.IsNullOrWhiteSpace(context))
        {
            return $"【原因】{reason}\n【修复】{fix}";
        }

        return $"【原因】{reason}\n【修复】{fix}\n【上下文】{context}";
    }
}

