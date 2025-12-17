using UnityEngine;
using UnityEngine.UI;

public class 创建角色界面 : MonoBehaviour
{
    [Header("UI 引用")]
    public GameObject 创建角色面板;  // 整个面板（用于显示/隐藏）
    public InputField 姓名输入框;
    public Button 男性按钮;
    public Button 女性按钮;
    public Button 创建按钮;
    public Text 提示文本;

    private string 当前选择的性别 = "男";
    private int 当前账号ID;

    void Start()
    {
        // 默认隐藏面板
        if (创建角色面板 != null)
            创建角色面板.SetActive(false);

        // 绑定按钮事件
        if (男性按钮 != null)
            男性按钮.onClick.AddListener(() => 选择性别("男"));
        if (女性按钮 != null)
            女性按钮.onClick.AddListener(() => 选择性别("女"));
        if (创建按钮 != null)
            创建按钮.onClick.AddListener(点击创建角色);
    }

    /// <summary>
    /// 显示创建角色面板（当检测到账号没有角色时调用）
    /// </summary>
    public void 显示创建角色面板(int accountId)
    {
        当前账号ID = accountId;
        if (创建角色面板 != null)
            创建角色面板.SetActive(true);
        if (提示文本 != null)
            提示文本.text = "请创建您的角色";
        
        // 重置输入框
        if (姓名输入框 != null)
            姓名输入框.text = "";
        
        // 默认选择男性
        当前选择的性别 = "男";
    }

    void 选择性别(string 性别)
    {
        当前选择的性别 = 性别;
        Debug.Log($"选择了性别：{性别}");
        
        // 可以在这里更新按钮的视觉反馈（比如高亮选中的按钮）
    }

    void 点击创建角色()
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

    /// <summary>
    /// 隐藏创建角色面板（创建成功后调用）
    /// </summary>
    public void 隐藏创建角色面板()
    {
        if (创建角色面板 != null)
            创建角色面板.SetActive(false);
    }
}

