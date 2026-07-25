/// <summary>
/// PlayerID 哨兵常量 — 替代散布在代码中的魔法字符串
/// </summary>
public static class PlayerID
{
    /// <summary>离线/未连接时的默认 ID</summary>
    public const string Offline = "Offline";
    /// <summary>无法确定的 ID</summary>
    public const string Unknown = "99";
    /// <summary>服务器房主 ID</summary>
    public const string Host = "0";
}
