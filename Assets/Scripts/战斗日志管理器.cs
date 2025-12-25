using UnityEngine;
using UnityEngine.UI;
using System.Text;
using System.Collections.Generic;

public class 战斗日志管理器 : MonoBehaviour
{
    // 单例模式
    public static 战斗日志管理器 实例;

    [Header("UI引用")]
    public Text 战斗日志文本;  // 在Unity编辑器中拖拽赋值
    public ScrollRect 滚动视图; // 在Unity编辑器中拖拽赋值

    [Header("性能设置")]
    [Tooltip("日志更新间隔（秒）")]
    public float 更新间隔 = 0.1f;
    [Tooltip("最大日志行数")]
    public int 最大日志行数 = 100;
    [Tooltip("是否启用自动滚动")]
    public bool 自动滚动到底部 = true;

    // 私有变量
    private StringBuilder 日志内容 = new StringBuilder(1024); // 预分配1KB内存
    private Queue<string> 日志队列 = new Queue<string>();
    private float 上次更新时间;
    private bool 需要更新UI;
    private int 当前行数 = 0;

    private void Awake()
    {
        // 单例模式初始化
        if (实例 == null)
        {
            实例 = this;
            // 可选：DontDestroyOnLoad(gameObject); // 如果需要跨场景保留
        }
        else if (实例 != this)
        {
            Destroy(gameObject);
            return;
        }

        // 初始化UI
        if (战斗日志文本 != null)
        {
            战斗日志文本.supportRichText = true; // 启用富文本
            战斗日志文本.text = "";
        }
    }

    private void Update()
    {
        // 节流更新UI
        if (需要更新UI && Time.unscaledTime - 上次更新时间 >= 更新间隔)
        {
            更新UI();
            需要更新UI = false;
            上次更新时间 = Time.unscaledTime;
        }
    }

    // 添加一条日志
    public void 添加日志(string 内容, 日志类型 类型 = 日志类型.普通)
    {
        string 带格式的日志 = 格式化日志(内容, 类型);

        // 添加到队列
        lock (日志队列)
        {
            日志队列.Enqueue(带格式的日志);
            当前行数++;

            // 移除最旧的日志（如果超过最大行数）
            while (当前行数 > 最大日志行数 && 日志队列.Count > 0)
            {
                日志队列.Dequeue();
                当前行数--;
            }
        }

        需要更新UI = true;
    }

    // 清空所有日志
    public void 清空日志()
    {
        lock (日志队列)
        {
            日志队列.Clear();
            日志内容.Clear();
            当前行数 = 0;
        }

        if (战斗日志文本 != null)
        {
            战斗日志文本.text = "";
        }
    }

    // 更新UI显示
    // 更新UI显示
    private void 更新UI()
    {
        if (战斗日志文本 == null) return;

        // 合并所有日志
        StringBuilder 新内容 = new StringBuilder(日志内容.Capacity);
        lock (日志队列)
        {
            foreach (string 日志 in 日志队列)
            {
                新内容.AppendLine(日志);
            }
            日志内容 = 新内容;  // 这里修正了变量名
        }

        // 更新UI
        战斗日志文本.text = 新内容.ToString();

        // 自动滚动到底部
        if (自动滚动到底部 && 滚动视图 != null)
        {
            Canvas.ForceUpdateCanvases();
            滚动视图.verticalNormalizedPosition = 0f;
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)滚动视图.transform);
        }
    }

    // 格式化日志（支持颜色和样式）
    private string 格式化日志(string 内容, 日志类型 类型)
    {
        switch (类型)
        {
            case 日志类型.伤害:
                return $"<color=#FF6B6B>{内容}</color>"; // 红色
            case 日志类型.治疗:
                return $"<color=#6BFF6B>{内容}</color>"; // 绿色
            case 日志类型.重要:
                return $"<b>{内容}</b>"; // 加粗
            case 日志类型.系统:
                return $"<color=#4A90E2>{内容}</color>"; // 蓝色
            default:
                return 内容;
        }
    }

    // 日志类型枚举
    public enum 日志类型
    {
        普通,
        伤害,
        治疗,
        重要,
        系统
    }

    // 在游戏退出时清理
    private void OnDestroy()
    {
        if (实例 == this)
        {
            实例 = null;
        }
    }

    /*   日志的使用方法
    
    // 添加普通日志
    战斗日志管理器.实例.添加日志("战斗开始！");

    // 添加带类型的日志
    战斗日志管理器.实例.添加Logger.AddLog("造成50点伤害", 战斗日志管理器.日志类型.伤害);
    战斗日志管理器.实例.添加Logger.AddLog("恢复30点生命", 战斗日志管理器.日志类型.治疗);
    战斗日志管理器.实例.添加Logger.AddLog("Boss出现！", 战斗日志管理器.日志类型.重要);

    // 清空日志
    战斗日志管理器.实例.清空日志();
    */
}