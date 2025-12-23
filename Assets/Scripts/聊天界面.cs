using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// 客户端聊天界面（输入框 + 发送 + 频道切换 + 消息显示）
/// 参考你提供的旧项目：一个 ScrollRect + 一个 Content + 一个 文本Item，实现频道切换显示
/// </summary>
public class 聊天界面 : MonoBehaviour
{
    public static 聊天界面 实例 { get; private set; }

    [Header("滚动视图")]
    public ScrollRect 聊天滚动视图;

    [Header("消息列表根节点（Content）")]
    public Transform 消息Root;

    [Header("消息Item 模板（挂 Text 组件）")]
    public GameObject 消息Item;

    [Header("频道切换区根节点（里面放 全部/世界/国家/家族/系统 的 Toggle）")]
    public Transform 消息类型选择Root;

    [Header("输入框和发送按钮")]
    public InputField 消息输入框;
    public Button 发送按钮;
    public Text 发送按钮文本;

    [Header("接口地址")]
    private string 发送世界消息地址 = "http://43.139.181.191:5000/api/sendWorldMessage";
    private string 发送国家消息地址 = "http://43.139.181.191:5000/api/sendCountryMessage";
    private string 发送家族消息地址 = "http://43.139.181.191:5000/api/sendClanMessage";
    private string 获取世界消息地址 = "http://43.139.181.191:5000/api/getWorldMessages?limit=10";
    private string 获取国家消息地址 = "http://43.139.181.191:5000/api/getCountryMessages?limit=10";
    private string 获取家族消息地址 = "http://43.139.181.191:5000/api/getClanMessages?limit=10";

    private Toggle[] 所有选择Toggle;

    /// <summary>
    /// 当前界面选中的显示频道（用于筛选显示）
    /// </summary>
    private 聊天频道 当前消息类型 = 聊天频道.世界;

    private readonly List<本地聊天消息> 当前显示消息数据 = new List<本地聊天消息>();

    private readonly List<GameObject> 消息预制件列表 = new List<GameObject>();

    /// <summary>
    /// 本地聊天数据（只做客户端显示缓存）
    /// </summary>
    public 本地聊天消息数据 数据 = new 本地聊天消息数据();

    // 发送冷却相关
    private float 最后发送时间 = 0f;
    private const float 发送冷却时间 = 5f; // 5秒
    private bool 是否禁言 = false;
    private Coroutine 冷却倒计时协程;

    private void Awake()
    {
        if (实例 != null && 实例 != this)
        {
            Destroy(gameObject);
            return;
        }

        实例 = this;
    }

    private void Start()
    {
        初始化();
        // 启动加载历史消息
        StartCoroutine(加载历史消息());
    }

    /// <summary>
    /// 初始化 UI 事件
    /// </summary>
    public void 初始化()
    {
        // 频道 Toggle
        if (消息类型选择Root != null)
        {
            所有选择Toggle = 消息类型选择Root.GetComponentsInChildren<Toggle>();
            for (int i = 0; i < 所有选择Toggle.Length; i++)
            {
                int index = i; // 捕获索引
                所有选择Toggle[i].onValueChanged.AddListener(isOn =>
                {
                    if (!isOn) return;
                    当前消息类型 = (聊天频道)index;
                    刷新显示();
                    更新发送UI状态();
                });
            }
        }

        // 发送按钮
        if (发送按钮 != null)
        {
            发送按钮.onClick.AddListener(发送消息);
        }

        // 输入框字数限制
        if (消息输入框 != null)
        {
            消息输入框.onValueChanged.AddListener(更新字数提示);
        }

        // 模板隐藏
        if (消息Item != null)
        {
            消息Item.SetActive(false);
        }

        // 默认选中世界频道
        if (所有选择Toggle != null && 所有选择Toggle.Length > (int)聊天频道.世界)
        {
            所有选择Toggle[(int)聊天频道.世界].isOn = true;
            当前消息类型 = 聊天频道.世界;
        }

        更新发送UI状态();
        刷新显示();
    }

    private void OnDestroy()
    {
        if (发送按钮 != null)
        {
            发送按钮.onClick.RemoveListener(发送消息);
        }

        if (消息输入框 != null)
        {
            消息输入框.onValueChanged.RemoveListener(更新字数提示);
        }

        if (所有选择Toggle != null)
        {
            foreach (var t in 所有选择Toggle)
            {
                t.onValueChanged.RemoveAllListeners();
            }
        }
    }

