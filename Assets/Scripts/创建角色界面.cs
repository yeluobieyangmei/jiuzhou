using UnityEngine;
using UnityEngine.UI;

public class 创建角色界面 : MonoBehaviour
{
    [Header("UI 引用")]
    public InputField 姓名输入框;
    public Text 提示文本;

    private string 当前选择的性别 = "男";
    private int 当前账号ID;

    /// <summary>
    /// 由 玩家数据管理 在需要创建角色时调用，设置当前账号ID
    /// </summary>
    public void 设置账号ID(int accountId)
    {
        当前账号ID = accountId;
    }

    public void 选择男性()
    {
        当前选择的性别 = "男";
        Debug.Log($"选择了性别：男");
    }
    public void 选择女性()
    {
        当前选择的性别 = "女";
        Debug.Log($"选择了性别：女");
    }

    public void 点击创建角色()
    {
        string 姓名 = 姓名输入框 != null ? 姓名输入框.text.Trim() : "";

        if (string.IsNullOrEmpty(姓名))
        {
            if (提示文本 != null)
                提示文本.text = "请输入玩家姓名";
            return;
        }

        if (string.IsNullOrEmpty(当前选择的性别))
        {
            if (提示文本 != null)
                提示文本.text = "请选择性别";
            return;
        }

        if (提示文本 != null)
            提示文本.text = "正在创建角色...";

        // 调用玩家数据管理的创建角色方法
        玩家数据管理 玩家管理 = FindObjectOfType<玩家数据管理>();
        if (玩家管理 != null)
        {
            玩家管理.创建角色(当前账号ID, 姓名, 当前选择的性别);
        }
        else
        {
            Debug.LogError("找不到玩家数据管理组件！");
            if (提示文本 != null)
                提示文本.text = "系统错误：找不到玩家数据管理";
        }
    }
}

