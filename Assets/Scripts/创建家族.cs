using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Text;
using 玩家数据结构;
using 国家系统;

public class 创建家族 : MonoBehaviour
{
    [Header("UI 引用")]
    public InputField 家族名输入框;
    public InputField 家族公告输入框;
    public Text 提示文本;  // 用于显示提示信息
    public 家族显示判断 家族显示判断;

    [Header("接口地址")]
    private string 创建家族地址 = "http://43.139.181.191:5000/api/createClan";

    public void 创建一个家族()
    {
        // 获取输入的家族名字
        string 家族名字 = 家族名输入框 != null ? 家族名输入框.text.Trim() : "";

        // 验证输入
        if (string.IsNullOrEmpty(家族名字))
        {
            显示提示("请正确输入家族名称！");
            return;
        }

        // 验证家族名字长度（客户端限制4个字符，服务端限制50个字符）
        if (家族名字.Length > 4)
        {
            显示提示("家族名称不可超过4个字！");
            return;
        }

        // 检查玩家是否已登录
        int accountId = PlayerPrefs.GetInt("AccountId", -1);
        if (accountId <= 0)
        {
            显示提示("创建家族失败：未找到账号ID，请先登录");
            return;
        }

        // 检查玩家是否有国家归属（客户端预检查）
        玩家数据 当前玩家 = 玩家数据管理.实例?.当前玩家数据;
        if (当前玩家 == null)
        {
            显示提示("创建家族失败：无法获取玩家数据");
            return;
        }

        if (当前玩家.国家 == null)
        {
            显示提示("创建家族失败：玩家必须属于某个国家");
            return;
        }

        // 显示创建中提示
        显示提示("正在创建家族...");

        // 发送创建家族请求
        StartCoroutine(发送创建家族请求(accountId, 家族名字));
    }

    /// <summary>
    /// 发送创建家族请求到服务器
    /// </summary>
    IEnumerator 发送创建家族请求(int accountId, string 家族名字)
    {
        string json数据 = $"{{\"accountId\":{accountId},\"clanName\":\"{家族名字}\"}}";
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json数据);

        using (UnityWebRequest 请求 = new UnityWebRequest(创建家族地址, "POST"))
        {
            请求.uploadHandler = new UploadHandlerRaw(bodyRaw);
            请求.downloadHandler = new DownloadHandlerBuffer();
            请求.SetRequestHeader("Content-Type", "application/json; charset=utf-8");

            yield return 请求.SendWebRequest();

            if (请求.result == UnityWebRequest.Result.ConnectionError ||
                请求.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError("创建家族出错: " + 请求.error);
                显示提示("创建家族失败：网络错误 - " + 请求.error);
            }
            else
            {
                string 返回文本 = 请求.downloadHandler.text;
                Debug.Log("创建家族响应: " + 返回文本);

                创建家族响应 响应 = JsonUtility.FromJson<创建家族响应>(返回文本);
                if (响应 != null)
                {
                    if (响应.success)
                    {
                        Debug.Log($"家族创建成功！家族ID：{响应.clanId}");
                        显示提示("家族创建成功！");

                        // 刷新玩家数据（会更新家族信息）
                        if (玩家数据管理.实例 != null)
                        {
                            玩家数据管理.实例.获取玩家数据(accountId);
                        }

                        // 将协程放在玩家数据管理上执行（因为它是DontDestroyOnLoad的单例，不会被禁用）
                        // 这样即使创建家族界面被关闭，协程也能继续运行
                        if (玩家数据管理.实例 != null)
                        {
                            玩家数据管理.实例.StartCoroutine(等待后刷新家族显示协程(家族显示判断, 3f));
                        }
                        else
                        {
                            Debug.LogWarning("玩家数据管理实例不存在，无法启动刷新协程");
                            // 如果玩家数据管理不存在，直接刷新（不使用加载动画）
                            if (家族显示判断 != null)
                            {
                                家族显示判断.刷新显示();
                            }
                        }

                        // 创建成功后，关闭创建家族界面
                        this.gameObject.SetActive(false);
                    }
                    else
                    {
                        Debug.LogError("创建家族失败: " + 响应.message);
                        显示提示("创建失败：" + 响应.message);
                    }
                }
                else
                {
                    Debug.LogError("创建家族失败：解析响应失败");
                    显示提示("创建家族失败：服务器响应错误");
                }
            }
        }
    }

    /// <summary>
    /// 显示提示信息
    /// </summary>
    void 显示提示(string 提示内容)
    {
        if (提示文本 != null)
        {
            提示文本.text = 提示内容;
        }
        Debug.Log(提示内容);
    }

    /// <summary>
    /// 等待指定时间后刷新家族显示判断（显示加载动画）
    /// 这个方法可以在玩家数据管理上执行，因为它是静态的
    /// </summary>
    public static IEnumerator 等待后刷新家族显示协程(家族显示判断 家族显示判断组件, float 等待时间)
    {
        // 从Resources加载加载动画预制体
        GameObject 加载动画预制体 = Resources.Load<GameObject>("通用界面/加载动画");
        加载动画 加载动画组件 = null;
        GameObject 加载动画对象 = null;

        if (加载动画预制体 != null)
        {
            加载动画对象 = Instantiate(加载动画预制体);
            加载动画组件 = 加载动画对象.GetComponent<加载动画>();
            
            if (加载动画组件 != null)
            {
                // 显示加载动画
                加载动画组件.开始加载动画(等待时间, "正在初始化家族信息...");
            }
            else
            {
                Debug.LogWarning("加载动画预制体上未找到加载动画组件");
            }
        }
        else
        {
            Debug.LogWarning("无法从Resources/通用界面/加载动画加载预制体");
        }

        // 等待指定时间
        yield return new WaitForSeconds(等待时间);

        // 刷新家族显示判断
        if (家族显示判断组件 != null)
        {
            家族显示判断组件.刷新显示();
        }

        // 清理临时加载动画对象（加载动画会在内部自动关闭，这里确保对象被销毁）
        if (加载动画对象 != null)
        {
            // 等待一小段时间确保动画完成
            yield return new WaitForSeconds(0.2f);
            Destroy(加载动画对象);
        }
    }
}

// =================== 服务端返回的数据结构 ===================

[System.Serializable]
public class 创建家族响应
{
    public bool success;
    public string message;
    public int clanId;
}
