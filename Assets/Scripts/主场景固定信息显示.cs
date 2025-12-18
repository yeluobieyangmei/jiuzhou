using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using 玩家数据结构;

public class 主场景固定信息显示 : MonoBehaviour
{
    [Header("刷新设置")]
    private float 刷新间隔秒 = 1f;   // 每多少秒刷新一次
    private float 刷新计时器 = 0f;

    public Image 性别图片;
    public Text 角色信息;
    public Text 家族信息;
    public Text 国家信息;
    public Text 铜钱信息;
    public Text 黄金信息;
    public Text 版本号;

    public void Update()
    {
        刷新计时器 += Time.deltaTime;
        if (刷新计时器 >= 刷新间隔秒)
        {
            刷新计时器 = 0f;
            刷新显示();
        }
    }
    public void OnEnable()
    {
        刷新显示();
    }
    public void 刷新显示()
    {
        玩家数据 玩家 = null;

        if (玩家数据管理.实例 != null && 玩家数据管理.实例.当前玩家数据 != null)
        {
            玩家 = 玩家数据管理.实例.当前玩家数据;
        }

        if (玩家 == null)
        {
            // 当前还没有任何玩家数据（可能是刚进主场景还没加载完），先不刷新
            return;
        }

        // 等级 + 名字（经验百分比目前用 0.00% 占位）
        if (角色信息 != null)
            角色信息.text = $"Lv.{玩家.等级} {玩家.姓名}(0.00%)";

        // 家族信息
        if (家族信息 != null)
        {
            string 家族名 = 玩家.家族 != null ? 玩家.家族.家族名字 : "暂无";
            家族信息.text = $"家族:{家族名}";
        }

        // 国家信息
        if (国家信息 != null)
        {
            string 国名 = 玩家.国家 != null ? 玩家.国家.国名 : "暂无";
            国家信息.text = $"国家:{国名}";
        }

        // 财产
        if (铜钱信息 != null)
            铜钱信息.text = $"铜钱:{玩家.铜钱}";

        if (黄金信息 != null)
            黄金信息.text = $"黄金:{玩家.黄金}";

        // 版本号（先写死，后面可以从配置读取）
        if (版本号 != null)
            版本号.text = "1.0.0";
    }
}
