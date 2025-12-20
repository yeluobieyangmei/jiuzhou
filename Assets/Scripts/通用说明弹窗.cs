using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System;

public class 通用说明弹窗 : MonoBehaviour
{
    public static 通用说明弹窗 实例;

    public GameObject 面板;   // 指向真正的弹窗UI面板，可以默认失活
    public Text 标题文本;
    public Text 内容文本;
    public Button 确定按钮;
    public Button 取消按钮;

    // 确定按钮的回调方法（无参数）
    private Action 确定回调;

    private void Awake()
    {
        实例 = this;   // 空对象始终激活，所以 Awake 一定在场景开始时执行
        
        // 如果确定按钮存在，绑定点击事件
        if (确定按钮 != null)
        {
            确定按钮.onClick.AddListener(点击确定);
        }
        
        // 如果取消按钮存在，绑定点击事件
        if (取消按钮 != null)
        {
            取消按钮.onClick.AddListener(点击取消);
        }
    }

    /// <summary>
    /// 显示说明弹窗
    /// </summary>
    /// <param name="标题">弹窗标题</param>
    /// <param name="内容">弹窗内容</param>
    /// <param name="确定方法">确定按钮的回调方法（可选）</param>
    public static void 显示(string 标题, string 内容, Action 确定方法 = null)
    {
        if (实例 == null)
        {
            Debug.LogError("通用说明弹窗.实例 未初始化");
            return;
        }

        if (实例.标题文本 != null)
        {
            实例.标题文本.text = 标题;
        }
        
        if (实例.内容文本 != null)
        {
            实例.内容文本.text = 内容;
        }

        实例.确定回调 = 确定方法;
        实例.面板.SetActive(true);
    }

    /// <summary>
    /// 隐藏说明弹窗
    /// </summary>
    public static void 隐藏()
    {
        if (实例 == null) return;
        实例.面板.SetActive(false);
        实例.确定回调 = null;
    }

    /// <summary>
    /// 点击确定按钮
    /// </summary>
    private void 点击确定()
    {
        // 如果有回调方法，先执行回调
        if (确定回调 != null)
        {
            确定回调();
        }
        
        // 关闭弹窗
        隐藏();
    }

    /// <summary>
    /// 点击取消按钮
    /// </summary>
    private void 点击取消()
    {
        // 关闭弹窗（不执行确定回调）
        隐藏();
    }
}
