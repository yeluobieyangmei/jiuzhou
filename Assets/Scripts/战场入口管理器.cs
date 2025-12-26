using UnityEngine;

/// <summary>
/// 战场入口管理器
/// 负责管理战场入口UI的显示
/// </summary>
public class 战场入口管理器 : MonoBehaviour
{
    public static 战场入口管理器 实例 { get; private set; }

    [Header("窗口对象")]
    public GameObject 窗口对象;

    private void Awake()
    {
        // 单例模式
        if (实例 != null && 实例 != this)
        {
            Destroy(gameObject);
            return;
        }

        实例 = this;
        DontDestroyOnLoad(gameObject);

        // 初始隐藏窗口
        if (窗口对象 != null)
        {
            窗口对象.SetActive(false);
        }

        Debug.Log("[战场入口管理器] 初始化完成");
    }

    /// <summary>
    /// 显示战场入口窗口
    /// </summary>
    public void 显示窗口()
    {
        if (窗口对象 != null)
        {
            窗口对象.SetActive(true);
            Debug.Log("[战场入口管理器] 窗口已显示");
        }
        else
        {
            Debug.LogWarning("[战场入口管理器] 窗口对象未设置");
        }
    }

    /// <summary>
    /// 隐藏战场入口窗口
    /// </summary>
    public void 隐藏窗口()
    {
        if (窗口对象 != null)
        {
            窗口对象.SetActive(false);
            Debug.Log("[战场入口管理器] 窗口已隐藏");
        }
    }
}
