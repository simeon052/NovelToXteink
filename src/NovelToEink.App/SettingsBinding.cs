using NovelToEink.Core;

namespace NovelToEink.App;

/// <summary>
/// <see cref="AppSettings"/> の 1 項目を書き換えて保存するためのヘルパー。
/// 設定値の保持場所は <see cref="AppSettings"/> だけにして、ViewModel 側にコピーを持たない。
/// 変更通知（PropertyChanged）は、<c>true</c> が返ったときだけ呼び出し側の ViewModel が上げる。
/// </summary>
internal static class SettingsBinding
{
    /// <summary>値が変わっていれば設定へ書き込んで保存する。変わったときだけ true を返す。</summary>
    public static bool Write<T>(AppSettings settings, T current, T value, Action<T> assign)
    {
        if (EqualityComparer<T>.Default.Equals(current, value)) return false;
        assign(value);
        settings.Save();
        return true;
    }

    /// <summary>範囲 [min, max] に丸めてから <see cref="Write{T}"/> する。</summary>
    public static bool WriteClamped(AppSettings settings, int current, int value, int min, int max, Action<int> assign)
        => Write(settings, current, Math.Clamp(value, min, max), assign);

    /// <summary>下限だけを丸めてから <see cref="Write{T}"/> する。</summary>
    public static bool WriteMin(AppSettings settings, int current, int value, int min, Action<int> assign)
        => Write(settings, current, Math.Max(min, value), assign);
}
