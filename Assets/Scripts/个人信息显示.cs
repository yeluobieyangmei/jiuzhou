using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using 玩家数据结构;

public class 个人信息显示 : MonoBehaviour
{
    public Text 生命值文本;
    public Text 攻击力文本;
    public Text 防御力文本;
    public Text 当前称号文本;
    public Text 成就文本;
    public Text 家族文本;
    public Text 国家文本;

    public void OnEnable()
    {
        刷新显示();
    }
    public void 刷新显示()
    {
        玩家数据 当前玩家 = 玩家数据管理.实例.当前玩家数据;
        生命值文本.text = $"生命值：{当前玩家.玩家属性.生命值}";
        攻击力文本.text = $"攻击力：{当前玩家.玩家属性.攻击力}";
        防御力文本.text = $"防御力：{当前玩家.玩家属性.防御力}";
        家族文本.text = 当前玩家.家族 == null ? "家族：暂未加入" : $"家族：{当前玩家.家族.家族名字}";
        国家文本.text = 当前玩家.国家 == null ? "国家：暂未加入" : $"国家：{当前玩家.国家.国名}({当前玩家.国家.国号})";
    }
}
