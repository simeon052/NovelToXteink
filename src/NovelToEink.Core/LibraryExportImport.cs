using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using NovelToEink.Xtc;

namespace NovelToEink.Core;

/// <summary>
/// ライブラリと設定のエクスポート/インポート機能。
/// </summary>
public static class LibraryExportImport
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    // ── 設定マッピング（唯一の定義場所） ──────────────────────────────
    /// <summary>
    /// AppSettings の各プロパティと JSON キーのマッピングを定義する。
    /// この配列に追加すれば Serialize / Apply が自動的に追従する。
    /// </summary>
    private static readonly (string JsonKey, PropertyInfo Property, Type TargetType)[] SettingsMap =
    [
        ("Vertical",              typeof(AppSettings).GetProperty(nameof(AppSettings.Vertical))!,              typeof(bool)),
        ("GrayscaleImages",       typeof(AppSettings).GetProperty(nameof(AppSettings.GrayscaleImages))!,       typeof(bool)),
        ("IncludeInlineImages",   typeof(AppSettings).GetProperty(nameof(AppSettings.IncludeInlineImages))!,   typeof(bool)),
        ("KeepRuby",              typeof(AppSettings).GetProperty(nameof(AppSettings.KeepRuby))!,              typeof(bool)),
        ("EnableProofreading",    typeof(AppSettings).GetProperty(nameof(AppSettings.EnableProofreading))!,    typeof(bool)),
        ("EpisodesPerFile",       typeof(AppSettings).GetProperty(nameof(AppSettings.EpisodesPerFile))!,       typeof(int)),
        ("RequestDelayMs",        typeof(AppSettings).GetProperty(nameof(AppSettings.RequestDelayMs))!,        typeof(int)),
        ("IsDarkMode",            typeof(AppSettings).GetProperty(nameof(AppSettings.IsDarkMode))!,            typeof(bool)),
        // XTC 設定
        ("GenerateXtc",           typeof(AppSettings).GetProperty(nameof(AppSettings.GenerateXtc))!,           typeof(bool)),
        ("XtcDevice",             typeof(AppSettings).GetProperty(nameof(AppSettings.XtcDevice))!,             typeof(XteinkDevice)),
        ("XtcFontFile",           typeof(AppSettings).GetProperty(nameof(AppSettings.XtcFontFile))!,           typeof(string)),
        ("XtcFontSize",           typeof(AppSettings).GetProperty(nameof(AppSettings.XtcFontSize))!,           typeof(int)),
        ("XtcTextThreshold",      typeof(AppSettings).GetProperty(nameof(AppSettings.XtcTextThreshold))!,      typeof(int)),
        ("XtcPaddingTop",         typeof(AppSettings).GetProperty(nameof(AppSettings.XtcPaddingTop))!,         typeof(int)),
        ("XtcPaddingBottom",      typeof(AppSettings).GetProperty(nameof(AppSettings.XtcPaddingBottom))!,      typeof(int)),
        ("XtcPaddingLeft",        typeof(AppSettings).GetProperty(nameof(AppSettings.XtcPaddingLeft))!,        typeof(int)),
        ("XtcPaddingRight",       typeof(AppSettings).GetProperty(nameof(AppSettings.XtcPaddingRight))!,       typeof(int)),
    ];

    /// <summary>SettingsMap のキー一覧（エクスポート順）。</summary>
    internal static IReadOnlyList<string> SettingKeys => [.. SettingsMap.Select(s => s.JsonKey)];

    // ── エクスポート構造体 ────────────────────────────────────────────
    /// <summary>エクスポートデータの構造体。</summary>
    public sealed class ExportData
    {
        public string ExportedAt { get; set; } = "";
        public string AppVersion { get; set; } = "";
        public IReadOnlyList<LibraryEntry> Library { get; set; } = [];
        public AppSettingsExport Settings { get; set; } = new();
    }

    /// <summary>エクスポートされる設定のみのコピー。</summary>
    public sealed class AppSettingsExport
    {
        public bool Vertical { get; set; }
        public bool GrayscaleImages { get; set; }
        public bool IncludeInlineImages { get; set; }
        public bool KeepRuby { get; set; }
        public bool EnableProofreading { get; set; }
        public int EpisodesPerFile { get; set; }
        public int RequestDelayMs { get; set; }
        public bool IsDarkMode { get; set; }
        // XTC 設定
        public bool GenerateXtc { get; set; }
        public string XtcDevice { get; set; } = "";
        public string XtcFontFile { get; set; } = "";
        public int XtcFontSize { get; set; }
        public int XtcTextThreshold { get; set; }
        public int XtcPaddingTop { get; set; }
        public int XtcPaddingBottom { get; set; }
        public int XtcPaddingLeft { get; set; }
        public int XtcPaddingRight { get; set; }
    }

    /// <summary>インポートデータの構造体（読み込み用）。</summary>
    public sealed class ImportData
    {
        public IReadOnlyList<LibraryEntry>? Library { get; set; }
        public Dictionary<string, string?>? Settings { get; set; }
    }

    // ── シリアライズ / デシリアライズ ────────────────────────────────
    /// <summary>ライブラリと設定をJSONファイルへエクスポートする。</summary>
    public static string SerializeExport(string appVersion, IReadOnlyList<LibraryEntry> entries, AppSettings settings)
    {
        var exportData = new ExportData
        {
            ExportedAt = DateTimeOffset.Now.ToString("o"),
            AppVersion = appVersion,
            Library = entries,
            Settings = BuildExportSettings(settings),
        };

        return JsonSerializer.Serialize(exportData, JsonOpts);
    }

    /// <summary>AppSettings の値を AppSettingsExport にコピーする。</summary>
    private static AppSettingsExport BuildExportSettings(AppSettings settings)
    {
        var export = new AppSettingsExport();
        foreach (var (jsonKey, prop, _) in SettingsMap)
        {
            var value = prop.GetValue(settings);
            if (jsonKey == "XtcDevice" && value is XteinkDevice dev)
                typeof(AppSettingsExport).GetProperty(jsonKey)!.SetValue(export, dev.ToString());
            else
                typeof(AppSettingsExport).GetProperty(jsonKey)!.SetValue(export, value);
        }
        return export;
    }

    /// <summary>JSON文字列からライブラリと設定をデシリアライズする。</summary>
    public static (IReadOnlyList<LibraryEntry> Library, Dictionary<string, string?> Settings) DeserializeExport(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("JSONのルート要素がオブジェクトではありません。");

        if (!root.TryGetProperty("Library", out var libraryElement) || libraryElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Library 配列が見つからないか、形式が不正です。");
        if (!root.TryGetProperty("Settings", out var settingsObj) || settingsObj.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Settings オブジェクトが見つからないか、形式が不正です。");

        var libraryJson = libraryElement.GetRawText();
        var library = JsonSerializer.Deserialize<List<LibraryEntry>>(libraryJson, JsonOpts) ?? [];

        var settings = new Dictionary<string, string?>();
        foreach (var prop in settingsObj.EnumerateObject())
        {
            settings[prop.Name] = prop.Value.ToString();
        }

        return (library, settings);
    }

    /// <summary>エクスポートされた設定をAppSettingsに適用する。</summary>
    public static void ApplyImportedSettings(AppSettings settings, Dictionary<string, string?> importedSettings)
    {
        foreach (var (jsonKey, prop, targetType) in SettingsMap)
        {
            if (!importedSettings.TryGetValue(jsonKey, out var raw))
                continue;

            // enum 系（XtcDevice）
            if (targetType.IsEnum && raw != null)
            {
                if (Enum.TryParse(targetType, raw, out var parsed))
                    prop.SetValue(settings, parsed);
            }
            // bool
            else if (targetType == typeof(bool) && raw != null)
            {
                if (bool.TryParse(raw, out var b))
                    prop.SetValue(settings, b);
            }
            // int
            else if (targetType == typeof(int) && raw != null)
            {
                if (int.TryParse(raw, out var i))
                    prop.SetValue(settings, i);
            }
            // string
            else if (targetType == typeof(string))
            {
                prop.SetValue(settings, raw);
            }
        }
    }
}
