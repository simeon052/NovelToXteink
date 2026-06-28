using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace NovelToEink.Core;

/// <summary>テキスト校正ルール1件。JSONで外部化されユーザーが編集できる。</summary>
public sealed class ProofreadingRule
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 適用対象と方法:
    /// "text_replace"  … テキストノードのみに単純置換
    /// "text_regex"    … テキストノードのみに正規表現置換
    /// "html_replace"  … HTML全体に単純置換（タグを跨ぐ変換に使う）
    /// "html_regex"    … HTML全体に正規表現置換（&lt;p&gt;***&lt;/p&gt;→&lt;hr/&gt; 等）
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text_replace";

    /// <summary>置換元文字列（*_replace 型で使用）</summary>
    [JsonPropertyName("from")]
    public string? From { get; set; }

    /// <summary>置換先文字列（*_replace 型で使用）</summary>
    [JsonPropertyName("to")]
    public string? To { get; set; }

    /// <summary>正規表現パターン（*_regex 型で使用）</summary>
    [JsonPropertyName("pattern")]
    public string? Pattern { get; set; }

    /// <summary>正規表現置換文字列（*_regex 型で使用。$1 等のグループ参照可）</summary>
    [JsonPropertyName("replacement")]
    public string? Replacement { get; set; }
}

/// <summary>
/// エピソード本文 XHTML に校正ルールを適用するサービス。
/// ルールは %AppData%\NovelToEink\proofreading.json で管理し、ユーザーが自由に編集できる。
/// </summary>
public sealed class ProofreadingService
{
    private readonly List<ProofreadingRule> _rules;

