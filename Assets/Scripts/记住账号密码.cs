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
        // 初始化保存路径（放在游戏根目录）
        保存路径 = Path.Combine(Application.dataPath, "../", 保存文件名);

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

            // 创建要保存的内容
            string 保存内容 = $"账号:{账号}\n密码:{密码}\n保存时间:{DateTime.Now}";

            // 写入文件
            File.WriteAllText(保存路径, 保存内容);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"保存账号密码时出错: {e.Message}");
        }
    }

    // 从文件读取账号密码
    public void 读取账号密码()
    {
        try
        {
            // 检查文件是否存在
            if (!File.Exists(保存路径))
            {
                Debug.Log($"保存文件不存在: {保存路径}");
                return;
            }

            // 读取文件内容
            string 文件内容 = File.ReadAllText(保存路径);

            // 解析账号密码
            string 账号 = "";
            string 密码 = "";

            string[] 行数组 = 文件内容.Split('\n');
            foreach (string 行 in 行数组)
            {
                if (行.StartsWith("账号:"))
                {
                    账号 = 行.Substring("账号:".Length);
                }
                else if (行.StartsWith("密码:"))
                {
                    密码 = 行.Substring("密码:".Length);
                }
            }

            // 将账号密码填入输入框
            账号输入框.text = 账号;
            密码输入框.text = 密码;

            Debug.Log("账号密码已自动填入输入框");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"读取账号密码时出错: {e.Message}");
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