    /// <summary>
    /// 从外部（网络、服务器事件）接收一条新消息
    /// </summary>
    public void 接收新消息(聊天频道 channel, string 完整文本)
    {
        if (string.IsNullOrEmpty(完整文本)) return;

        数据.添加消息(channel, 完整文本);

        // 如果当前频道能看到这条消息，就刷新
        if (当前消息类型 == 聊天频道.全部 || 当前消息类型 == channel)
        {
            刷新显示();
        }
    }

    /// <summary>
    /// 获取当前要显示的消息列表
    /// </summary>
    private void 获取当前显示消息列表()
    {
        当前显示消息数据.Clear();
        var 列表 = 数据.获取消息(当前消息类型);
        if (列表 != null)
        {
            当前显示消息数据.AddRange(列表);
        }
    }

    /// <summary>
    /// 刷新界面显示
    /// </summary>
    public void 刷新显示()
    {
        if (消息Root == null || 消息Item == null) return;

        获取当前显示消息列表();

        int 消息数 = 当前显示消息数据.Count;
        int 物体数 = 消息预制件列表.Count;
        int max = Math.Max(消息数, 物体数);

        GameObject go;
        for (int i = 0; i < max; i++)
        {
            if (i < 物体数)
            {
                go = 消息预制件列表[i];
            }
            else
            {
                go = GameObject.Instantiate(消息Item, 消息Root);
                消息预制件列表.Add(go);
            }

            if (i < 消息数)
            {
                var msg = 当前显示消息数据[i];
                Text t = go.GetComponent<Text>();
                if (t != null)
                {
                    t.text = msg.文本;
                    t.color = (msg.频道 == 聊天频道.系统) ? Color.red : Color.white;
                }
                go.SetActive(true);
            }
            else
            {
                go.SetActive(false);
            }
        }

        StartCoroutine(滚动到最新());
    }

    /// <summary>
    /// 协程：自动滚动到底部
    /// </summary>
    private IEnumerator 滚动到最新()
    {
        if (聊天滚动视图 == null)
            yield break;

        // 等一帧，确保 Layout/ContentSizeFitter 刷新完成
        yield return null;

        Canvas.ForceUpdateCanvases();

        // 0 通常是底部，如果方向反了，可以改成 1
        聊天滚动视图.verticalNormalizedPosition = 0f;
    }

    /// <summary>
    /// 文本长度限制（20字）
    /// </summary>
    private void 更新字数提示(string 文本)
    {
        int 当前字数 = 文本.Length;
        int 最大字数 = 20;

        if (当前字数 > 最大字数 && 消息输入框 != null)
        {
            通用提示框.显示("消息不可超过20字!");
            消息输入框.text = 文本.Substring(0, 最大字数);
        }
    }

    /// <summary>
    /// 当前频道是否允许发送（世界/国家/家族）
    /// </summary>
    private bool 当前频道可发送()
    {
        return 当前消息类型 == 聊天频道.世界
               || 当前消息类型 == 聊天频道.国家
               || 当前消息类型 == 聊天频道.家族;
    }

    /// <summary>
    /// 根据当前频道和禁言/冷却状态更新输入框和按钮
    /// </summary>
    private void 更新发送UI状态()
    {
        bool 可发送频道 = 当前频道可发送();

        if (消息输入框 != null)
        {
            消息输入框.interactable = 可发送频道 && !是否禁言;
        }

        if (发送按钮 != null)
        {
            // 冷却期间，协程里会控制按钮状态，这里只负责非冷却时的基础状态
            if (冷却倒计时协程 == null)
            {
                发送按钮.interactable = 可发送频道 && !是否禁言;
            }
        }
    }

    /// <summary>
    /// 点击发送按钮
    /// </summary>
    public void 发送消息()
    {
        if (消息输入框 == null) return;

        if (!当前频道可发送())
        {
            通用提示框.显示("当前频道不能发送消息");
            return;
        }

        string 消息内容 = 消息输入框.text.Trim();
        if (string.IsNullOrEmpty(消息内容))
        {
            通用提示框.显示("消息内容不能为空");
            return;
        }

        // 冷却检查
        float 当前时间 = Time.time;
        if (当前时间 - 最后发送时间 < 发送冷却时间)
        {
            float 剩余时间 = 发送冷却时间 - (当前时间 - 最后发送时间);
            通用提示框.显示($"发送消息过于频繁，请{Mathf.CeilToInt(剩余时间)}秒后再试");
            return;
        }

        // 禁言检查
        if (是否禁言)
        {
            通用提示框.显示("你已被禁言，无法发送消息");
            return;
        }

        // 根据当前频道发送消息
        switch (当前消息类型)
        {
            case 聊天频道.世界:
                StartCoroutine(发送世界消息(消息内容));
                break;
            case 聊天频道.国家:
                StartCoroutine(发送国家消息(消息内容));
                break;
            case 聊天频道.家族:
                StartCoroutine(发送家族消息(消息内容));
                break;
        }

        // 清空输入框
        消息输入框.text = "";
    }