    private static readonly JsonSerializerOptions ReadOpts = new();
    private static readonly JsonSerializerOptions WriteOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 日本語をそのまま出力
    };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "NovelToEink", "proofreading.json");

    public ProofreadingService(IEnumerable<ProofreadingRule> rules)
    {
        _rules = [.. rules];
    }

    /// <summary>
    /// ルールファイルを読み込む。ファイルが無い場合は narou.rb 互換デフォルトを保存して返す。
    /// 新しいデフォルトルールが既存ファイルに未登録の場合は末尾に追記して保存する。
    /// </summary>
    public static ProofreadingService Load(string? path = null)
    {
        path ??= DefaultPath;
        if (File.Exists(path))
        {
            try
            {
                var rules = JsonSerializer.Deserialize<List<ProofreadingRule>>(
                    File.ReadAllText(path), ReadOpts);
                if (rules != null)
                {
                    var updated = false;
                    foreach (var def in DefaultRules)
                    {
                        if (!rules.Any(r => r.Name == def.Name))
                        {
                            rules.Add(def);
                            updated = true;
                        }
                    }
                    if (updated) SaveRules(rules, path);
                    return new ProofreadingService(rules);
                }
            }
            catch { }
        }
        // 初回起動時にデフォルトルールを保存
        SaveRules(DefaultRules, path);
        return new ProofreadingService(DefaultRules);
    }

    public static void SaveRules(IEnumerable<ProofreadingRule> rules, string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(rules.ToList(), WriteOpts));
        }
        catch { }
    }

    /// <summary>有効な text_replace ルールを (From, To) リストで返す（PatchVertical 用）。</summary>
    public IReadOnlyList<(string From, string To)> GetEnabledTextReplacements()
        => _rules
            .Where(r => r.Enabled && r.Type == "text_replace" && r.From != null && r.To != null)
            .Select(r => (r.From!, r.To!))
            .ToList();

    /// <summary>エピソード本文 XHTML に校正ルールを適用して返す。</summary>
    public string Apply(string bodyHtml)
    {
        foreach (var rule in _rules.Where(r => r.Enabled))
        {
            try
            {
                bodyHtml = rule.Type switch
                {
                    "text_replace" => ApplyToTextNodes(bodyHtml,
                        s => s.Replace(rule.From ?? "", rule.To ?? "", StringComparison.Ordinal)),
                    "text_regex" => ApplyToTextNodes(bodyHtml,
                        s => Regex.Replace(s, rule.Pattern ?? "(?!)", rule.Replacement ?? "")),
                    "html_replace" => bodyHtml.Replace(rule.From ?? "", rule.To ?? "", StringComparison.Ordinal),
                    "html_regex" => Regex.Replace(bodyHtml, rule.Pattern ?? "(?!)", rule.Replacement ?? ""),
                    _ => bodyHtml,
                };
            }
            catch { /* ルールの正規表現エラー等は無視して次のルールへ */ }
        }
        return bodyHtml;
    }

    /// <summary>HTML のテキストノード部分（タグ外）にのみ変換関数を適用する。</summary>
    private static string ApplyToTextNodes(string html, Func<string, string> transform)
    {
        var sb = new StringBuilder(html.Length);
        var i = 0;
        while (i < html.Length)
        {
            var tagStart = html.IndexOf('<', i);
            if (tagStart < 0)
            {
                sb.Append(transform(html[i..]));
                break;
            }
            if (tagStart > i)
                sb.Append(transform(html[i..tagStart]));
            var tagEnd = html.IndexOf('>', tagStart);
            if (tagEnd < 0) { sb.Append(html[tagStart..]); break; }
            sb.Append(html[tagStart..(tagEnd + 1)]);
            i = tagEnd + 1;
        }
        return sb.ToString();
    }

    // ---- narou.rb 互換デフォルトルール ----

    public static List<ProofreadingRule> DefaultRules =>
    [
        new()
        {
            Name = "感嘆符・疑問符後の全角スペース",
            Description = "！または？の直後に全角スペースがない場合に補う。閉じ括弧・記号・行末の前は除く。(narou.rb互換)",
            Enabled = true,
            Type = "text_regex",
            Pattern = @"([！？])([^」』）】　\s！？…―])",
            Replacement = "$1　$2",
        },
        new()
        {
            Name = "三点リーダー統一（半角ピリオド3つ）",
            Description = "「...」を「……」に統一する。(narou.rb互換)",
            Enabled = true,
            Type = "text_replace",
            From = "...",
            To = "……",
        },
        new()
        {
            Name = "三点リーダー統一（中黒3つ）",
            Description = "「・・・」を「……」に統一する。(narou.rb互換)",
            Enabled = true,
            Type = "text_replace",
            From = "・・・",
            To = "……",
        },
        new()
        {
            Name = "二点リーダーを三点リーダーへ",
            Description = "「‥」(U+2025) を「…」(U+2026) に統一する。(narou.rb互換)",
            Enabled = true,
            Type = "text_replace",
            From = "‥",
            To = "…",
        },
        new()
        {
            Name = "ダッシュ統一（半角ハイフン2つ）",
            Description = "「--」を「――」に統一する。(narou.rb互換)",
            Enabled = true,
            Type = "text_replace",
            From = "--",
            To = "――",
        },
        new()
        {
            Name = "ダッシュ統一（全角ハイフン2つ）",
            Description = "「－－」を「――」に統一する。(narou.rb互換)",
            Enabled = true,
            Type = "text_replace",
            From = "－－",
            To = "――",
        },
        new()
        {
            Name = "EMダッシュ統一",
            Description = "「—」(U+2014 EMダッシュ) を「――」に統一する。(narou.rb互換)",
            Enabled = true,
            Type = "text_replace",
            From = "—",
            To = "――",
        },
        new()
        {
            Name = "感嘆符後の句点除去",
            Description = "「！。」の組み合わせから不要な句点を除去する。(narou.rb互換)",
            Enabled = true,
            Type = "text_replace",
            From = "！。",
            To = "！",
        },
        new()
        {
            Name = "疑問符後の句点除去",
            Description = "「？。」の組み合わせから不要な句点を除去する。(narou.rb互換)",
            Enabled = true,
            Type = "text_replace",
            From = "？。",
            To = "？",
        },
        new()
        {
            Name = "波ダッシュ統一",
            Description = "「～」(U+FF5E 全角チルダ) を「〜」(U+301C 波ダッシュ) に統一する。縦書きで U+FF5E が正しく回転しないリーダー対策。",
            Enabled = true,
            Type = "text_replace",
            From = "～",
            To = "〜",
        },
        new()
        {
            Name = "アスタリスク区切り線",
            Description = "「***」「＊＊＊」のみの段落を水平線 <hr/> に変換する。(narou.rb互換)",
            Enabled = true,
            Type = "html_regex",
            Pattern = @"<p>[ 　]*[＊\*]{3,}[ 　]*</p>",
            Replacement = "<hr/>",
        },
        new()
        {
            Name = "半角感嘆符を全角に",
            Description = "「!」を「！」に変換する。英語タイトルや固有名詞に影響する可能性があるため既定は無効。",
            Enabled = false,
            Type = "text_replace",
            From = "!",
            To = "！",
        },
        new()
        {
            Name = "半角疑問符を全角に",
            Description = "「?」を「？」に変換する。英語タイトルや固有名詞に影響する可能性があるため既定は無効。",
            Enabled = false,
            Type = "text_replace",
            From = "?",
            To = "？",
        },
        new()
        {
            Name = "半角縦棒を全角に",
            Description = "「|」を「｜」に変換する（ルビ記法の外での縦棒）。",
            Enabled = false,
            Type = "text_replace",
            From = "|",
            To = "｜",
        },
        new()
        {
            Name = "半角コロンを全角に",
            Description = "「:」を「：」に変換する。時刻表記（12:30 等）にも影響するため既定は無効。",
            Enabled = false,
            Type = "text_replace",
            From = ":",
            To = "：",
        },
    ];
}
