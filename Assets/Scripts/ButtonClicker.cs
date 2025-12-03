using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UIElements;

public class ButtonClicker : MonoBehaviour
{
    UIDocument buttonDocument;
    Button uiButton;


    //OnEnable当前的方法内容只是单纯测试控件按钮功能
    //ToDo 后续需要把这部分内容换成具有实际效果的方法
    private void OnEnable()
    {
        buttonDocument = GetComponent<UIDocument>();

        if (buttonDocument == null)
        {
            Debug.LogError("没找到button Document");
        }

        uiButton = buttonDocument.rootVisualElement.Q("TestButton") as Button;

        if (uiButton != null) 
        {
            Debug.Log("Button找到了");
        }


        uiButton.RegisterCallback<ClickEvent>(OnButtonClick);


    }


    public void OnButtonClick(ClickEvent evt)
    {
        Debug.Log("这个按键被按下了");
    }


    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