    /// <summary>
    /// 发送世界消息
    /// </summary>
    private IEnumerator 发送世界消息(string 消息内容)
    {
        int accountId = PlayerPrefs.GetInt("AccountId", -1);
        if (accountId <= 0)
        {
            通用提示框.显示("未登录，无法发送消息");
            yield break;
        }

        var request = new UnityWebRequest(发送世界消息地址, "POST");
        string jsonBody = $"{{\"accountId\":{accountId},\"message\":\"{消息内容}\"}}";
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            var response = JsonUtility.FromJson<发送消息响应>(request.downloadHandler.text);
            if (response.success)
            {
                最后发送时间 = Time.time;
                启动冷却倒计时();

                // 本地立即显示自己发送的世界消息
                string 玩家名称 = 玩家数据管理.实例?.当前玩家数据?.姓名 ?? "我";
                string 时间 = DateTime.Now.ToString("HH:mm");
                string 完整 = $"{时间} {玩家名称} {消息内容}";
                接收新消息(聊天频道.世界, 完整);

                // 重新获取玩家数据以更新黄金显示
                if (accountId > 0 && 玩家数据管理.实例 != null)
                {
                    玩家数据管理.实例.获取玩家数据(accountId);
                }
            }
            else
            {
                通用提示框.显示(response.message);
                if (response.message.Contains("禁言"))
                {
                    设置禁言状态(true);
                }
            }
        }
        else
        {
            通用提示框.显示("发送消息失败：" + request.error);
        }

