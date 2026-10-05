using System.IO;
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

    /// <summary>ライブラリと設定をJSONファイルへエクスポートする。</summary>
    public static string SerializeExport(string appVersion, IReadOnlyList<LibraryEntry> entries, AppSettings settings)
    {
        var exportData = new ExportData
        {
            ExportedAt = DateTimeOffset.Now.ToString("o"),
            AppVersion = appVersion,
            Library = entries,
            Settings = new AppSettingsExport
            {
                Vertical = settings.Vertical,
                GrayscaleImages = settings.GrayscaleImages,
                IncludeInlineImages = settings.IncludeInlineImages,
                KeepRuby = settings.KeepRuby,
                EnableProofreading = settings.EnableProofreading,
                EpisodesPerFile = settings.EpisodesPerFile,
                RequestDelayMs = settings.RequestDelayMs,
                IsDarkMode = settings.IsDarkMode,
                // XTC 設定
                GenerateXtc = settings.GenerateXtc,
                XtcDevice = settings.XtcDevice.ToString(),
                XtcFontFile = settings.XtcFontFile,
                XtcFontSize = settings.XtcFontSize,
                XtcTextThreshold = settings.XtcTextThreshold,
                XtcPaddingTop = settings.XtcPaddingTop,
                XtcPaddingBottom = settings.XtcPaddingBottom,
                XtcPaddingLeft = settings.XtcPaddingLeft,
                XtcPaddingRight = settings.XtcPaddingRight,
            },
        };

        return JsonSerializer.Serialize(exportData, JsonOpts);
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
        if (importedSettings.TryGetValue("Vertical", out var v) && bool.TryParse(v, out var vertical))
            settings.Vertical = vertical;
        if (importedSettings.TryGetValue("GrayscaleImages", out var g) && bool.TryParse(g, out var grayscale))
            settings.GrayscaleImages = grayscale;
        if (importedSettings.TryGetValue("IncludeInlineImages", out var i) && bool.TryParse(i, out var inline))
            settings.IncludeInlineImages = inline;
        if (importedSettings.TryGetValue("KeepRuby", out var r) && bool.TryParse(r, out var ruby))
            settings.KeepRuby = ruby;
        if (importedSettings.TryGetValue("EnableProofreading", out var p) && bool.TryParse(p, out var proof))
            settings.EnableProofreading = proof;
        if (importedSettings.TryGetValue("EpisodesPerFile", out var e) && int.TryParse(e, out var episodes))
            settings.EpisodesPerFile = episodes;
        if (importedSettings.TryGetValue("RequestDelayMs", out var d) && int.TryParse(d, out var delay))
            settings.RequestDelayMs = delay;
        if (importedSettings.TryGetValue("IsDarkMode", out var dark) && bool.TryParse(dark, out var darkMode))
            settings.IsDarkMode = darkMode;

        // XTC 設定
        if (importedSettings.TryGetValue("GenerateXtc", out var gx) && bool.TryParse(gx, out var genXtc))
            settings.GenerateXtc = genXtc;
        if (importedSettings.TryGetValue("XtcDevice", out var xd) && Enum.TryParse(xd, out XteinkDevice device))
            settings.XtcDevice = device;
        if (importedSettings.TryGetValue("XtcFontFile", out var xf))
            settings.XtcFontFile = xf ?? "";
        if (importedSettings.TryGetValue("XtcFontSize", out var xs) && int.TryParse(xs, out var fontSize))
            settings.XtcFontSize = fontSize;
        if (importedSettings.TryGetValue("XtcTextThreshold", out var xt) && int.TryParse(xt, out var textThreshold))
            settings.XtcTextThreshold = textThreshold;
        if (importedSettings.TryGetValue("XtcPaddingTop", out var xtp) && int.TryParse(xtp, out var paddingTop))
            settings.XtcPaddingTop = paddingTop;
        if (importedSettings.TryGetValue("XtcPaddingBottom", out var xbp) && int.TryParse(xbp, out var paddingBottom))
            settings.XtcPaddingBottom = paddingBottom;
        if (importedSettings.TryGetValue("XtcPaddingLeft", out var xlp) && int.TryParse(xlp, out var paddingLeft))
            settings.XtcPaddingLeft = paddingLeft;
        if (importedSettings.TryGetValue("XtcPaddingRight", out var xrp) && int.TryParse(xrp, out var paddingRight))
            settings.XtcPaddingRight = paddingRight;
    }
}
