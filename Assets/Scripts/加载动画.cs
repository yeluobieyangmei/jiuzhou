using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class 加载动画:MonoBehaviour
{
    public Slider 滑动条;
    public Text 进度;

    private Coroutine 当前加载协程;

    /// <summary>
    /// 关闭动画（停止所有协程并隐藏）
    /// </summary>
    public void 关闭动画()
    {
        if (当前加载协程 != null)
        {
            StopCoroutine(当前加载协程);
            当前加载协程 = null;
        }
        StopAllCoroutines();
        gameObject.SetActive(false);
    }

    /// <summary>
    /// 通用加载动画方法：让滑动条平滑地从0到1，在指定时间后自动关闭
    /// </summary>
    /// <param name="加载时间">加载动画持续时间（秒）</param>
    /// <param name="提示文本">显示的提示文本（可选，默认为"加载中..."）</param>
    public void 开始加载动画(float 加载时间, string 提示文本 = "加载中...")
    {
        // 如果已经有加载动画在运行，先停止它
        if (当前加载协程 != null)
        {
            StopCoroutine(当前加载协程);
        }

        // 显示加载动画
        gameObject.SetActive(true);
        
        // 设置初始状态
        if (滑动条 != null)
        {
            滑动条.value = 0f;
        }
        if (进度 != null)
        {
            进度.text = 提示文本;
        }

        // 启动加载协程
        当前加载协程 = StartCoroutine(加载动画协程(加载时间));
    }

    /// <summary>
    /// 加载动画协程：平滑地更新进度条
    /// </summary>
    private IEnumerator 加载动画协程(float 加载时间)
    {
        float 已用时间 = 0f;
        float 更新间隔 = 0.05f; // 每0.05秒更新一次，让进度条更平滑

        while (已用时间 < 加载时间)
        {
            yield return new WaitForSeconds(更新间隔);
            已用时间 += 更新间隔;

            // 计算进度（0到1之间）
            float 进度值 = 已用时间 / 加载时间;
            
            // 更新滑动条
            if (滑动条 != null)
            {
                滑动条.value = 进度值;
            }

            // 可选：更新进度文本显示百分比
            if (进度 != null)
            {
                // 保持提示文本，不显示百分比（如果需要显示百分比，可以取消下面的注释）
                // 进度.text = $"{提示文本} {(进度值 * 100):F0}%";
            }
        }

        // 确保进度条到达100%
        if (滑动条 != null)
        {
            滑动条.value = 1f;
        }

        // 等待一小段时间让用户看到100%
        yield return new WaitForSeconds(0.1f);

        // 关闭动画
        关闭动画();
    }
}