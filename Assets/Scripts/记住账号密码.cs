using UnityEngine;
using UnityEngine.UI;
using System.IO;
using System;

public class 记住账号密码 : MonoBehaviour
{
    public InputField 账号输入框;
    public InputField 密码输入框;
    public Button 登录按钮;
    private string 保存文件名 = "账号密码记录.txt";

    // 保存路径
    private string 保存路径;

    void Start()
    {
        // 初始化保存路径（使用持久化数据路径，跨平台兼容）
        // PC: %USERPROFILE%\AppData\LocalLow\<CompanyName>\<ProductName>
        // Android: /data/data/<packageName>/files/
        // iOS: /var/mobile/Containers/Data/Application/<guid>/Documents/
        保存路径 = Path.Combine(Application.persistentDataPath, 保存文件名);

        // 绑定登录按钮点击事件
        登录按钮.onClick.AddListener(保存账号密码);

        // 游戏开始时自动读取保存的账号密码
        OnEnable();
    }

    void OnEnable()
    {
        // 延迟一帧执行，确保UI组件已初始化
        StartCoroutine(延迟读取账号密码());
    }

    System.Collections.IEnumerator 延迟读取账号密码()
    {
        yield return null; // 等待一帧
        读取账号密码();
    }

    // 保存账号密码到文件
    public void 保存账号密码()
    {
        try
        {
            // 获取输入的账号密码
            string 账号 = 账号输入框.text;
            string 密码 = 密码输入框.text;

            // 检查输入是否为空
            if (string.IsNullOrEmpty(账号) || string.IsNullOrEmpty(密码))
            {
                Debug.LogWarning("[记住账号密码] 账号或密码为空，跳过保存");
                return;
            }

            // 创建要保存的内容（只保存账号和密码）
            string 保存内容 = $"账号:{账号}\n密码:{密码}";

            // 确保目录存在
            string 目录路径 = Path.GetDirectoryName(保存路径);
            if (!Directory.Exists(目录路径))
            {
                Directory.CreateDirectory(目录路径);
                Debug.Log($"[记住账号密码] 创建目录: {目录路径}");
            }

            // 写入文件
            File.WriteAllText(保存路径, 保存内容, System.Text.Encoding.UTF8);
            Debug.Log($"[记住账号密码] 保存成功: {保存路径}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[记住账号密码] 保存账号密码时出错: {e.Message}\n堆栈: {e.StackTrace}");
        }
    }

    // 从文件读取账号密码
    public void 读取账号密码()
    {
        try
        {
            // 检查输入框是否已初始化
            if (账号输入框 == null || 密码输入框 == null)
            {
                Debug.LogWarning("[记住账号密码] 输入框未初始化，跳过读取");
                return;
            }

            // 检查文件是否存在
            if (!File.Exists(保存路径))
            {
                Debug.Log($"[记住账号密码] 保存文件不存在: {保存路径}");
                return;
            }

            // 读取文件内容
            string 文件内容 = File.ReadAllText(保存路径, System.Text.Encoding.UTF8);

            // 解析账号密码
            string 账号 = "";
            string 密码 = "";

            string[] 行数组 = 文件内容.Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string 行 in 行数组)
            {
                string 清理后的行 = 行.Trim();
                if (清理后的行.StartsWith("账号:"))
                {
                    账号 = 清理后的行.Substring("账号:".Length).Trim();
                }
                else if (清理后的行.StartsWith("密码:"))
                {
                    密码 = 清理后的行.Substring("密码:".Length).Trim();
                }
            }

            // 将账号密码填入输入框
            if (!string.IsNullOrEmpty(账号))
            {
                账号输入框.text = 账号;
            }
            if (!string.IsNullOrEmpty(密码))
            {
                密码输入框.text = 密码;
            }
        }
        catch (System.Exception e)
        {

        }
    }

    // 手动清除保存的账号密码
    public void 清除保存的账号密码()
    {
        try
        {
            if (File.Exists(保存路径))
            {
                File.Delete(保存路径);
                Debug.Log("已清除保存的账号密码");

                // 清空输入框
                账号输入框.text = "";
                密码输入框.text = "";
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"清除账号密码时出错: {e.Message}");
        }
    }

    // 检查是否有保存的账号密码
    public bool 是否有保存的账号密码()
    {
        return File.Exists(保存路径);
    }
}