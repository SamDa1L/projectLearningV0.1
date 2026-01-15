using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// 0.45 方案 B 单 prefab 版本：统一管理所有浮动文字
/// 所有伤害/治疗数字使用同一个 FloatingText.prefab
/// </summary>
public class UIManager : MonoBehaviour
{
    /// <summary>
    /// 0.45 修正：统一的浮动文字 prefab（必须包含 HealthText.cs 和 TMP_Text）
    /// </summary>
    [Header("Floating Text Settings")]
    [Tooltip("统一的浮动文字 prefab（Assets/UI/Text/FloatingText.prefab）")]
    public GameObject floatingTextPrefab;

    [Header("Floating Text Style (0.46)")]
    [Tooltip("0.46: FloatingTextStyleCatalog (ScriptableObject). If null, use fallback (0.45).")]
    public FloatingTextStyleCatalog floatingTextStyleCatalog;

    /// <summary>
    /// 0.45 修正：浮动文字容器（FloatingTextCanvas 或 FloatingTextRoot）
    /// 必须是 RectTransform（UI 对象）
    /// 注意：此字段无法在 Prefab 中直接引用场景对象，必须在场景实例上覆盖绑定
    /// </summary>
    [Tooltip("浮动文字容器（FloatingTextCanvas，必须在场景实例中绑定）")]
    public Canvas gameCanvas;

    /// <summary>
    /// 0.45 修正：可选的浮动文字根节点（优先使用，否则回退到 gameCanvas）
    /// </summary>
    [Tooltip("可选：FloatingTextRoot 节点（FloatingTextCanvas 下的子节点）")]
    public Transform floatingTextRoot;

    /// <summary>
    /// 0.45 修正：世界坐标转换用相机（优先使用，空则回退到 Camera.main）
    /// </summary>
    [Tooltip("世界坐标转换用相机（优先使用，空则回退到 Camera.main）")]
    public Camera worldCamera;

    private InputAction _escapeAction;

    private void Awake()
    {
        // 0.45 修正：移除 FindObjectOfType，改为序列化引用
        // 如果未设置，输出警告但不阻断（允许运行时注入）
        if (gameCanvas == null)
        {
            Debug.LogWarning("[UIManager] gameCanvas 未设置，浮动文字功能将不可用。请在 Inspector 中指定或通过脚本注入。");
        }

        if (floatingTextPrefab == null)
        {
            Debug.LogWarning("[UIManager] floatingTextPrefab 未设置，浮动文字功能将不可用。");
        }
    }


    private void OnEnable()
    {
        CharacterEvents.characterDamaged += CharacterTookDamage;
        CharacterEvents.characterHealed += CharacterHealed;

        EnsureEscapeAction();
        _escapeAction.Enable();
    }


    private void OnDisable()
    {
        CharacterEvents.characterDamaged -= CharacterTookDamage;
        CharacterEvents.characterHealed -= CharacterHealed;

        if (_escapeAction != null)
        {
            _escapeAction.Disable();
        }
    }



    // Start is called before the first frame update
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {

    }

    private void EnsureEscapeAction()
    {
        if (_escapeAction != null)
        {
            return;
        }

        // Local action avoids adding a second PlayerInput that would steal keyboard/mouse pairing.
        _escapeAction = new InputAction("UIManager.Escape", InputActionType.Button, "<Keyboard>/escape");
        _escapeAction.started += OnExitGame;
    }


