using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 消息项UI组件
/// 用于显示单条聊天消息
/// </summary>
public class 消息项UI : MonoBehaviour
{
    [Header("UI组件")]
    public Text 玩家名称文本;
    public Text 消息内容文本;
    public Text 时间文本;

    /// <summary>
    /// 设置消息内容
    /// </summary>
    public void 设置消息(string 玩家名称, string 消息内容, string 时间, bool 是系统消息 = false)
    {
        if (玩家名称文本 != null)
        {
            玩家名称文本.text = 玩家名称;
        }

        if (消息内容文本 != null)
        {
            消息内容文本.text = 消息内容;
            // 系统消息使用红色字体
            if (是系统消息)
            {
                消息内容文本.color = Color.red;
            }
            else
            {
                消息内容文本.color = Color.white;
            }
        }

        if (时间文本 != null)
        {
            时间文本.text = 时间;
        }
    }

    /// <summary>
    /// 重置消息项（用于对象池回收）
    /// </summary>
    public void 重置()
    {
        if (玩家名称文本 != null)
        {
            玩家名称文本.text = "";
        }
        if (消息内容文本 != null)
        {
            消息内容文本.text = "";
            消息内容文本.color = Color.white;
        }
        if (时间文本 != null)
        {
            时间文本.text = "";
        }
    }
}

