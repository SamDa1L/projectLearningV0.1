using UnityEngine;
using UnityEngine.UIElements;

public class ButtonClicker : MonoBehaviour
{
    private UIDocument _buttonDocument;
    private Button _uiButton;

    // 当前脚本仅用于测试 UI Toolkit 按钮点击回调；后续可替换为实际业务逻辑。
    private void OnEnable()
    {
        _buttonDocument = GetComponent<UIDocument>();
        if (_buttonDocument == null)
        {
            Debug.LogError("没找到 UIDocument 组件");
            return;
        }

        _uiButton = _buttonDocument.rootVisualElement.Q<Button>("TestButton");
        if (_uiButton == null)
        {
            Debug.LogError("没找到按钮：TestButton");
            return;
        }

        _uiButton.RegisterCallback<ClickEvent>(OnButtonClick);
    }

    private void OnDisable()
    {
        if (_uiButton != null)
        {
            _uiButton.UnregisterCallback<ClickEvent>(OnButtonClick);
        }
    }

    private void OnButtonClick(ClickEvent evt)
    {
        Debug.Log("按钮被点击");
    }
}
