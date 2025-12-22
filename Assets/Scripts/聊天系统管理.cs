using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using System;
using System.Text;

/// <summary>
/// 聊天系统管理器
/// 负责管理聊天UI、消息显示、消息发送等功能
/// </summary>
public class 聊天系统管理 : MonoBehaviour
{
    public static 聊天系统管理 实例 { get; private set; }

    [Header("频道Toggle")]
    public Toggle 世界频道Toggle;
    public Toggle 国家频道Toggle;
    public Toggle 家族频道Toggle;
    public Toggle 系统频道Toggle;

    [Header("消息列表容器")]
    public Transform 世界消息列表容器;
    public Transform 国家消息列表容器;
    public Transform 家族消息列表容器;
    public Transform 系统消息列表容器;

    [Header("输入框和发送按钮")]
    public InputField 消息输入框;
    public Button 发送按钮;
    public Text 发送按钮文本;
    public Text 字数提示文本;

    [Header("消息项预制体")]
    public GameObject 消息项预制体;

    [Header("接口地址")]
    private string 发送世界消息地址 = "http://43.139.181.191:5000/api/sendWorldMessage";
    private string 发送国家消息地址 = "http://43.139.181.191:5000/api/sendCountryMessage";
    private string 发送家族消息地址 = "http://43.139.181.191:5000/api/sendClanMessage";
    private string 获取世界消息地址 = "http://43.139.181.191:5000/api/getWorldMessages?limit=10";
    private string 获取国家消息地址 = "http://43.139.181.191:5000/api/getCountryMessages?limit=10";
    private string 获取家族消息地址 = "http://43.139.181.191:5000/api/getClanMessages?limit=10";

    // 当前选中的频道
    private string 当前频道 = "world"; // "world", "country", "clan", "system"

    // 消息对象池
    private Queue<GameObject> 世界消息对象池 = new Queue<GameObject>();
    private Queue<GameObject> 国家消息对象池 = new Queue<GameObject>();
    private Queue<GameObject> 家族消息对象池 = new Queue<GameObject>();
    private Queue<GameObject> 系统消息对象池 = new Queue<GameObject>();

    // 当前显示的消息列表（用于限制最多70条）
    private List<GameObject> 世界消息列表 = new List<GameObject>();
    private List<GameObject> 国家消息列表 = new List<GameObject>();
    private List<GameObject> 家族消息列表 = new List<GameObject>();
    private List<GameObject> 系统消息列表 = new List<GameObject>();

    // 发送冷却相关
    private float 最后发送时间 = 0f;
    private const float 发送冷却时间 = 5f; // 5秒
    private bool 是否禁言 = false;
    private Coroutine 冷却倒计时协程;

    private void Awake()
    {
        // 单例模式
        if (实例 != null && 实例 != this)
        {
            Destroy(gameObject);
            return;
        }

        实例 = this;
    }

    private void Start()
    {
        // 初始化UI事件
        初始化UI事件();

        // 初始化对象池（预创建一些对象）
        初始化对象池();

        // 加载历史消息
        加载历史消息();
    }

    /// <summary>
    /// 初始化UI事件
    /// </summary>
    private void 初始化UI事件()
    {
        // Toggle事件
        if (世界频道Toggle != null)
        {
            世界频道Toggle.onValueChanged.AddListener((isOn) => { if (isOn) 切换频道("world"); });
        }
        if (国家频道Toggle != null)
        {
            国家频道Toggle.onValueChanged.AddListener((isOn) => { if (isOn) 切换频道("country"); });
        }
        if (家族频道Toggle != null)
        {
            家族频道Toggle.onValueChanged.AddListener((isOn) => { if (isOn) 切换频道("clan"); });
        }
        if (系统频道Toggle != null)
        {
            系统频道Toggle.onValueChanged.AddListener((isOn) => { if (isOn) 切换频道("system"); });
        }

        // 发送按钮事件
        if (发送按钮 != null)
        {
            发送按钮.onClick.AddListener(发送消息);
        }

        // 输入框事件（字数限制和提示）
        if (消息输入框 != null)
        {
            消息输入框.onValueChanged.AddListener(更新字数提示);
        }

        // 默认选中世界频道
        if (世界频道Toggle != null)
        {
            世界频道Toggle.isOn = true;
            切换频道("world");
        }
    }

