using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using 玩家数据结构;

public class 家族显示判断 : MonoBehaviour
{
    public static 家族显示判断 实例 { get; private set; }

    public 家族信息显示 家族信息显示;
    public GameObject 无家族界面;
    public 家族列表显示 家族列表显示;
    
    private void Awake()
    {
        // 单例模式
        if (实例 != null && 实例 != this)
        {
            Destroy(gameObject);
            return;
        }
        实例 = this;
    }
    
    private void OnEnable()
    {
        刷新显示();
    }

    public void 刷新显示()
    {
        玩家数据 当前玩家 = 玩家数据管理.实例.当前玩家数据;
        if (当前玩家.家族 != null)
        {
            家族信息显示.gameObject.SetActive(true);
            无家族界面.gameObject.SetActive(false);
        }
        else
        {
            家族信息显示.gameObject.SetActive(false);
            无家族界面.gameObject.SetActive(true);
        }
    }

    public void 点击加入家族按钮()
    {
        家族列表显示.当前显示类型 = 家族列表显示.显示类型.申请家族;
        家族列表显示.gameObject.SetActive(true);
    }
}
