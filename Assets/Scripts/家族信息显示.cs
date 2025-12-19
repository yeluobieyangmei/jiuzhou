using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using 玩家数据结构;
using 国家系统;

public class 家族信息显示 : MonoBehaviour
{
    public Button 退出家族按钮;
    public Button 解散家族按钮;
    //public 显示家族列表 显示家族列表;  显示家族列表尚未实现，此处先写补上格式 后续完善
    public void OnEnable()
    {
        刷新显示();
    }

    public void 刷新显示()
    {
        玩家数据 当前玩家 = 全局变量.所有玩家数据表[全局变量.当前身份];
        this.transform.GetChild(0).GetComponent<Text>().text = $"家族名称：{当前玩家.家族.家族名字}";
        this.transform.GetChild(1).GetComponent<Text>().text = $"家族族长：{全局变量.所有玩家数据表[当前玩家.家族.族长ID].姓名}";
        this.transform.GetChild(2).GetComponent<Text>().text = 当前玩家.ID == 当前玩家.家族.族长ID ? "我的职位：族长" : "我的职位族员";
        this.transform.GetChild(3).GetComponent<Text>().text = $"家族等级：{当前玩家.家族.家族等级}";
        this.transform.GetChild(4).GetComponent<Text>().text = $"家族人数：{当前玩家.家族.家族成员.Count}";
        this.transform.GetChild(5).GetComponent<Text>().text = $"家族繁荣：{当前玩家.家族.家族繁荣值}";
        this.transform.GetChild(6).GetComponent<Text>().text = $"国内排名：{当前玩家.家族.国家排名}";
        this.transform.GetChild(7).GetComponent<Text>().text = $"世界排名：{当前玩家.家族.世界排名}";
        this.transform.GetChild(8).GetComponent<Text>().text = $"家族资金：{当前玩家.家族.家族资金}";
        if (当前玩家.ID == 当前玩家.家族.族长ID)
        {
            退出家族按钮.gameObject.SetActive(false);
            解散家族按钮.gameObject.SetActive(true);
        }
        else
        {
            退出家族按钮.gameObject.SetActive(true);
            解散家族按钮.gameObject.SetActive(false);
        }
    }

    public void 点击加入家族()
    {
        //显示家族列表.gameObject.SetActive(true);   显示家族列表尚未实现，此处先写补上格式 后续完善
    }

    public void 解散家族()
    {
        玩家数据 当前玩家 = 全局变量.所有玩家数据表[全局变量.当前身份];
        if (当前玩家.解散家族())
        {
            刷新显示();
        }
    }

    public void 退出家族()
    {
        玩家数据 当前玩家 = 全局变量.所有玩家数据表[全局变量.当前身份];
        if (当前玩家.家族 != null)
        {
            if (当前玩家.家族.王城战是否战斗中)
            {
                通用提示框.显示("家族正在战斗中,不可操作!");
                return;
            }

            家族信息库 当前家族 = 当前玩家.家族;
            当前家族.家族成员.Remove(当前玩家);
            当前玩家.家族 = null;
            this.刷新显示();
            return;
        }
        通用提示框.显示("当前没有家族!");
        return;
    }

    public void 捐献()
    {
        玩家数据 当前玩家 = 全局变量.所有玩家数据表[全局变量.当前身份];
        if (当前玩家.黄金 > 1000)
        {
            当前玩家.黄金 -= 1000;
            当前玩家.家族.家族资金 += 100000;
            通用提示框.显示("捐献成功!家族资金+100");
            this.刷新显示();
            return;
        }
        else
        {
            通用提示框.显示("捐献失败!需要1000黄金");
            return;
        }
    }
}
