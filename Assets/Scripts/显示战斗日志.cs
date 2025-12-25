using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class 显示战斗日志 : MonoBehaviour
{
    public Text 标题;
    public void 关闭()
    {
        战斗日志管理器.实例.清空日志();
        gameObject.SetActive(false);
    }
}
