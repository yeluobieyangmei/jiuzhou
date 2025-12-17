using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using 国家系统;

public class 全局方法类 : MonoBehaviour
{
    public static 国家信息库 获取指定名字的国家(string 名字)
    {
        int count = 全局变量.所有国家列表.Count;
        for (int i = 0; i < count; i++)
        {
            if (全局变量.所有国家列表[i].国名 == 名字)
            {
                return 全局变量.所有国家列表[i];
            }
        }
        return null;
    }

    public static 家族信息库 获取指定名字的家族(string 名字)
    {
        int count = 全局变量.所有家族列表.Count;
        for (int i = 0; i < count; i++)
        {
            if (全局变量.所有家族列表[i].家族名字 == 名字)
            {
                return 全局变量.所有家族列表[i];
            }
        }
        return null;
    }
}
