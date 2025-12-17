using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Collections;
using System.Text;

public class 登录界面 : MonoBehaviour
{
    [Header("UI 引用")]
    public InputField 账号输入框;
    public InputField 密码输入框;
    public Text 提示文本;

    [Header("接口地址")]
    private string 登录地址 = "http://43.139.181.191:5000/api/login";
    private string 注册地址 = "http://43.139.181.191:5000/api/register";

    public void 点击登录()
    {
        string 账号 = 账号输入框 != null ? 账号输入框.text.Trim() : "";
        string 密码 = 密码输入框 != null ? 密码输入框.text : "";

        if (string.IsNullOrEmpty(账号) || string.IsNullOrEmpty(密码))
        {
            if (提示文本 != null)
                提示文本.text = "账号和密码不能为空";
            return;
        }

        if (提示文本 != null)
            提示文本.text = "正在登录...";

        StartCoroutine(发送请求(登录地址, 账号, 密码, true));
    }

    public void 点击注册()
    {
        string 账号 = 账号输入框 != null ? 账号输入框.text.Trim() : "";
        string 密码 = 密码输入框 != null ? 密码输入框.text : "";

        if (string.IsNullOrEmpty(账号) || string.IsNullOrEmpty(密码))
        {
            if (提示文本 != null)
                提示文本.text = "账号和密码不能为空";
            return;
        }

        if (提示文本 != null)
            提示文本.text = "正在注册...";

        StartCoroutine(发送请求(注册地址, 账号, 密码, false));
    }

    IEnumerator 发送请求(string url, string 账号, string 密码, bool 是登录)
    {
        string json数据 = $"{{\"username\":\"{账号}\",\"password\":\"{密码}\"}}";
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json数据);

        using (UnityWebRequest 请求 = new UnityWebRequest(url, "POST"))
        {
            请求.uploadHandler = new UploadHandlerRaw(bodyRaw);
            请求.downloadHandler = new DownloadHandlerBuffer();
            请求.SetRequestHeader("Content-Type", "application/json");

            yield return 请求.SendWebRequest();

            if (请求.result == UnityWebRequest.Result.ConnectionError ||
                请求.result == UnityWebRequest.Result.ProtocolError)
            {
                if (提示文本 != null)
                    提示文本.text = "网络错误: " + 请求.error;
            }
            else
            {
                string 返回文本 = 请求.downloadHandler.text;

                if (是登录)
                {
                    登录结果 结果 = JsonUtility.FromJson<登录结果>(返回文本);
                    if (结果 != null)
                    {
                        if (结果.success)
                        {
                            if (提示文本 != null)
                                提示文本.text = "登录成功，正在加载玩家数据...";
                            Debug.Log($"登录成功，账号ID：{结果.accountId}，令牌：{结果.token}");
                            
                            // 保存账号ID（用于后续接口调用）
                            PlayerPrefs.SetInt("AccountId", 结果.accountId);
                            PlayerPrefs.SetString("Token", 结果.token);
                            PlayerPrefs.Save();
                            
                            // 调用玩家数据管理，获取或创建角色
                            玩家数据管理 玩家管理 = FindObjectOfType<玩家数据管理>();
                            if (玩家管理 == null)
                            {
                                GameObject 管理对象 = new GameObject("玩家数据管理");
                                玩家管理 = 管理对象.AddComponent<玩家数据管理>();
                            }
                            玩家管理.获取玩家数据(结果.accountId);
                        }
                        else
                        {
                            if (提示文本 != null)
                                提示文本.text = "登录失败：" + 结果.message;
                        }
                    }
                    else
                    {
                        if (提示文本 != null)
                            提示文本.text = "登录响应解析失败";
                    }
                }
                else
                {
                    注册结果 结果 = JsonUtility.FromJson<注册结果>(返回文本);
                    if (结果 != null)
                    {
                        if (结果.success)
                        {
                            if (提示文本 != null)
                                提示文本.text = "注册成功，请使用该账号登录";
                        }
                        else
                        {
                            if (提示文本 != null)
                                提示文本.text = "注册失败：" + 结果.message;
                        }
                    }
                    else
                    {
                        if (提示文本 != null)
                            提示文本.text = "注册响应解析失败";
                    }
                }
            }
        }
    }
}

[System.Serializable]
public class 注册结果
{
    public bool success;
    public string message;
}


