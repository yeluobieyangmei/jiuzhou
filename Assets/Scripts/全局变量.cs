using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using 国家系统;
using 玩家数据结构;

public class 全局变量
{

    public static void SetActive(GameObject go, bool state)
    {
        if (go == null)
        {
            return;
        }
        if (go.activeSelf != state)
        {
            go.SetActive(state);
        }
    }
    public static List<玩家数据> 所有玩家数据表 = new List<玩家数据>();
    public static List<国家信息库> 所有国家列表 = new List<国家信息库>();
    public static List<家族信息库> 所有家族列表 = new List<家族信息库>();

    public static int 当前身份 = 0;
    public static int 国家ID记录 = 0;
    public static int 家族ID记录 = 0;
    public static int 创建AI玩家数 = 270;
}
