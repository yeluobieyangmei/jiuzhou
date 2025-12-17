[System.Serializable]
public class 登录结果
{
    // 是否成功
    public bool success;

    // 提示信息
    public string message;

    // 令牌
    public string token;

    // 账号ID（新增）
    public int accountId;
}