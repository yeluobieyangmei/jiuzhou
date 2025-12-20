using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Text;
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
    public 家族显示判断 家族显示判断;  // 用于延迟刷新显示
    
    [Header("接口地址")]
    private string 获取国家家族列表地址 = "http://43.139.181.191:5000/api/getClansByCountry";
    private string 获取所有家族列表地址 = "http://43.139.181.191:5000/api/getAllClans";
    private string 加入家族地址 = "http://43.139.181.191:5000/api/joinClan";
    private string 检查退出家族冷却地址 = "http://43.139.181.191:5000/api/checkLeaveClanCooldown";
    
    // 存储从服务器获取的家族列表
    private List<家族信息库> 服务器家族列表 = new List<家族信息库>();
    
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

        // 清理旧的克隆对象
        foreach (var obj in 克隆池)
        {
            if (obj != null) Destroy(obj);
        }
        克隆池.Clear();
        当前选中家族 = null;
        服务器家族列表.Clear();

        // 根据显示类型从服务器获取数据
        玩家数据 当前玩家 = 玩家数据管理.实例?.当前玩家数据;
        if (当前玩家 == null)
        {
            Debug.LogWarning("无法获取当前玩家数据，无法显示家族列表");
            return;
        }

        if (当前显示类型 == 显示类型.国家排名查看 || 当前显示类型 == 显示类型.申请家族)
        {
            // 获取指定国家的家族列表
            if (当前玩家.国家 == null || 当前玩家.国家.国家ID <= 0)
            {
                Debug.LogWarning("当前玩家没有国家，无法显示国家家族列表");
                return;
            }
            StartCoroutine(获取国家家族列表(当前玩家.国家.国家ID));
        }
        else if (当前显示类型 == 显示类型.世界排名查看)
        {
            // 获取所有家族列表
            StartCoroutine(获取所有家族列表());
        }
    }

    /// <summary>
    /// 获取指定国家的家族列表
    /// </summary>
    IEnumerator 获取国家家族列表(int 国家ID)
    {
        string json数据 = $"{{\"countryId\":{国家ID}}}";
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json数据);

        using (UnityWebRequest 请求 = new UnityWebRequest(获取国家家族列表地址, "POST"))
        {
            请求.uploadHandler = new UploadHandlerRaw(bodyRaw);
            请求.downloadHandler = new DownloadHandlerBuffer();
            请求.SetRequestHeader("Content-Type", "application/json; charset=utf-8");

            yield return 请求.SendWebRequest();

            if (请求.result == UnityWebRequest.Result.ConnectionError ||
                请求.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError("获取国家家族列表出错: " + 请求.error);
            }
            else
            {
                string 返回文本 = 请求.downloadHandler.text;
                Debug.Log("获取国家家族列表响应: " + 返回文本);

                获取家族列表响应 响应 = JsonUtility.FromJson<获取家族列表响应>(返回文本);
                if (响应 != null && 响应.success && 响应.data != null)
                {
                    // 转换服务器数据为本地家族信息库对象
                    foreach (var 服务器家族 in 响应.data)
                    {
                        家族信息库 家族 = 转换服务器家族数据(服务器家族);
                        服务器家族列表.Add(家族);
                    }
                    
                    // 显示家族列表
                    显示家族列表();
                }
                else
                {
                    Debug.LogError("获取国家家族列表失败: " + (响应 != null ? 响应.message : "解析错误"));
                }
            }
        }
    }

    /// <summary>
    /// 获取所有家族列表
    /// </summary>
    IEnumerator 获取所有家族列表()
    {
        using (UnityWebRequest 请求 = UnityWebRequest.Get(获取所有家族列表地址))
        {
            yield return 请求.SendWebRequest();

            if (请求.result == UnityWebRequest.Result.ConnectionError ||
                请求.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError("获取所有家族列表出错: " + 请求.error);
            }
            else
            {
                string 返回文本 = 请求.downloadHandler.text;
                Debug.Log("获取所有家族列表响应: " + 返回文本);

                获取家族列表响应 响应 = JsonUtility.FromJson<获取家族列表响应>(返回文本);
                if (响应 != null && 响应.success && 响应.data != null)
                {
                    // 转换服务器数据为本地家族信息库对象
                    foreach (var 服务器家族 in 响应.data)
                    {
                        家族信息库 家族 = 转换服务器家族数据(服务器家族);
                        服务器家族列表.Add(家族);
                    }
                    
                    // 显示家族列表
                    显示家族列表();
                }
                else
                {
                    Debug.LogError("获取所有家族列表失败: " + (响应 != null ? 响应.message : "解析错误"));
                }
            }
        }
    }

    /// <summary>
    /// 转换服务器家族数据为本地家族信息库对象
    /// </summary>
    家族信息库 转换服务器家族数据(服务器家族数据 服务器数据)
    {
        家族信息库 家族 = new 家族信息库();
        家族.家族ID = 服务器数据.id;
        家族.家族名字 = 服务器数据.name;
        家族.家族等级 = 服务器数据.level;
        家族.家族繁荣值 = 服务器数据.prosperity;
        家族.家族资金 = 服务器数据.funds;
        家族.族长ID = 服务器数据.leaderId;
        
        // 注意：家族信息库中没有族长名字和当前人数字段
        // 族长名字可以通过族长ID从玩家数据中查找
        // 当前人数可以通过家族.获取当前人数()方法获取，但需要家族成员列表已加载
        
        // 如果有国家信息，关联国家
        if (服务器数据.countryId > 0)
        {
            国家信息库 国家 = 全局方法类.获取指定ID的国家(服务器数据.countryId);
            if (国家 != null)
            {
                家族.家族国家 = 国家;
            }
            else if (!string.IsNullOrEmpty(服务器数据.countryName))
            {
                // 如果全局变量中没有，创建一个临时国家对象
                国家 = 全局方法类.获取指定名字的国家(服务器数据.countryName);
                if (国家 == null)
                {
                    国家 = new 国家信息库();
                    国家.国家ID = 服务器数据.countryId;
                    国家.国名 = 服务器数据.countryName;
                    国家.国号 = 服务器数据.countryCode;
                    全局变量.所有国家列表.Add(国家);
                }
                家族.家族国家 = 国家;
            }
        }
        
        return 家族;
    }

    /// <summary>
    /// 显示家族列表
    /// </summary>
    void 显示家族列表()
    {
        int count = 服务器家族列表.Count;

        for (int i = 0; i < count; i++)
        {
            GameObject 克隆对象 = Instantiate(要克隆的对象, 父对象);
            家族信息库 家族 = 服务器家族列表[i];

            // 显示排名和家族名字
            if (当前显示类型 == 显示类型.世界排名查看)
            {
                // 世界排名：显示排名、家族名字、国家信息
                string 国家信息 = 家族.家族国家 != null ? $"({家族.家族国家.国号})" : "";
                克隆对象.transform.GetChild(0).GetComponent<Text>().text = $"{i + 1}.";
                克隆对象.transform.GetChild(0).GetComponent<Text>().fontSize = 30;
                克隆对象.transform.GetChild(0).GetComponent<Text>().color = new Color(0.94f, 0.97f, 0.21f);
                克隆对象.transform.GetChild(1).GetComponent<Text>().text = $"{家族.家族名字} {国家信息}";
            }
            else
            {
                // 国家排名或申请家族：显示排名和家族名字
                克隆对象.transform.GetChild(0).GetComponent<Text>().text = $"{i + 1}.";
                克隆对象.transform.GetChild(0).GetComponent<Text>().fontSize = 30;
                克隆对象.transform.GetChild(0).GetComponent<Text>().color = new Color(0.94f, 0.97f, 0.21f);
                克隆对象.transform.GetChild(1).GetComponent<Text>().text = 家族.家族名字;
            }

            克隆对象.gameObject.SetActive(true);
            克隆池.Add(克隆对象);

            // 处理 Toggle 选择逻辑
            Toggle t = 克隆对象.GetComponent<Toggle>();
            家族信息库 捕获家族 = 家族; // 闭包捕获
            t.onValueChanged.AddListener(isOn =>
            {
                if (isOn)
                {
                    当前选中家族 = 捕获家族;
                    Debug.Log($"当前选中家族：{当前选中家族.家族名字}");
                }
            });
        }
    }

    /// <summary>
    /// 申请加入家族（首先检查退出家族冷却时间）
    /// </summary>
    public void 申请加入()
    {
        if (当前选中家族 == null)
        {
            通用提示框.显示("请先选择一个家族");
            return;
        }

        玩家数据 当前玩家 = 玩家数据管理.实例?.当前玩家数据;
        if (当前玩家 == null)
        {
            通用提示框.显示("无法获取当前玩家数据，无法加入家族");
            return;
        }

        // 检查玩家是否已经有家族
        if (当前玩家.家族 != null && 当前玩家.家族.家族ID > 0)
        {
            通用提示框.显示("加入家族失败：你已经属于某个家族");
            return;
        }

        int accountId = PlayerPrefs.GetInt("AccountId", -1);
        if (accountId <= 0)
        {
            通用提示框.显示("加入家族失败：未找到账号ID，请先登录");
            return;
        }

        // 先检查退出家族冷却时间
        StartCoroutine(检查退出家族冷却时间(accountId));
    }

    /// <summary>
    /// 检查退出家族冷却时间
    /// </summary>
    IEnumerator 检查退出家族冷却时间(int accountId)
    {
        string json数据 = $"{{\"accountId\":{accountId}}}";
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json数据);

        using (UnityWebRequest 请求 = new UnityWebRequest(检查退出家族冷却地址, "POST"))
        {
            请求.uploadHandler = new UploadHandlerRaw(bodyRaw);
            请求.downloadHandler = new DownloadHandlerBuffer();
            请求.SetRequestHeader("Content-Type", "application/json; charset=utf-8");

            yield return 请求.SendWebRequest();

            if (请求.result == UnityWebRequest.Result.ConnectionError ||
                请求.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError("检查退出家族冷却时间出错: " + 请求.error);
                // 如果检查失败，仍然允许尝试加入（可能是网络问题）
                发送加入家族请求(accountId, 当前选中家族.家族ID);
            }
            else
            {
                string 返回文本 = 请求.downloadHandler.text;
                Debug.Log("检查退出家族冷却时间响应: " + 返回文本);

                检查退出家族冷却响应 响应 = JsonUtility.FromJson<检查退出家族冷却响应>(返回文本);
                if (响应 != null && 响应.success)
                {
                    if (响应.inCooldown)
                    {
                        // 还在冷却中，显示提示
                        通用提示框.显示($"{响应.remainingMinutes}分后才可加入新家族！");
                    }
                    else
                    {
                        // 冷却时间已过，可以加入
                        发送加入家族请求(accountId, 当前选中家族.家族ID);
                    }
                }
                else
                {
                    // 如果检查失败，仍然允许尝试加入（可能是服务器问题）
                    Debug.LogWarning("检查退出家族冷却时间失败，继续尝试加入家族");
                    发送加入家族请求(accountId, 当前选中家族.家族ID);
                }
            }
        }
    }

    /// <summary>
    /// 发送加入家族请求到服务器
    /// </summary>
    void 发送加入家族请求(int accountId, int 家族ID)
    {
        StartCoroutine(发送加入家族请求协程(accountId, 家族ID));
    }

    /// <summary>
    /// 发送加入家族请求协程
    /// </summary>
    IEnumerator 发送加入家族请求协程(int accountId, int 家族ID)
    {
        string json数据 = $"{{\"accountId\":{accountId},\"clanId\":{家族ID}}}";
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json数据);

        using (UnityWebRequest 请求 = new UnityWebRequest(加入家族地址, "POST"))
        {
            请求.uploadHandler = new UploadHandlerRaw(bodyRaw);
            请求.downloadHandler = new DownloadHandlerBuffer();
            请求.SetRequestHeader("Content-Type", "application/json; charset=utf-8");

            yield return 请求.SendWebRequest();

            if (请求.result == UnityWebRequest.Result.ConnectionError ||
                请求.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError("加入家族出错: " + 请求.error);
                通用提示框.显示("加入家族失败：网络错误");
            }
            else
            {
                string 返回文本 = 请求.downloadHandler.text;
                Debug.Log("加入家族响应: " + 返回文本);

                加入家族响应 响应 = JsonUtility.FromJson<加入家族响应>(返回文本);
                if (响应 != null)
                {
                    if (响应.success)
                    {
                        通用提示框.显示(响应.message);

                        // 刷新玩家数据（会更新家族信息）
                        if (玩家数据管理.实例 != null)
                        {
                            玩家数据管理.实例.获取玩家数据(accountId);
                        }

                        // 关闭家族列表显示UI
                        this.gameObject.SetActive(false);

                        // 3秒后执行家族显示判断的刷新显示
                        if (家族显示判断 != null)
                        {
                            玩家数据管理.实例.StartCoroutine(延迟刷新家族显示判断(家族显示判断, 3f));
                        }
                    }
                    else
                    {
                        通用提示框.显示(响应.message);
                    }
                }
                else
                {
                    通用提示框.显示("加入家族失败：解析响应失败");
                }
            }
        }
    }

    /// <summary>
    /// 延迟刷新家族显示判断（等待玩家数据更新完成）
    /// </summary>
    static IEnumerator 延迟刷新家族显示判断(家族显示判断 家族显示判断组件, float 延迟秒数)
    {
        Debug.Log($"准备等待{延迟秒数}秒后刷新家族显示判断");
        
        // 显示加载动画，让用户知道正在处理
        if (玩家数据管理.实例 != null && 玩家数据管理.实例.加载动画组件 != null)
        {
            玩家数据管理.实例.加载动画组件.开始加载动画(延迟秒数, "正在更新家族信息...");
        }
        
        // 等待指定时间
        yield return new WaitForSeconds(延迟秒数);
        
        Debug.Log($"{延迟秒数}秒等待完成，准备刷新家族显示判断");
        
        // 确保组件和GameObject都存在且激活
        if (家族显示判断组件 != null && 家族显示判断组件.gameObject != null)
        {
            // 如果GameObject未激活，尝试激活它
            if (!家族显示判断组件.gameObject.activeInHierarchy)
            {
                Debug.LogWarning("家族显示判断GameObject未激活，尝试激活");
                家族显示判断组件.gameObject.SetActive(true);
            }
            
            Debug.Log("开始刷新家族显示判断");
            家族显示判断组件.刷新显示();
        }
        else
        {
            Debug.LogError("家族显示判断组件为null或GameObject为null，无法刷新！");
        }
    }
}

// =================== 响应数据类 ===================

[System.Serializable]
public class 获取家族列表响应
{
    public bool success;
    public string message;
    public List<服务器家族数据> data;
}

[System.Serializable]
public class 服务器家族数据
{
    public int id;
    public string name;
    public int level;
    public int prosperity;
    public int funds;
    public int leaderId;
    public string leaderName;
    public int memberCount;
    public int countryId;  // -1 表示没有国家
    public string countryName;
    public string countryCode;
}

[System.Serializable]
public class 加入家族响应
{
    public bool success;
    public string message;
}

[System.Serializable]
public class 检查退出家族冷却响应
{
    public bool success;
    public string message;
    public bool inCooldown;  // 是否在冷却中
    public int remainingMinutes;  // 剩余分钟数
}
