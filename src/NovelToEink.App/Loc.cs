using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Markup;
using NovelToEink.Core;

namespace NovelToEink.App;

/// <summary>
/// XAML 用マークアップ拡張。{loc:Loc key} で現在ロケール（OS言語 or Override）を解決する。
/// 静的値（XAML加载時に確定する値）は LocRegistry に登録され、言語変更時に再解決される。
/// VM 側の Locale.T(...) はバインド評価時に毎回解決されるため登録不要。
/// </summary>
public class LocExtension : MarkupExtension
{
    private readonly string _key;

    public LocExtension(string key) => _key = key;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (serviceProvider.GetService(typeof(IProvideValueTarget)) is IProvideValueTarget target
            && target.TargetObject is DependencyObject obj
            && target.TargetProperty is DependencyProperty dp)
        {
            LocRegistry.Register(obj, dp, _key);
        }
        return Locale.T(_key);
    }
}

/// <summary>静的 XAML 値の登録表。言語切替時に再解決して対象へ書き戻す。</summary>
public static class LocRegistry
{
    private static readonly List<(DependencyObject Target, DependencyProperty Property, string Key)> Entries = new();

    public static void Register(DependencyObject target, DependencyProperty property, string key) =>
        Entries.Add((target, property, key));

    public static void Refresh()
    {
        foreach (var (target, property, key) in Entries)
            target.SetValue(property, Locale.T(key));
    }
}
