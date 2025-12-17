using UnityEngine;
using UnityEngine.UI;
using 玩家数据结构;

public class 显示角色信息界面 : MonoBehaviour
{
    [Header("UI 引用")]
    public GameObject 角色信息面板;  // 整个面板（用于显示/隐藏）
    public Text 姓名文本;
    public Text 性别文本;
    public Text 等级文本;
    public Text 称号文本;
    public Text 官职文本;
    public Text 铜钱文本;
    public Text 黄金文本;
    public Text 生命值文本;
    public Text 当前生命值文本;
    public Text 攻击力文本;
    public Text 防御力文本;
    public Text 暴击率文本;
    public Text 国家文本;
    public Text 家族文本;

    /// <summary>
    /// 显示角色信息面板（当获取到玩家数据后调用）
    /// </summary>
    public void 显示角色信息(玩家数据 玩家)
    {
        if (角色信息面板 != null)
            角色信息面板.SetActive(true);

        if (玩家 == null)
        {
            Debug.LogError("玩家数据为空，无法显示");
            return;
        }

        // 更新UI显示
        if (姓名文本 != null)
            姓名文本.text = $"姓名：{玩家.姓名}";
        if (性别文本 != null)
            性别文本.text = $"性别：{玩家.性别}";
        if (等级文本 != null)
            等级文本.text = $"等级：{玩家.等级}";
        if (称号文本 != null)
            称号文本.text = $"称号：{玩家.称号名}";
        if (官职文本 != null)
            官职文本.text = $"官职：{玩家.官职}";
        if (铜钱文本 != null)
            铜钱文本.text = $"铜钱：{玩家.铜钱:N0}";  // N0 表示千分位分隔符
        if (黄金文本 != null)
            黄金文本.text = $"黄金：{玩家.黄金:N0}";

        // 属性信息
        if (生命值文本 != null)
            生命值文本.text = $"生命值：{玩家.玩家属性.生命值}";
        if (当前生命值文本 != null)
            当前生命值文本.text = $"当前生命值：{玩家.玩家属性.当前生命值}";
        if (攻击力文本 != null)
            攻击力文本.text = $"攻击力：{玩家.玩家属性.攻击力:N0}";
        if (防御力文本 != null)
            防御力文本.text = $"防御力：{玩家.玩家属性.防御力}";
        if (暴击率文本 != null)
            暴击率文本.text = $"暴击率：{玩家.玩家属性.暴击率:P2}";  // P2 表示百分比，保留2位小数

        // 国家和家族信息
        if (国家文本 != null)
            国家文本.text = $"国家：{(玩家.国家 != null ? 玩家.国家.国名 : "无")}";
        if (家族文本 != null)
            家族文本.text = $"家族：{(玩家.家族 != null ? 玩家.家族.家族名字 : "无")}";

        Debug.Log("角色信息界面已更新");
    }

    /// <summary>
    /// 隐藏角色信息面板
    /// </summary>
    public void 隐藏角色信息面板()
    {
        if (角色信息面板 != null)
            角色信息面板.SetActive(false);
    }
}

