using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using 玩家数据结构;

public class 家族显示判断 : MonoBehaviour
{
    public 家族信息显示 家族信息显示;
    public GameObject 无家族界面;
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
}