    /// <summary>
    /// 初始化对象池
    /// </summary>
    private void 初始化对象池()
    {
        if (消息项预制体 == null)
        {
            Debug.LogError("消息项预制体未设置！");
            return;
        }

        // 为每个频道预创建10个对象
        for (int i = 0; i < 10; i++)
        {
            世界消息对象池.Enqueue(创建消息对象());
            国家消息对象池.Enqueue(创建消息对象());
            家族消息对象池.Enqueue(创建消息对象());
            系统消息对象池.Enqueue(创建消息对象());
        }
    }

    /// <summary>
    /// 创建消息对象
    /// </summary>
    private GameObject 创建消息对象()
    {
        GameObject obj = Instantiate(消息项预制体);
        obj.SetActive(false);
        return obj;
    }

    /// <summary>
    /// 从对象池获取消息对象
    /// </summary>
    private GameObject 获取消息对象(string 频道)
    {
        Queue<GameObject> 对象池 = null;
        switch (频道)
        {
            case "world":
                对象池 = 世界消息对象池;
                break;
            case "country":
                对象池 = 国家消息对象池;
                break;
            case "clan":
                对象池 = 家族消息对象池;
                break;
            case "system":
                对象池 = 系统消息对象池;
                break;
        }

        if (对象池 == null) return null;

        GameObject obj;
        if (对象池.Count > 0)
        {
            obj = 对象池.Dequeue();
        }
        else
        {
            obj = 创建消息对象();
        }

        obj.SetActive(true);
        return obj;
    }

    /// <summary>
    /// 回收消息对象到对象池
    /// </summary>
    private void 回收消息对象(GameObject obj, string 频道)
    {
        if (obj == null) return;

        obj.SetActive(false);
        消息项UI 消息项 = obj.GetComponent<消息项UI>();
        if (消息项 != null)
        {
            消息项.重置();
        }

        Queue<GameObject> 对象池 = null;
        switch (频道)
        {
            case "world":
                对象池 = 世界消息对象池;
                break;
            case "country":
                对象池 = 国家消息对象池;
                break;
            case "clan":
                对象池 = 家族消息对象池;
                break;
            case "system":
                对象池 = 系统消息对象池;
                break;
        }

        if (对象池 != null)
        {
            对象池.Enqueue(obj);
        }
    }

    /// <summary>
    /// 切换频道
    /// </summary>
    private void 切换频道(string 频道)
    {
        当前频道 = 频道;

        // 显示/隐藏对应的消息列表
        if (世界消息列表容器 != null)
        {
            世界消息列表容器.gameObject.SetActive(频道 == "world");
        }
        if (国家消息列表容器 != null)
        {
            国家消息列表容器.gameObject.SetActive(频道 == "country");
        }
        if (家族消息列表容器 != null)
        {
            家族消息列表容器.gameObject.SetActive(频道 == "clan");
        }
        if (系统消息列表容器 != null)
        {
            系统消息列表容器.gameObject.SetActive(频道 == "system");
        }

        // 系统频道不能发送消息
        bool 可以发送 = 频道 != "system";
        if (消息输入框 != null)
        {
            消息输入框.interactable = 可以发送;
        }
        if (发送按钮 != null)
        {
            发送按钮.interactable = 可以发送 && !是否禁言;
        }
    }

    /// <summary>
    /// 更新字数提示
    /// </summary>
    private void 更新字数提示(string 文本)
    {
        if (字数提示文本 == null) return;

        int 当前字数 = 文本.Length;
        int 最大字数 = 20;
        字数提示文本.text = $"{当前字数}/{最大字数}";

        // 如果超过20字，截断
        if (当前字数 > 最大字数 && 消息输入框 != null)
        {
            消息输入框.text = 文本.Substring(0, 最大字数);
        }
    }