    /// <summary>
    /// 0.45 修正：统一的浮动文字生成方法（契约 P0-1 + P0-2）
    /// </summary>
    /// <param name="target">受伤的 Damageable 对象</param>
    /// <param name="amount">伤害数值</param>
    /// <param name="worldPos">世界坐标（来自 Damageable.Hit）</param>
    public void CharacterTookDamage(Damageable target, int amount, Vector2 worldPos)
    {
        if (floatingTextStyleCatalog != null)
        {
            ShowFloatingText(FloatingTextKind.Damage, amount, worldPos);
            return;
        }
        // 前置校验
        if (gameCanvas == null)
        {
            Debug.LogWarning("[UIManager] gameCanvas 为空，无法生成伤害浮动文字");
            return;
        }

        if (floatingTextPrefab == null)
        {
            Debug.LogWarning("[UIManager] floatingTextPrefab 为空，无法生成伤害浮动文字");
            return;
        }

        // 选择最终 parent（优先 floatingTextRoot，否则 gameCanvas.transform）
        Transform parent = floatingTextRoot != null ? floatingTextRoot : gameCanvas.transform;
        RectTransform parentRect = parent as RectTransform;

        if (parentRect == null)
        {
            Debug.LogError("[UIManager] parent 必须是 RectTransform（UI 对象），当前类型：" + parent.GetType());
            return;
        }

        // 获取世界坐标转换用相机（优先 worldCamera，否则 Camera.main）
        Camera cam = worldCamera != null ? worldCamera : Camera.main;
        if (cam == null)
        {
            Debug.LogError("[UIManager] 无法获取世界坐标转换用相机（worldCamera 和 Camera.main 都为空）");
            return;
        }

        // 世界坐标转屏幕坐标
        Vector2 screenPoint = cam.WorldToScreenPoint(worldPos);

        // 确定 UI 相机（Overlay 传 null，ScreenSpaceCamera 传对应相机）
        Camera uiCam = null;
        if (gameCanvas.renderMode == RenderMode.ScreenSpaceCamera)
        {
            uiCam = gameCanvas.worldCamera;
        }

        // 屏幕坐标转 RectTransform 局部坐标（契约 P0-1）
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
            parentRect, screenPoint, uiCam, out Vector2 localPos))
        {
            Debug.LogWarning("[UIManager] ScreenPointToLocalPointInRectangle 失败");
            return;
        }

        // 实例化 FloatingText
        GameObject go = Instantiate(floatingTextPrefab, parentRect);
        RectTransform goRect = go.transform as RectTransform;
        if (goRect == null)
        {
            Debug.LogError("[UIManager] floatingTextPrefab 根节点必须包含 RectTransform");
            Destroy(go);
            return;
        }

        // 设置局部坐标
        goRect.anchoredPosition = localPos;

        // 设置文本内容和颜色（红色 = 伤害）
        TMP_Text tmpText = go.GetComponent<TMP_Text>();
        if (tmpText != null)
        {
            tmpText.text = amount.ToString();
            tmpText.color = Color.red;
        }
        else
        {
            Debug.LogWarning("[UIManager] floatingTextPrefab 缺少 TMP_Text 组件");
        }
    }

    /// <summary>
    /// 0.45 修正：统一的治疗浮动文字生成方法（契约 P0-1 + P0-2）
    /// </summary>
    /// <param name="target">治疗的 Damageable 对象</param>
    /// <param name="amount">治疗数值</param>
    /// <param name="worldPos">世界坐标（来自 Damageable.Heal）</param>
    public void CharacterHealed(Damageable target, int amount, Vector2 worldPos)
    {
        if (floatingTextStyleCatalog != null)
        {
            ShowFloatingText(FloatingTextKind.Heal, amount, worldPos);
            return;
        }
        // 前置校验
        if (gameCanvas == null)
        {
            Debug.LogWarning("[UIManager] gameCanvas 为空，无法生成治疗浮动文字");
            return;
        }

        if (floatingTextPrefab == null)
        {
            Debug.LogWarning("[UIManager] floatingTextPrefab 为空，无法生成治疗浮动文字");
            return;
        }

        // 选择最终 parent（优先 floatingTextRoot，否则 gameCanvas.transform）
        Transform parent = floatingTextRoot != null ? floatingTextRoot : gameCanvas.transform;
        RectTransform parentRect = parent as RectTransform;

        if (parentRect == null)
        {
            Debug.LogError("[UIManager] parent 必须是 RectTransform（UI 对象），当前类型：" + parent.GetType());
            return;
        }

        // 获取世界坐标转换用相机（优先 worldCamera，否则 Camera.main）
        Camera cam = worldCamera != null ? worldCamera : Camera.main;
        if (cam == null)
        {
            Debug.LogError("[UIManager] 无法获取世界坐标转换用相机（worldCamera 和 Camera.main 都为空）");
            return;
        }

        // 世界坐标转屏幕坐标
        Vector2 screenPoint = cam.WorldToScreenPoint(worldPos);

        // 确定 UI 相机（Overlay 传 null，ScreenSpaceCamera 传对应相机）
        Camera uiCam = null;
        if (gameCanvas.renderMode == RenderMode.ScreenSpaceCamera)
        {
            uiCam = gameCanvas.worldCamera;
        }

        // 屏幕坐标转 RectTransform 局部坐标（契约 P0-1）
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
            parentRect, screenPoint, uiCam, out Vector2 localPos))
        {
            Debug.LogWarning("[UIManager] ScreenPointToLocalPointInRectangle 失败");
            return;
        }

        // 实例化 FloatingText
        GameObject go = Instantiate(floatingTextPrefab, parentRect);
        RectTransform goRect = go.transform as RectTransform;
        if (goRect == null)
        {
            Debug.LogError("[UIManager] floatingTextPrefab 根节点必须包含 RectTransform");
            Destroy(go);
            return;
        }

        // 设置局部坐标
        goRect.anchoredPosition = localPos;

        // 设置文本内容和颜色（绿色 = 治疗）
        TMP_Text tmpText = go.GetComponent<TMP_Text>();
        if (tmpText != null)
        {
            tmpText.text = amount.ToString();
            tmpText.color = Color.green;
        }
        else
        {
            Debug.LogWarning("[UIManager] floatingTextPrefab 缺少 TMP_Text 组件");
        }
    }

    private void ShowFloatingText(FloatingTextKind kind, int amount, Vector2 worldPos)
    {
        if (gameCanvas == null)
        {
            Debug.LogWarning("[UIManager] gameCanvas is null, cannot spawn floating text.");
            return;
        }

        if (floatingTextPrefab == null)
        {
            Debug.LogWarning("[UIManager] floatingTextPrefab is null, cannot spawn floating text.");
            return;
        }

        Transform parent = floatingTextRoot != null ? floatingTextRoot : gameCanvas.transform;
        RectTransform parentRect = parent as RectTransform;
        if (parentRect == null)
        {
            Debug.LogError("[UIManager] FloatingText parent must be a RectTransform.");
            return;
        }

        Camera cam = worldCamera != null ? worldCamera : Camera.main;
        if (cam == null)
        {
            Debug.LogError("[UIManager] No camera for WorldToScreenPoint (worldCamera and Camera.main are null).");
            return;
        }

        Vector2 screenPoint = cam.WorldToScreenPoint(worldPos);

        Camera uiCam = null;
        if (gameCanvas.renderMode == RenderMode.ScreenSpaceCamera)
        {
            uiCam = gameCanvas.worldCamera;
        }

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
            parentRect, screenPoint, uiCam, out Vector2 localPos))
        {
            Debug.LogWarning("[UIManager] ScreenPointToLocalPointInRectangle failed.");
            return;
        }

        bool hasCatalog = floatingTextStyleCatalog != null;
        FloatingTextStyleCatalog.Entry style = default;
        if (hasCatalog)
        {
            style = floatingTextStyleCatalog.GetStyleOrDefault(kind);

            localPos += style.motionStyle.localOffset;

            Vector2 randomOffset = style.motionStyle.randomLocalOffset;
            if (randomOffset != Vector2.zero)
            {
                localPos += new Vector2(
                    UnityEngine.Random.Range(-randomOffset.x, randomOffset.x),
                    UnityEngine.Random.Range(-randomOffset.y, randomOffset.y));
            }
        }

        GameObject go = Instantiate(floatingTextPrefab, parentRect);
        RectTransform goRect = go.transform as RectTransform;
        if (goRect == null)
        {
            Debug.LogError("[UIManager] floatingTextPrefab root must contain RectTransform.");
            Destroy(go);
            return;
        }

        goRect.anchoredPosition = localPos;

        TMP_Text tmpText = go.GetComponent<TMP_Text>();
        if (tmpText == null)
        {
            Debug.LogWarning("[UIManager] floatingTextPrefab missing TMP_Text component.");
            Destroy(go);
            return;
        }

        int displayAmount = Mathf.Abs(amount); // Never show +/- sign.
        if (!hasCatalog)
        {
            tmpText.text = displayAmount.ToString();
            tmpText.color = kind == FloatingTextKind.Heal ? Color.green : Color.red;
            return;
        }

        var textStyle = style.textStyle;

        if (textStyle.fontAsset != null)
        {
            tmpText.font = textStyle.fontAsset;
        }

        if (textStyle.fontMaterialPreset != null)
        {
            tmpText.fontSharedMaterial = textStyle.fontMaterialPreset;
        }

        tmpText.fontStyle = textStyle.fontStyle;
        tmpText.color = textStyle.color;

        if (textStyle.enableAutoSize)
        {
            tmpText.enableAutoSizing = true;
            if (textStyle.fontSizeMin > 0)
                tmpText.fontSizeMin = textStyle.fontSizeMin;
            if (textStyle.fontSizeMax > 0)
                tmpText.fontSizeMax = textStyle.fontSizeMax;
            if (textStyle.fontSize > 0)
                tmpText.fontSize = textStyle.fontSize;
        }
        else
        {
            tmpText.enableAutoSizing = false;
            if (textStyle.fontSize > 0)
                tmpText.fontSize = textStyle.fontSize;
        }

        string prefix = string.IsNullOrEmpty(textStyle.prefix) ? string.Empty : textStyle.prefix;
        string suffix = string.IsNullOrEmpty(textStyle.suffix) ? string.Empty : textStyle.suffix;
        tmpText.text = $"{prefix}{displayAmount}{suffix}";

        HealthText healthText = go.GetComponent<HealthText>();
        if (healthText != null)
        {
            healthText.moveSpeed = style.motionStyle.moveSpeed;
            healthText.timeToFade = style.motionStyle.timeToFade;
        }
    }

    public void OnExitGame(InputAction.CallbackContext context)
    {
        if (context.started)
        {
            #if (UNITY_EDITOR || DEVELOPMENT_BUILD)
                        Debug.Log(this.name + ":" + this.GetType() + ":" + System.Reflection.MethodBase.GetCurrentMethod().Name);
            #endif
            #if (UNITY_EDITOR)
                        var editorAppType = System.Type.GetType("UnityEditor.EditorApplication, UnityEditor");
                        var isPlayingProp = editorAppType?.GetProperty("isPlaying");
                        if (isPlayingProp != null)
                        {
                            isPlayingProp.SetValue(null, false);
                        }
            #elif (UNITY_STANDALONE)
                        Application.Quit();
            #elif (UNITY_WEBGL)
                        SceneManager.LoadScene("QuitScene");
            #endif

        }
    }



}