        request.Dispose();
    }

    /// <summary>
    /// 发送国家消息
    /// </summary>
    private IEnumerator 发送国家消息(string 消息内容)
    {
        int accountId = PlayerPrefs.GetInt("AccountId", -1);
        if (accountId <= 0)
        {
            通用提示框.显示("未登录，无法发送消息");
            yield break;
        }

        var request = new UnityWebRequest(发送国家消息地址, "POST");
        string jsonBody = $"{{\"accountId\":{accountId},\"message\":\"{消息内容}\"}}";
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            var response = JsonUtility.FromJson<发送消息响应>(request.downloadHandler.text);
            if (response.success)
            {
                最后发送时间 = Time.time;
                启动冷却倒计时();

                // 本地立即显示自己发送的国家消息
                string 玩家名称 = 玩家数据管理.实例?.当前玩家数据?.姓名 ?? "我";
                string 时间 = DateTime.Now.ToString("HH:mm");
                string 完整 = $"{时间} {玩家名称} {消息内容}";
                接收新消息(聊天频道.国家, 完整);

                // 重新获取玩家数据以更新黄金显示（国家消息不扣黄金，但为了保持一致性也刷新）
                if (accountId > 0 && 玩家数据管理.实例 != null)
                {
                    玩家数据管理.实例.获取玩家数据(accountId);
                }
            }
            else
            {
                通用提示框.显示(response.message);
                if (response.message.Contains("禁言"))
                {
                    设置禁言状态(true);
                }
            }
        }
        else
        {
            通用提示框.显示("发送消息失败：" + request.error);
        }

        request.Dispose();
    }

    /// <summary>
    /// 发送家族消息
    /// </summary>
    private IEnumerator 发送家族消息(string 消息内容)
    {
        int accountId = PlayerPrefs.GetInt("AccountId", -1);
        if (accountId <= 0)
        {
            通用提示框.显示("未登录，无法发送消息");
            yield break;
        }

        var request = new UnityWebRequest(发送家族消息地址, "POST");
        string jsonBody = $"{{\"accountId\":{accountId},\"message\":\"{消息内容}\"}}";
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            var response = JsonUtility.FromJson<发送消息响应>(request.downloadHandler.text);
            if (response.success)
            {
                最后发送时间 = Time.time;
                启动冷却倒计时();

                // 本地立即显示自己发送的家族消息
                string 玩家名称 = 玩家数据管理.实例?.当前玩家数据?.姓名 ?? "我";
                string 时间 = DateTime.Now.ToString("HH:mm");
                string 完整 = $"{时间} {玩家名称} {消息内容}";
                接收新消息(聊天频道.家族, 完整);

                // 重新获取玩家数据以更新黄金显示（家族消息不扣黄金，但为了保持一致性也刷新）
                if (accountId > 0 && 玩家数据管理.实例 != null)
                {
                    玩家数据管理.实例.获取玩家数据(accountId);
                }
            }
            else
            {
                通用提示框.显示(response.message);
                if (response.message.Contains("禁言"))
                {
                    设置禁言状态(true);
                }
            }
        }
        else
        {
            通用提示框.显示("发送消息失败：" + request.error);
        }

        request.Dispose();
    }

    /// <summary>
    /// 启动冷却倒计时
    /// </summary>
    private void 启动冷却倒计时()
    {
        if (冷却倒计时协程 != null)
        {
            StopCoroutine(冷却倒计时协程);
        }
        冷却倒计时协程 = StartCoroutine(冷却倒计时协程方法());
    }

    /// <summary>
    /// 冷却倒计时协程
    /// </summary>
    private IEnumerator 冷却倒计时协程方法()
    {
        if (发送按钮文本 == null || 发送按钮 == null) yield break;

        发送按钮.interactable = false;

        float 剩余时间 = 发送冷却时间;
        while (剩余时间 > 0)
        {
            发送按钮文本.text = $"发送({Mathf.CeilToInt(剩余时间)}s)";
            yield return new WaitForSeconds(1f);
            剩余时间 -= 1f;
        }

        发送按钮文本.text = "发送";
        冷却倒计时协程 = null;
        更新发送UI状态();
    }

    /// <summary>
    /// 设置禁言状态（由服务器返回“禁言”提示时调用）
    /// </summary>
    public void 设置禁言状态(bool 禁言)
    {
        是否禁言 = 禁言;
        更新发送UI状态();
    }

    /// <summary>
    /// 加载历史消息（世界/国家/家族）
    /// </summary>
    private IEnumerator 加载历史消息()
    {
        yield return 加载世界消息();
        yield return 加载国家消息();
        yield return 加载家族消息();
        刷新显示();
    }

    private IEnumerator 加载世界消息()
    {
        if (string.IsNullOrEmpty(获取世界消息地址)) yield break;

        var request = UnityWebRequest.Get(获取世界消息地址);
        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            var response = JsonUtility.FromJson<获取消息响应>(request.downloadHandler.text);
            if (response.success && response.data != null)
            {
                response.data.Reverse(); // 从旧到新
                foreach (var msg in response.data)
                {
                    string 时间 = DateTime.Parse(msg.messageTime).ToString("HH:mm");
                    string 完整 = $"{时间} {msg.playerName} {msg.message}";
                    数据.添加消息(聊天频道.世界, 完整);
                }
            }
        }

        request.Dispose();
    }

    private IEnumerator 加载国家消息()
    {
        if (string.IsNullOrEmpty(获取国家消息地址)) yield break;

        var request = UnityWebRequest.Get(获取国家消息地址);
        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            var response = JsonUtility.FromJson<获取消息响应>(request.downloadHandler.text);
            if (response.success && response.data != null)
            {
                response.data.Reverse();
                foreach (var msg in response.data)
                {
                    string 时间 = DateTime.Parse(msg.messageTime).ToString("HH:mm");
                    string 完整 = $"{时间} {msg.playerName} {msg.message}";
                    数据.添加消息(聊天频道.国家, 完整);
                }
            }
        }

        request.Dispose();
    }

    private IEnumerator 加载家族消息()
    {
        if (string.IsNullOrEmpty(获取家族消息地址)) yield break;

        var request = UnityWebRequest.Get(获取家族消息地址);
        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            var response = JsonUtility.FromJson<获取消息响应>(request.downloadHandler.text);
            if (response.success && response.data != null)
            {
                response.data.Reverse();
                foreach (var msg in response.data)
                {
                    string 时间 = DateTime.Parse(msg.messageTime).ToString("HH:mm");
                    string 完整 = $"{时间} {msg.playerName} {msg.message}";
                    数据.添加消息(聊天频道.家族, 完整);
                }
            }
        }

        request.Dispose();
    }
}

// =================== 响应数据类（与服务器端对应）===================

[Serializable]
public class 发送消息响应
{
    public bool success;
    public string message;
}

[Serializable]
public class 获取消息响应
{
    public bool success;
    public string message;
    public List<消息数据> data;
}

[Serializable]
public class 消息数据
{
    public int playerId;
    public string playerName;
    public string message;
    public string messageTime;
}

