using UnityEngine;
using UnityEngine.UI;
using 玩家数据结构;

/// <summary>
/// 战场入口管理器
/// 负责管理战场入口UI的显示
/// </summary>
public class 战场入口管理器 : MonoBehaviour
{
    public static 战场入口管理器 实例 { get; private set; }

    [Header("窗口对象")]
    public GameObject 窗口对象;
    public Text 内容文本;
    public Button 进入战场按钮;
    public Button 稍后进入按钮;

    // 当前倒计时信息
    private int 当前剩余秒数 = -1;
    private string 家族1名称 = "";
    private string 家族2名称 = "";
    private bool 倒计时已结束 = false;

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

        // 初始隐藏窗口
        if (窗口对象 != null)
        {
            窗口对象.SetActive(false);
        }

        // 初始隐藏进入战场按钮
        if (进入战场按钮 != null)
        {
            进入战场按钮.gameObject.SetActive(false);
        }

        Debug.Log("[战场入口管理器] 初始化完成");
    }

    /// <summary>
    /// 更新倒计时信息
    /// </summary>
    public void 更新倒计时信息(int 剩余秒数, string 家族1名称, string 家族2名称)
    {
        当前剩余秒数 = 剩余秒数;
        this.家族1名称 = 家族1名称;
        this.家族2名称 = 家族2名称;
        倒计时已结束 = false;
        刷新信息();
    }

    /// <summary>
    /// 标记倒计时结束
    /// </summary>
    public void 标记倒计时结束()
    {
        倒计时已结束 = true;
        当前剩余秒数 = 0;
        刷新信息();
        
        // 显示进入战场按钮
        if (进入战场按钮 != null)
        {
            进入战场按钮.gameObject.SetActive(true);
            Debug.Log("[战场入口管理器] 倒计时结束，已显示进入战场按钮");
        }
    }

    /// <summary>
    /// 刷新信息显示
    /// </summary>
    public void 刷新信息()
    {
        if (内容文本 == null)
        {
            Debug.LogWarning("[战场入口管理器] 内容文本未设置");
            return;
        }

        // 确定对手家族名称
        玩家数据 当前玩家 = 玩家数据管理.实例?.当前玩家数据;
        string 对手家族名称 = "";
        if (当前玩家 != null && 当前玩家.家族 != null)
        {
            // 如果当前玩家属于家族1，对手是家族2；反之亦然
            // 这里简化处理，显示两个家族名称
            if (当前玩家.家族.家族名字 == 家族1名称)
            {
                对手家族名称 = 家族2名称;
            }
            else if (当前玩家.家族.家族名字 == 家族2名称)
            {
                对手家族名称 = 家族1名称;
            }
            else
            {
                // 如果无法确定，显示两个家族
                对手家族名称 = $"{家族1名称} VS {家族2名称}";
            }
        }
        else
        {
            对手家族名称 = $"{家族1名称} VS {家族2名称}";
        }

        // 根据倒计时状态显示不同内容
        if (倒计时已结束)
        {
            // 倒计时结束，战场开始
            内容文本.text = $"王城战已经开始！\n" +
                              $"对手家族：{对手家族名称}\n\n" +
                              $"战场已开启，是否立即加入战斗？";
        }
        else if (当前剩余秒数 >= 0)
        {
            // 倒计时进行中
            内容文本.text = $"王城战即将开始！\n" +
                              $"对手家族：{对手家族名称}\n" +
                              $"剩余准备时间：{当前剩余秒数}秒\n\n" +
                              $"请做好准备，战场即将开始！";
        }
        else
        {
            // 默认状态
            内容文本.text = $"王城战准备中...";
        }
    }

    /// <summary>
    /// 显示战场入口窗口（倒计时15秒时调用）
    /// </summary>
    public void 显示窗口()
    {
        if (窗口对象 != null)
        {
            窗口对象.SetActive(true);
            Debug.Log("[战场入口管理器] 窗口已显示");
        }
        else
        {
            Debug.LogWarning("[战场入口管理器] 窗口对象未设置");
        }

        // 隐藏进入战场按钮（倒计时期间不显示）
        if (进入战场按钮 != null)
        {
            进入战场按钮.gameObject.SetActive(false);
            Debug.Log("[战场入口管理器] 进入战场按钮已隐藏");
        }
    }

    /// <summary>
    /// 隐藏战场入口窗口
    /// </summary>
    public void 隐藏窗口()
    {
        if (窗口对象 != null)
        {
            窗口对象.SetActive(false);
            Debug.Log("[战场入口管理器] 窗口已隐藏");
        }
    }

    public void 稍后进入()
    {
        窗口对象.gameObject.SetActive(false);
    }
}
