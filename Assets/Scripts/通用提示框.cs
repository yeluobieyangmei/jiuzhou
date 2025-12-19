using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class 通用提示框 : MonoBehaviour
{
    public static 通用提示框 Instance;

    public GameObject 面板;   // 指向真正的提示框UI面板，可以默认失活
    public Text 提示文本;

    private void Awake()
    {
        Instance = this;   // 空对象始终激活，所以 Awake 一定在场景开始时执行
    }

    public static void 显示(string 文本)
    {
        if (Instance == null)
        {
            Debug.LogError("通用提示框.Instance 未初始化");
            return;
        }

        Instance.提示文本.text = 文本;
        Instance.面板.SetActive(true);
    }

    public static void 隐藏()
    {
        if (Instance == null) return;
        Instance.面板.SetActive(false);
        Debug.Log("关闭面板");
    }
}
