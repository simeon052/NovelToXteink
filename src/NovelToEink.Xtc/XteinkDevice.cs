namespace NovelToEink.Xtc;

/// <summary>出力対象の Xteink 端末。解像度が異なるだけで、フォーマットは共通（1bit XTC/XTG）。</summary>
public enum XteinkDevice
{
    /// <summary>Xteink X3（3.7 インチ / 528×792）。</summary>
    X3,

    /// <summary>Xteink X4 Pro（4.3 インチ / 480×800）。</summary>
    X4Pro,
}

/// <summary><see cref="XteinkDevice"/> のパネル解像度。</summary>
public static class XteinkDeviceInfo
{
    /// <summary>端末のパネル解像度（px）を返す。</summary>
    public static (int Width, int Height) GetResolution(this XteinkDevice device) => device switch
    {
        XteinkDevice.X3 => (528, 792),
        XteinkDevice.X4Pro => (480, 800),
        _ => throw new ArgumentOutOfRangeException(nameof(device), device, "未知の端末です。"),
    };

    /// <summary>UI 表示用の名称。</summary>
    public static string GetDisplayName(this XteinkDevice device) => device switch
    {
        XteinkDevice.X3 => "Xteink X3 (528×792)",
        XteinkDevice.X4Pro => "Xteink X4 Pro (480×800)",
        _ => device.ToString(),
    };

    /// <summary>"X3" / "X4Pro" / "x4-pro" などの文字列を解釈する。</summary>
    public static bool TryParse(string? value, out XteinkDevice device)
    {
        device = XteinkDevice.X4Pro;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var normalized = value.Replace("-", "").Replace("_", "").Replace(" ", "").ToLowerInvariant();
        switch (normalized)
        {
            case "x3":
                device = XteinkDevice.X3;
                return true;
            case "x4pro":
            case "x4":
                device = XteinkDevice.X4Pro;
                return true;
            default:
                return false;
        }
    }
}
