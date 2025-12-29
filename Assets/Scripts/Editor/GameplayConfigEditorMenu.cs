using UnityEditor;

public static class GameplayConfigEditorMenu
{
    [MenuItem("Tools/Gameplay/Verify GameplayConfig")]
    public static void VerifyGameplayConfig()
    {
        GameplayConfig.VerifyConfig();
    }
}
