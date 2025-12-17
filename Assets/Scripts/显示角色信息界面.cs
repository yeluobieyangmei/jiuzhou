using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using 玩家数据结构;

public class 显示角色信息界面 : MonoBehaviour
{
    [Header("UI 引用")]
    // 你说的三个 Text：玩家名字+等级、玩家国家
    public Text 名字文本;
    public Text 国家文本;

    [Header("场景设置")]
    public string 主场景名称 = "游戏主场景"; // 在 Inspector 里设置实际主场景名

    /// <summary>
    /// 显示角色信息（当获取到玩家数据后调用）
    /// </summary>
    public void 显示角色信息(玩家数据 玩家)
    {
        if (玩家 == null)
        {
            Debug.LogError("玩家数据为空，无法显示");
            return;
        }

        // 名字 + 等级
        if (名字文本 != null)
            名字文本.text = $"{玩家.姓名}（{玩家.等级}级）";

        // 国家
        if (国家文本 != null)
            国家文本.text = 玩家.国家 != null ? 玩家.国家.国名 : "无国家";

        Debug.Log("角色信息界面已更新（名字/等级/国家）");
    }

    /// <summary>
    /// 进入角色（挂在“进入角色”按钮上）
    /// </summary>
    public void 点击进入角色()
    {
        if (string.IsNullOrEmpty(主场景名称))
        {
            Debug.LogError("主场景名称未设置，请在 Inspector 中设置主场景名称");
            return;
        }

        Debug.Log($"加载主场景：{主场景名称}");
        SceneManager.LoadScene(主场景名称);
    }
}

