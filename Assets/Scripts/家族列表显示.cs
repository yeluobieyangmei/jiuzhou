using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using 玩家数据结构;
using 国家系统;

public class 家族列表显示 : MonoBehaviour
{
    public Transform 父对象;
    public GameObject 要克隆的对象;
    List<GameObject> 克隆池 = new List<GameObject>();
    public 家族信息库 当前选中家族 = null;
    public 家族信息库 当前家族 { get; set; }
    public 显示类型 当前显示类型;
    public Button 申请加入按钮;
    public enum 显示类型
    {
        申请家族,
        国家排名查看,
        世界排名查看,
    }

    public void OnEnable()
    {
        刷新显示();
    }

    public void 刷新显示()
    {
        申请加入按钮.gameObject.SetActive(当前显示类型 == 显示类型.申请家族);
        要克隆的对象.gameObject.SetActive(false);
        国家信息库 玩家国家 = 全局变量.所有玩家数据表[全局变量.当前身份].国家;
        int count = 0;
        if (当前显示类型 == 显示类型.国家排名查看 || 当前显示类型 == 显示类型.申请家族)
        {
            count = 玩家国家.家族成员表.Count;
        }
        else if(当前显示类型 == 显示类型.世界排名查看)
        {
            count = 全局变量.所有家族列表.Count;
        }


        foreach (var obj in 克隆池)//遍历每个被克隆出来的对象
        {
            if (obj != null) Destroy(obj);//如果这个对象在unity中还在不是空的 就Destroy(obj)销毁这个对象
        }
        克隆池.Clear();

        for (int i = 0; i < count; i++)
        {
            GameObject 克隆对象 = Instantiate(要克隆的对象, 父对象);

            克隆对象.transform.GetChild(0).GetComponent<Text>().text = $"";//这里显示排名数字 如： 1.   2.  3.
            克隆对象.transform.GetChild(1).GetComponent<Text>().text = $"";//这里显示家族名字
            克隆对象.gameObject.SetActive(true);
            克隆池.Add(克隆对象);


            // 处理 Toggle 选择逻辑
            Toggle t = 克隆对象.GetComponent<Toggle>();//获取每个克隆对象上的Toggle组件
            家族信息库 捕获家族 = 全局变量.所有家族列表[i]; // 闭包捕获
            t.onValueChanged.AddListener(isOn => //如果这个对象被点击了，就把当前选中国家赋值为当前点中的国家
            {
                if (isOn)
                {
                    当前选中家族 = 捕获家族;
                    Debug.Log($"当前选中家族：{当前选中家族.家族名字}");
                }
            });
        }
    }

    public void 申请加入()
    {
        //目前限制 后面完善
    }
}
