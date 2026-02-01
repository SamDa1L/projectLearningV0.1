using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;

public static class FloatingTextTools
{
    private const string UiManagerPrefabPath = "Assets/Manager/UIManager.prefab";

    [MenuItem("Tools/UI/FloatingText/Validate")]
    private static void Validate()
    {
        var sb = new StringBuilder();
        bool hasError = false;

        GameObject uiManagerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(UiManagerPrefabPath);
        if (uiManagerPrefab == null)
        {
            sb.AppendLine($"[Error] Missing prefab: {UiManagerPrefabPath}");
            hasError = true;
            ShowResult(sb, hasError);
            return;
        }

        UIManager uiManager = uiManagerPrefab.GetComponent<UIManager>();
        if (uiManager == null)
        {
            sb.AppendLine($"[Error] UIManager component not found on prefab: {UiManagerPrefabPath}");
            hasError = true;
            ShowResult(sb, hasError);
            return;
        }

        if (uiManager.floatingTextPrefab == null)
        {
            sb.AppendLine("[Error] UIManager.floatingTextPrefab is not assigned.");
            hasError = true;
        }
        else
        {
            ValidateFloatingTextPrefab(uiManager.floatingTextPrefab, sb, ref hasError);
        }

        if (uiManager.floatingTextStyleCatalog == null)
        {
            sb.AppendLine("[Error] UIManager.floatingTextStyleCatalog is not assigned (0.46 requirement).");
            hasError = true;
        }
        else
        {
            ValidateStyleCatalog(uiManager.floatingTextStyleCatalog, sb, ref hasError);
        }

        if (!hasError)
        {
            sb.AppendLine("[OK] FloatingText validation passed.");
        }

        ShowResult(sb, hasError);
    }

    private static void ValidateFloatingTextPrefab(GameObject floatingTextPrefab, StringBuilder sb, ref bool hasError)
    {
        TMP_Text tmpText = floatingTextPrefab.GetComponent<TMP_Text>();
        if (tmpText == null)
        {
            sb.AppendLine("[Error] FloatingText prefab is missing TMP_Text.");
            hasError = true;
        }
        else if (tmpText.raycastTarget)
        {
            sb.AppendLine("[Error] FloatingText TMP_Text.raycastTarget must be false.");
            hasError = true;
        }

        HealthText healthText = floatingTextPrefab.GetComponent<HealthText>();
        if (healthText == null)
        {
            sb.AppendLine("[Error] FloatingText prefab is missing HealthText.");
            hasError = true;
        }
    }

    private static void ValidateStyleCatalog(FloatingTextStyleCatalog catalog, StringBuilder sb, ref bool hasError)
    {
        if (!catalog.TryGetStyle(FloatingTextKind.Heal, out var healStyle))
        {
            sb.AppendLine("[Warning] StyleCatalog has no explicit Heal entry; Heal will fall back to defaultStyle.");
        }
        else
        {
            WarnIfContainsSign("Heal.prefix", healStyle.textStyle.prefix, sb);
            WarnIfContainsSign("Heal.suffix", healStyle.textStyle.suffix, sb);
        }

        if (!catalog.TryGetStyle(FloatingTextKind.Damage, out var damageStyle))
        {
            sb.AppendLine("[Warning] StyleCatalog has no explicit Damage entry; Damage will fall back to defaultStyle.");
        }
        else
        {
            WarnIfContainsSign("Damage.prefix", damageStyle.textStyle.prefix, sb);
            WarnIfContainsSign("Damage.suffix", damageStyle.textStyle.suffix, sb);
        }
    }

    private static void WarnIfContainsSign(string label, string value, StringBuilder sb)
    {
        if (string.IsNullOrEmpty(value))
            return;

        if (value.Contains("+") || value.Contains("-"))
        {
            sb.AppendLine($"[Warning] {label} contains '+' or '-' (0.46 requires no +/- sign in floating numbers).");
        }
    }

    private static void ShowResult(StringBuilder sb, bool hasError)
    {
        string message = sb.ToString();
        if (hasError)
            Debug.LogError(message);
        else
            Debug.Log(message);

        EditorUtility.DisplayDialog("FloatingText Validate", message, "OK");
    }
}

