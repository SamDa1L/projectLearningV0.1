using UnityEditor;
using UnityEngine;

public static class GameplayConfigEditorMenu
{
    [MenuItem("Tools/Gameplay/Verify GameplayConfig")]
    public static void VerifyGameplayConfig()
    {
        var config = Resources.Load<GameplayConfig>("Config/GameplayConfig");
        if (config == null)
        {
            Debug.LogError("[GameplayConfig] 配置未找到。请在 Assets/Resources/Config/GameplayConfig.asset 创建");
            return;
        }

        Debug.Log($"[GameplayConfig] 验证成功 - 版本 v{config.version}");
        config.PrintAllValues();
    }
}
