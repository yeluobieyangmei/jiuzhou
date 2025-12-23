using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 聊天频道枚举
/// </summary>
public enum 聊天频道
{
    全部 = 0,
    世界 = 1,
    国家 = 2,
    家族 = 3,
    系统 = 4,
}

/// <summary>
/// 本地单条聊天消息数据（只用于客户端显示）
/// </summary>
[Serializable]
public class 本地聊天消息
{
    /// <summary>
    /// 消息时间戳（自1970-01-01起的秒数）
    /// </summary>
    public long 时间戳;

    /// <summary>
    /// 消息所属频道
    /// </summary>
    public 聊天频道 频道;

    /// <summary>
    /// 最终展示在 Text 上的一整行字符串（例如：04:06 玩家名 消息内容）
    /// </summary>
    public string 文本;

    public 本地聊天消息(聊天频道 channel, string text)
    {
        时间戳 = DateTimeOffset.Now.ToUnixTimeSeconds();
        频道 = channel;
        文本 = text;
    }
}

/// <summary>
/// 本地聊天消息数据管理（仅客户端缓存，用于界面显示）
/// </summary>
public class 本地聊天消息数据
{
    private readonly List<本地聊天消息> 世界消息 = new List<本地聊天消息>();
    private readonly List<本地聊天消息> 国家消息 = new List<本地聊天消息>();
    private readonly List<本地聊天消息> 家族消息 = new List<本地聊天消息>();
    private readonly List<本地聊天消息> 系统消息 = new List<本地聊天消息>();

    /// <summary>
    /// 每个频道在客户端最多保留的消息数量
    /// </summary>
    private const int 每频道最大条目数 = 100;

    /// <summary>
    /// 添加一条消息到本地缓存
    /// </summary>
    public void 添加消息(聊天频道 频道, string 文本)
    {
        var msg = new 本地聊天消息(频道, 文本);

        switch (频道)
        {
            case 聊天频道.世界:
                世界消息.Add(msg);
                if (世界消息.Count > 每频道最大条目数)
                    世界消息.RemoveAt(0);
                break;

            case 聊天频道.国家:
                国家消息.Add(msg);
                if (国家消息.Count > 每频道最大条目数)
                    国家消息.RemoveAt(0);
                break;

            case 聊天频道.家族:
                家族消息.Add(msg);
                if (家族消息.Count > 每频道最大条目数)
                    家族消息.RemoveAt(0);
                break;

            case 聊天频道.系统:
                系统消息.Add(msg);
                if (系统消息.Count > 每频道最大条目数)
                    系统消息.RemoveAt(0);
                break;
        }
    }

    /// <summary>
    /// 获取指定频道的消息列表（不会返回 null）
    /// </summary>
    public List<本地聊天消息> 获取消息(聊天频道 类型)
    {
        switch (类型)
        {
            case 聊天频道.世界:
                return 世界消息;

            case 聊天频道.国家:
                return 国家消息;

            case 聊天频道.家族:
                return 家族消息;

            case 聊天频道.系统:
                return 系统消息;

            case 聊天频道.全部:
            default:
                var all = new List<本地聊天消息>();
                all.AddRange(世界消息);
                all.AddRange(国家消息);
                all.AddRange(家族消息);
                all.AddRange(系统消息);
                all.Sort((a, b) => a.时间戳.CompareTo(b.时间戳));
                return all;
        }
    }

    /// <summary>
    /// 清空指定频道的消息
    /// </summary>
    public void 清空频道(聊天频道 类型)
    {
        switch (类型)
        {
            case 聊天频道.世界:
                世界消息.Clear();
                break;
            case 聊天频道.国家:
                国家消息.Clear();
                break;
            case 聊天频道.家族:
                家族消息.Clear();
                break;
            case 聊天频道.系统:
                系统消息.Clear();
                break;
            case 聊天频道.全部:
            default:
                世界消息.Clear();
                国家消息.Clear();
                家族消息.Clear();
                系统消息.Clear();
                break;
        }
    }
}


