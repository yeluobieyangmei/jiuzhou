using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using 玩家数据结构;
using 怪物数据结构;

public class 可攻击怪物展示界面 : MonoBehaviour
{
    public 攻击对象查看 攻击对象查看;
    public Transform 父对象;
    public GameObject 要克隆的对象;
    List<GameObject> 克隆池 = new List<GameObject>();
    public 怪物数据 当前选中怪物 = null;
    private List<怪物数据> 土匪列表 = new List<怪物数据>();
    private const int 目标怪物数量 = 10; // 保持列表中的怪物数量
    public void OnEnable()
    {
        //刷新显示();
    }

    public void 刷新显示()
    {
        要克隆的对象.gameObject.SetActive(false);
        int count = 土匪列表.Count;

        foreach (var obj in 克隆池)//遍历每个被克隆出来的对象
        {
            if (obj != null) Destroy(obj);//如果这个对象在unity中还在不是空的 就Destroy(obj)销毁这个对象
        }
        克隆池.Clear();

        for (int i = 0; i < count; i++)
        {
            GameObject 克隆对象 = Instantiate(要克隆的对象, 父对象);
            克隆对象.transform.GetChild(0).GetComponent<Text>().text = $"LV.{土匪列表[i].等级} {土匪列表[i].名称}";
            克隆对象.gameObject.SetActive(true);
            克隆池.Add(克隆对象);

            // 处理 Toggle 选择逻辑
            Toggle t = 克隆对象.GetComponent<Toggle>();//获取每个克隆对象上的Toggle组件
            怪物数据 捕获怪物 = 土匪列表[i]; // 闭包捕获
            t.onValueChanged.AddListener(isOn => //如果这个对象被点击了，就把当前选中国家赋值为当前点中的国家
            {
                if (isOn)
                {
                    当前选中怪物 = 捕获怪物;
                    攻击对象查看.当前怪物 = 捕获怪物;
                    攻击对象查看.gameObject.SetActive(true);
                }
            });
        }
    }

    public void 移除已击败怪物(怪物数据 被击败的怪物)
    {
        // 从土匪列表中移除被击败的怪物
        土匪列表.RemoveAll(怪物 => 怪物.ID == 被击败的怪物.ID);

        // 自动生成新怪物来补齐列表
        补齐怪物列表();

        // 重新刷新UI
        刷新显示();
    }

    public void 点击土匪按钮()
    {
        if (土匪列表.Count <= 0)
        {
            生成土匪();
            刷新显示();
        }
        else
        {
            刷新显示();
        }
    }
    /// <summary>
    /// 生成指定数量的土匪（用于初始化列表）
    /// </summary>
    private void 生成土匪()
    {
        // 联网版本：从模板生成怪物
        if (怪物模板管理.实例 == null)
        {
            Debug.LogError("怪物模板管理未初始化，无法生成怪物");
            return;
        }

        // 确保模板已加载
        if (!怪物模板管理.实例.是否已加载)
        {
            Debug.LogWarning("怪物模板尚未加载，正在加载...");
            怪物模板管理.实例.获取怪物模板();
            // 等待加载完成（这里简化处理，实际应该用协程等待）
            return;
        }

        服务端怪物模板数据 模板 = 怪物模板管理.实例.获取模板(怪物类型.土匪);
        if (模板 == null)
        {
            Debug.LogError("无法获取土匪模板");
            return;
        }

        // 生成指定数量的土匪（等级随机在基础等级附近）
        for (int i = 0; i < 目标怪物数量; i++)
        {
            生成单个怪物(模板);
        }
    }

    /// <summary>
    /// 生成单个怪物并添加到列表
    /// </summary>
    private void 生成单个怪物(服务端怪物模板数据 模板)
    {
        if (模板 == null)
        {
            Debug.LogError("模板为空，无法生成怪物");
            return;
        }

        // 等级在基础等级±2范围内随机
        int 随机等级 = 模板.baseLevel + Random.Range(-2, 3);
        随机等级 = Mathf.Max(1, 随机等级); // 至少1级

        // 根据模板创建怪物
        var 土匪 = 怪物数据.根据模板创建(模板, 随机等级);
        if (土匪 != null)
        {
            土匪列表.Add(土匪);
        }
    }

    /// <summary>
    /// 补齐怪物列表到目标数量
    /// </summary>
    private void 补齐怪物列表()
    {
        // 如果列表已满或超过目标数量，不需要补齐
        if (土匪列表.Count >= 目标怪物数量)
        {
            return;
        }

        // 检查模板管理是否可用
        if (怪物模板管理.实例 == null)
        {
            Debug.LogError("怪物模板管理未初始化，无法生成怪物");
            return;
        }

        // 确保模板已加载
        if (!怪物模板管理.实例.是否已加载)
        {
            Debug.LogWarning("怪物模板尚未加载，无法补齐怪物列表");
            return;
        }

        服务端怪物模板数据 模板 = 怪物模板管理.实例.获取模板(怪物类型.土匪);
        if (模板 == null)
        {
            Debug.LogError("无法获取土匪模板");
            return;
        }

        // 计算需要生成的数量
        int 需要生成数量 = 目标怪物数量 - 土匪列表.Count;

        // 生成缺失的怪物
        for (int i = 0; i < 需要生成数量; i++)
        {
            生成单个怪物(模板);
        }
    }


}
