using System;
using System.Collections;
using UnityEngine;
using 国家系统;

/// <summary>
/// 战场管理器
/// 负责管理王城战的倒计时和战场生成
/// </summary>
public class 战场管理器 : MonoBehaviour
{
    public static 战场管理器 实例 { get; private set; }

    [Header("倒计时设置")]
    private const float 倒计时时长 = 30f; // 30秒倒计时

    // 当前倒计时状态
    private bool 是否正在倒计时 = false;
    private float 剩余时间 = 0f;
    private 国家信息库 当前战场国家 = null;

    private void Awake()
    {
        // 单例模式
        if (实例 != null && 实例 != this)
        {
            Destroy(gameObject);
            return;
        }

        实例 = this;
        DontDestroyOnLoad(gameObject);
        Debug.Log("战场管理器初始化完成");
    }

    /// <summary>
    /// 启动战场倒计时
    /// 当两个家族都宣战后调用此方法
    /// </summary>
    /// <param name="国家">宣战的国家</param>
    /// <param name="开始时间">战场开始时间（从服务器获取），如果为null则使用当前时间+30秒</param>
    public void 启动战场倒计时(国家信息库 国家, DateTime? 开始时间 = null)
    {
        // 检查宣战家族是否都已设置
        if (国家 == null)
        {
            Debug.LogError("国家信息为空，无法启动倒计时");
            return;
        }

        if (国家.宣战家族1 == null || 国家.宣战家族2 == null)
        {
            Debug.LogError("宣战家族未完整，无法启动倒计时");
            return;
        }

        // 如果倒计时已在进行，只更新国家信息（避免重复启动）
        if (是否正在倒计时)
        {
            当前战场国家 = 国家;
            Debug.Log("战场倒计时已在进行中，已更新国家信息");
            return;
        }

        // 设置倒计时状态
        是否正在倒计时 = true;
        当前战场国家 = 国家;

        // 计算剩余时间
        DateTime 实际开始时间 = 开始时间 ?? DateTime.Now.AddSeconds(倒计时时长);
        TimeSpan 时间差 = 实际开始时间 - DateTime.Now;
        剩余时间 = (float)时间差.TotalSeconds;

        if (剩余时间 <= 0)
        {
            Debug.LogWarning("战场开始时间已过，立即开始");
            剩余时间 = 0;
        }

        Debug.Log($"战场倒计时启动! {国家.宣战家族1.家族名字} VS {国家.宣战家族2.家族名字}，剩余时间: {剩余时间}秒");

        // 启动倒计时协程
        StartCoroutine(倒计时协程());
    }

    /// <summary>
    /// 同步服务端倒计时（从WebSocket推送的倒计时更新）
    /// </summary>
    /// <param name="国家">宣战的国家</param>
    /// <param name="剩余秒数">服务端推送的剩余秒数</param>
    public void 同步服务端倒计时(国家信息库 国家, int 剩余秒数)
    {
        // 检查宣战家族是否都已设置
        if (国家 == null)
        {
            return;
        }

        if (国家.宣战家族1 == null || 国家.宣战家族2 == null)
        {
            return;
        }

        // 如果倒计时未启动，先启动
        if (!是否正在倒计时)
        {
            启动战场倒计时(国家, DateTime.Now.AddSeconds(剩余秒数));
        }
        else
        {
            // 如果倒计时已在进行，直接同步剩余时间（使用服务端的时间，确保同步）
            剩余时间 = 剩余秒数;
            当前战场国家 = 国家;
            Debug.Log($"同步服务端倒计时: 剩余 {剩余秒数} 秒");
        }
    }

    // 是否已显示战场入口UI（避免重复显示）
    private bool 已显示战场入口UI = false;

    /// <summary>
    /// 倒计时协程
    /// 注意：倒计时主要依赖服务端推送同步，本地倒计时仅作为补充显示
    /// </summary>
    private IEnumerator 倒计时协程()
    {
        while (剩余时间 > 0f && 是否正在倒计时)
        {
            yield return new WaitForSeconds(1f); // 每秒更新一次
            
            // 本地倒计时递减（但主要依赖服务端推送同步）
            剩余时间 -= 1f;
            if (剩余时间 < 0) 剩余时间 = 0;

            // 当倒计时还剩15秒时，显示战场入口UI
            int 整数秒数 = Mathf.CeilToInt(剩余时间);
            if (整数秒数 <= 15 && !已显示战场入口UI)
            {
                已显示战场入口UI = true;
                显示战场入口UI();
            }

            // 输出倒计时信息（可以在这里添加UI显示）
            if (整数秒数 % 5 == 0 || 整数秒数 <= 10) // 每5秒或最后10秒输出
            {
                Debug.Log($"战场倒计时: {整数秒数}秒");
            }
        }

        // 倒计时结束
        if (是否正在倒计时)
        {
            是否正在倒计时 = false;
            Debug.Log("战场倒计时结束! 准备生成战场...");

            // 生成战场（这个功能以后实现）
            生成战场();
        }
    }

    /// <summary>
    /// 显示战场入口UI
    /// </summary>
    private void 显示战场入口UI()
    {
        // 查找战场入口管理器并显示UI
        战场入口管理器 入口管理器 = FindObjectOfType<战场入口管理器>();
        if (入口管理器 != null)
        {
            入口管理器.显示入口UI(当前战场国家);
        }
        else
        {
            Debug.LogWarning("未找到战场入口管理器组件，无法显示战场入口UI");
        }
    }

    /// <summary>
    /// 生成战场
    /// 倒计时结束后调用
    /// </summary>
    private void 生成战场()
    {
        if (当前战场国家 == null)
        {
            Debug.LogError("当前战场国家为空，无法生成战场");
            return;
        }

        if (当前战场国家.宣战家族1 == null || 当前战场国家.宣战家族2 == null)
        {
            Debug.LogError("宣战家族信息不完整，无法生成战场");
            return;
        }

        Debug.Log($"开始生成战场: {当前战场国家.宣战家族1.家族名字} VS {当前战场国家.宣战家族2.家族名字}");
        
        // TODO: 这里以后实现具体的战场生成逻辑
        // 例如：
        // - 创建战场场景
        // - 生成Boss
        // - 初始化双方家族积分
        // - 允许玩家进入战场等
        
        // 不再显示提示框，改为通过UI按钮状态来提示
        // 通用提示框.显示($"战场已开启! {当前战场国家.宣战家族1.家族名字} VS {当前战场国家.宣战家族2.家族名字}");
    }

    /// <summary>
    /// 获取当前倒计时剩余时间（秒）
    /// </summary>
    public float 获取剩余时间()
    {
        return 是否正在倒计时 ? 剩余时间 : 0f;
    }

    /// <summary>
    /// 检查是否正在倒计时
    /// </summary>
    public bool 是否倒计时中()
    {
        return 是否正在倒计时;
    }

    /// <summary>
    /// 获取当前战场国家
    /// </summary>
    public 国家信息库 获取当前战场国家()
    {
        return 当前战场国家;
    }

    /// <summary>
    /// 停止倒计时（如果需要取消战场）
    /// </summary>
    public void 停止倒计时()
    {
        if (是否正在倒计时)
        {
            是否正在倒计时 = false;
            剩余时间 = 0f;
            当前战场国家 = null;
            已显示战场入口UI = false;
            Debug.Log("战场倒计时已停止");
        }
    }
}

