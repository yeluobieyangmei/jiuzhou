using UnityEngine;
using UnityEngine.UI;
using 玩家数据结构;
using 怪物数据结构;
using System.Collections;

public class 攻击对象查看 : MonoBehaviour
{
    public 怪物数据 当前怪物 { get; set; }
    public Text 标题;
    public Text 对象名字;
    public Text 对象详情;
    public 显示战斗日志 显示战斗日志;
    public 可攻击怪物展示界面 可攻击怪物展示界面;

    // 添加一个标志位，防止重复点击
    private bool 正在战斗中 = false;

    private void OnEnable()
    {
        正在战斗中 = false;  // 重置状态
        对象信息显示();
    }

    public void 对象信息显示()
    {
        if (当前怪物 == null) return;

        标题.text = "怪物信息";
        对象名字.text = $"LV.{当前怪物.等级} {当前怪物.名称}";
        对象详情.text = $"生命值：{当前怪物.属性.当前生命值}\n" +
            $"攻击力：{当前怪物.属性.攻击力}\n" +
            $"防御力：{当前怪物.属性.防御力}\n" +
            $"铜钱：{当前怪物.铜钱}\n经验：{当前怪物.经验值}";
    }

    public void 开始战斗()
    {
        // 防止重复点击
        if (正在战斗中) return;
        正在战斗中 = true;

        // 强制更新UI状态
        Canvas.ForceUpdateCanvases();

        try
        {
            // 联网版本：使用服务器数据
            玩家数据 玩家 = 玩家数据管理.实例?.当前玩家数据;
            if (玩家 == null)
            {
                Debug.LogError("无法获取玩家数据，请先登录");
                正在战斗中 = false;
                return;
            }

            var (是否胜利, 被击败的怪物) = 战斗系统.开始战斗(玩家, 当前怪物);

            if (是否胜利)
            {
                显示战斗日志.标题.text = "战斗胜利!";
                var 战斗展示 = 可攻击怪物展示界面;
                战斗展示?.移除已击败怪物(被击败的怪物);
            }
            else
            {
                显示战斗日志.标题.text = "战斗失败!";
            }
        }
        finally
        {
            // 确保无论如何都会执行关闭操作
            StartCoroutine(延迟关闭面板());
        }
    }

    private IEnumerator 延迟关闭面板()
    {
        // 等待一帧，确保所有UI更新完成
        yield return null;

        if (gameObject != null)
        {
            gameObject.SetActive(false);
            显示战斗日志.gameObject.SetActive(true);
        }
    }
}