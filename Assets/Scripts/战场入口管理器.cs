using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using 玩家数据结构;
using 国家系统;

/// <summary>
/// 战场入口管理器
/// 负责显示战场入口UI和倒计时
/// </summary>
public class 战场入口管理器 : MonoBehaviour
{
    [Header("入口UI组件")]
    public GameObject 战场入口弹窗;
    public Text 战场提示文本;
    public Button 进入战场按钮;
    public Button 稍后进入按钮;

    [Header("战场主界面")]
    public GameObject 战场主界面; //王城战战场UI

    // 当前战场信息
    private 国家信息库 当前战场国家 = null;
    private bool 是否正在显示 = false;

    private void Start()
    {
        // 初始隐藏UI
        if (战场入口弹窗 != null)
        {
            战场入口弹窗.SetActive(false);
        }
        
        // 初始隐藏进入战场按钮
        if (进入战场按钮 != null)
        {
            进入战场按钮.gameObject.SetActive(false);
        }
    }

    private void Update()
    {
        if (是否正在显示 && 当前战场国家 != null)
        {
            更新UI显示();
        }
    }

    /// <summary>
    /// 显示战场入口UI
    /// </summary>
    public void 显示入口UI(国家信息库 国家)
    {
        if (国家 == null || 国家.宣战家族1 == null || 国家.宣战家族2 == null)
        {
            Debug.LogWarning("战场国家信息不完整，无法显示入口UI");
            return;
        }

        当前战场国家 = 国家;
        是否正在显示 = true;

        if (战场入口弹窗 != null)
        {
            战场入口弹窗.SetActive(true);
        }

        // 初始隐藏进入战场按钮（倒计时结束后才显示）
        if (进入战场按钮 != null)
        {
            进入战场按钮.gameObject.SetActive(false);
        }

        Debug.Log("战场入口UI已显示");
    }

    /// <summary>
    /// 更新UI显示
    /// </summary>
    private void 更新UI显示()
    {
        玩家数据 当前玩家 = 玩家数据管理.实例?.当前玩家数据;
        if (当前玩家 == null || 当前玩家.家族 == null || 当前战场国家 == null)
        {
            return;
        }

        // 判断当前玩家属于哪个家族
        bool 是家族1 = 当前玩家.家族.家族ID == 当前战场国家.宣战家族1.家族ID;
        bool 是家族2 = 当前玩家.家族.家族ID == 当前战场国家.宣战家族2.家族ID;

        if (!是家族1 && !是家族2)
        {
            // 当前玩家不属于宣战家族，隐藏UI
            if (战场入口弹窗 != null)
            {
                战场入口弹窗.SetActive(false);
            }
            是否正在显示 = false;
            return;
        }

        // 获取对手家族名称
        string 对手家族名称 = 是家族1 ? 当前战场国家.宣战家族2.家族名字 : 当前战场国家.宣战家族1.家族名字;

        // 获取倒计时剩余时间
        float 剩余时间 = 0f;
        bool 倒计时中 = false;
        if (战场管理器.实例 != null)
        {
            剩余时间 = 战场管理器.实例.获取剩余时间();
            倒计时中 = 战场管理器.实例.是否倒计时中();
        }

        // 更新提示文本（倒计时直接显示在"剩余准备时间"中）
        if (战场提示文本 != null)
        {
            if (倒计时中 && 剩余时间 > 0)
            {
                战场提示文本.text = $"王城战即将开始！\n" +
                                  $"对手家族：{对手家族名称}\n" +
                                  $"剩余准备时间：{Mathf.CeilToInt(剩余时间)}秒\n\n" +
                                  $"请做好准备，战场即将开始！";
            }
            else
            {
                战场提示文本.text = $"王城战正在进行中！\n" +
                                  $"对手家族：{对手家族名称}\n\n" +
                                  $"是否立即加入战斗？";
            }
        }

        // 倒计时结束后，激活进入战场按钮
        if (进入战场按钮 != null)
        {
            if (!倒计时中 || 剩余时间 <= 0)
            {
                if (!进入战场按钮.gameObject.activeSelf)
                {
                    进入战场按钮.gameObject.SetActive(true);
                }
            }
        }
    }

    /// <summary>
    /// 点击进入战场按钮
    /// </summary>
    public void 点击进入战场()
    {
        if (战场主界面 != null)
        {
            战场主界面.SetActive(true);
            Debug.Log("已打开战场主界面");
        }
        else
        {
            Debug.LogWarning("战场主界面未设置");
        }

        // 隐藏入口UI
        if (战场入口弹窗 != null)
        {
            战场入口弹窗.SetActive(false);
        }
        是否正在显示 = false;
    }

    /// <summary>
    /// 点击稍后进入按钮
    /// </summary>
    public void 点击稍后进入()
    {
        // 隐藏入口UI（但不关闭，玩家可以稍后再打开）
        if (战场入口弹窗 != null)
        {
            战场入口弹窗.SetActive(false);
        }
        是否正在显示 = false;
        Debug.Log("玩家选择稍后进入战场");
    }
}