    /// <summary>
    /// 发送消息
    /// </summary>
    public void 发送消息()
    {
        if (消息输入框 == null) return;

        string 消息内容 = 消息输入框.text.Trim();
        if (string.IsNullOrEmpty(消息内容))
        {
            通用提示框.显示("消息内容不能为空");
            return;
        }

        // 检查冷却时间
        float 当前时间 = Time.time;
        if (当前时间 - 最后发送时间 < 发送冷却时间)
        {
            float 剩余时间 = 发送冷却时间 - (当前时间 - 最后发送时间);
            通用提示框.显示($"发送消息过于频繁，请{Mathf.CeilToInt(剩余时间)}秒后再试");
            return;
        }

        // 检查是否禁言
        if (是否禁言)
        {
            通用提示框.显示("你已被禁言，无法发送消息");
            return;
        }

        // 根据当前频道发送消息
        switch (当前频道)
        {
            case "world":
                StartCoroutine(发送世界消息(消息内容));
                break;
            case "country":
                StartCoroutine(发送国家消息(消息内容));
                break;
            case "clan":
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
            }
            else
            {
                通用提示框.显示(response.message);
                // 如果是因为禁言，更新禁言状态
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
        发送按钮.interactable = !是否禁言 && 当前频道 != "system";
    }

    /// <summary>
    /// 设置禁言状态
    /// </summary>
    public void 设置禁言状态(bool 禁言)
    {
        是否禁言 = 禁言;
        if (发送按钮 != null)
        {
            发送按钮.interactable = !禁言 && 当前频道 != "system";
        }
        if (消息输入框 != null)
        {
            消息输入框.interactable = !禁言 && 当前频道 != "system";
        }
    }

    /// <summary>
    /// 添加消息到UI（从WebSocket接收到的实时消息）
    /// </summary>
    public void 添加消息(string 频道, string 玩家名称, string 消息内容, bool 是系统消息 = false)
    {
        string 时间 = DateTime.Now.ToString("HH:mm:ss");
        添加消息到列表(频道, 玩家名称, 消息内容, 时间, 是系统消息);
    }

    /// <summary>
    /// 添加消息到列表
    /// </summary>
    private void 添加消息到列表(string 频道, string 玩家名称, string 消息内容, string 时间, bool 是系统消息)
    {
        GameObject 消息对象 = 获取消息对象(频道);
        if (消息对象 == null) return;

        Transform 容器 = null;
        List<GameObject> 消息列表 = null;

        switch (频道)
        {
            case "world":
                容器 = 世界消息列表容器;
                消息列表 = 世界消息列表;
                break;
            case "country":
                容器 = 国家消息列表容器;
                消息列表 = 国家消息列表;
                break;
            case "clan":
                容器 = 家族消息列表容器;
                消息列表 = 家族消息列表;
                break;
            case "system":
                容器 = 系统消息列表容器;
                消息列表 = 系统消息列表;
                break;
        }

        if (容器 == null || 消息列表 == null) return;

        // 设置消息内容
        消息项UI 消息项 = 消息对象.GetComponent<消息项UI>();
        if (消息项 != null)
        {
            消息项.设置消息(玩家名称, 消息内容, 时间, 是系统消息);
        }

        // 添加到容器
        消息对象.transform.SetParent(容器, false);
        消息列表.Add(消息对象);

        // 限制最多70条消息
        const int 最大消息数 = 70;
        if (消息列表.Count > 最大消息数)
        {
            GameObject 最旧消息 = 消息列表[0];
            消息列表.RemoveAt(0);
            回收消息对象(最旧消息, 频道);
        }
    }

    /// <summary>
    /// 加载历史消息
    /// </summary>
    private void 加载历史消息()
    {
        StartCoroutine(加载世界消息());
        StartCoroutine(加载国家消息());
        StartCoroutine(加载家族消息());
    }

    /// <summary>
    /// 加载世界消息
    /// </summary>
    private IEnumerator 加载世界消息()
    {
        var request = UnityWebRequest.Get(获取世界消息地址);
        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.Success)
        {
            var response = JsonUtility.FromJson<获取消息响应>(request.downloadHandler.text);
            if (response.success && response.data != null)
            {
                // 反转列表，从旧到新显示
                response.data.Reverse();
                foreach (var msg in response.data)
                {
                    string 时间 = DateTime.Parse(msg.messageTime).ToString("HH:mm:ss");
                    添加消息到列表("world", msg.playerName, msg.message, 时间, false);
                }
            }
        }

        request.Dispose();
    }

    /// <summary>
    /// 加载国家消息
    /// </summary>
    private IEnumerator 加载国家消息()
    {
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
                    string 时间 = DateTime.Parse(msg.messageTime).ToString("HH:mm:ss");
                    添加消息到列表("country", msg.playerName, msg.message, 时间, false);
                }
            }
        }

        request.Dispose();
    }

    /// <summary>
    /// 加载家族消息
    /// </summary>
    private IEnumerator 加载家族消息()
    {
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
                    string 时间 = DateTime.Parse(msg.messageTime).ToString("HH:mm:ss");
                    添加消息到列表("clan", msg.playerName, msg.message, 时间, false);
                }
            }
        }

        request.Dispose();
    }
}

// =================== 响应数据类 ===================

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

