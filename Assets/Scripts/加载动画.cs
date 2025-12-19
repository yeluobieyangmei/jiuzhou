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

    public void 关闭动画()
    {
        StopAllCoroutines();
        gameObject.SetActive(false);
    }


}