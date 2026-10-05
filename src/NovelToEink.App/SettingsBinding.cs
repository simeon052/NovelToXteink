using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NovelToEink.App;

/// <summary>
/// AppSettings のプロパティを ViewModel にバインドするためのヘルパー。
/// 値の変更時に自動的に Settings.Save() を呼び出す。
/// </summary>
public static class SettingsBinding
{
    /// <summary>
    /// 設定値を取得・設定する。値が変更されたら Settings を保存し、OnChanged を呼ぶ。
    /// </summary>
    public static bool Bind<T>(
        ref T field,
        T value,
        Action? onSave,
        Action<string?> notify)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        onSave?.Invoke();
        notify(null);
        return true;
    }

    /// <summary>
    /// クランプ付き設定バインディング。min/max で値を制限する。
    /// </summary>
    public static bool BindClamped(
        ref int field,
        int value,
        int min,
        int max,
        Action? onSave,
        Action<string?> notify,
        [CallerMemberName] string? propertyName = null)
    {
        var clamped = Math.Clamp(value, min, max);
        if (field == clamped) return false;
        field = clamped;
        onSave?.Invoke();
        notify(propertyName);
        return true;
    }

    /// <summary>
    /// 下限のみチェック付き設定バインディング。
    /// </summary>
    public static bool BindClampedMin(
        ref int field,
        int value,
        int min,
        Action? onSave,
        Action<string?> notify,
        [CallerMemberName] string? propertyName = null)
    {
        var clamped = Math.Max(min, value);
        if (field == clamped) return false;
        field = clamped;
        onSave?.Invoke();
        notify(propertyName);
        return true;
    }
}
