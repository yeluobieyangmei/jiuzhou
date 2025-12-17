using UnityEngine;
using UnityEngine.Networking;
using System.Collections;
using System.Text;

public class 服务器测试 : MonoBehaviour
{
    // 登录接口地址（用你的虚拟机 IP）
    private string 服务器登录地址 = "http://43.139.181.191:5000/api/login";

    // 测试账号密码（后面可以改成从输入框获取）
    private string 测试账号 = "test";
    private string 测试密码 = "123456";

    void Start()
    {
        // 如需在进入场景时自动测试登录，可以取消下一行的注释
        // StartCoroutine(发送登录请求(测试账号, 测试密码));
    }

    IEnumerator 发送登录请求(string 账号, string 密码)
    {
        // 和你在 PowerShell 里的一样，使用 username / password 字段名
        string json数据 = $"{{\"username\":\"{账号}\",\"password\":\"{密码}\"}}";
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json数据);

        using (UnityWebRequest 请求 = new UnityWebRequest(服务器登录地址, "POST"))
        {
            请求.uploadHandler = new UploadHandlerRaw(bodyRaw);
            请求.downloadHandler = new DownloadHandlerBuffer();
            请求.SetRequestHeader("Content-Type", "application/json");

            // 发送请求
            yield return 请求.SendWebRequest();

            // 检查结果
            if (请求.result == UnityWebRequest.Result.ConnectionError ||
                请求.result == UnityWebRequest.Result.ProtocolError)
            {
                Debug.LogError("登录请求出错: " + 请求.error);
            }
            else
            {
                string 返回文本 = 请求.downloadHandler.text;
                Debug.Log("登录响应原始文本: " + 返回文本);

                // 解析为 登录结果 对象
                登录结果 结果 = JsonUtility.FromJson<登录结果>(返回文本);
                if (结果 != null)
                {
                    Debug.Log($"解析后 -> 是否成功: {结果.success}, 提示: {结果.message}, 令牌: {结果.token}");
                }
                else
                {
                    Debug.LogError("解析登录结果失败");
                }
            }
        }
    }
}