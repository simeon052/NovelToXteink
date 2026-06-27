# Novel To Xteink — 再現プロンプト

> このファイルは、同等のアプリをゼロから再実装するための仕様プロンプトです。
> 実際のコードよりも「なぜその設計か」「どこにハマるか」の情報を重視しています。

---

## 依頼内容

日本のWeb小説サイト（小説家になろう / カクヨム）から小説をダウンロードし、
E-Inkリーダー「Xteink X3」向けに最適化した EPUB3 ファイルを生成・管理する
Windows デスクトップアプリ（WPF）とCLIツールを C# .NET 10 で作成してください。

アプリ名: **Novel To Xteink**

---

## 技術スタック（厳守事項）

| 項目 | 値 |
|------|-----|
| .NET | 10.0 |
| UI | WPF (`net10.0-windows`) |
| ソリューション形式 | `.slnx`（`dotnet new sln` で自動生成される新形式） |
| 画像処理 | **SixLabors.ImageSharp 3.1.x** / **SixLabors.ImageSharp.Drawing 2.1.x** |
| HTML解析 | HtmlAgilityPack 1.12.x |
| EPUB生成 | `System.IO.Compression.ZipArchive`（外部ライブラリ不使用） |
| パターン | MVVM（ViewModelBase / RelayCommand / AsyncRelayCommand を自前実装） |

**⚠️ ImageSharp バージョンピン留め必須**  
v3.1.x と Drawing v2.1.x は Apache-2.0。v4 / Drawing v3 以降は商用ライセンス必須で
`No Six Labors license found` エラーでビルドが失敗する。必ず `3.1.x` / `2.1.x` に固定すること。

---

## プロジェクト構成

```
NovelToEink.slnx
src/
  NovelToEink.Core/        # classlib net10.0  ← UI非依存
  NovelToEink.App/         # WPF net10.0-windows
  NovelToEink.Cli/         # console net10.0
```

`Core` は `App` にも `Cli` にも参照される。UIに依存するコードは一切 Core に入れない。

---

## Core の実装仕様

### スクレイパー

`INovelScraper` インターフェースを実装するクラスを2つ作る。

```csharp
interface INovelScraper
{
    NovelSite Site { get; }
    bool CanHandle(string url);
    (NovelSite site, string workId)? TryParseWorkId(string url); // ネット不要・URL解析のみ
    Task<NovelMetadata> GetMetadataAsync(string url, CancellationToken ct);
    Task<IReadOnlyList<EpisodeRef>> GetTableOfContentsAsync(string url, CancellationToken ct);
    Task<EpisodeContent> GetEpisodeAsync(EpisodeRef episode, EpubOptions options, CancellationToken ct);
}
```

**SyosetuScraper（なろう）**
- URL正規表現: `^https?://(ncode|novel18)\.syosetu\.com/([nN]\d+[a-z]+)`
- 目次HTML: `p-eplist__subtitle`（新）/ `subtitle`（旧）両対応。`?p=N` ページング追従（最大500ページ）
- 本文: `p-novel__text`（preface/afterword 分離）/ `novel_honbun`（旧）
- 前書き・後書きを別フィールドで保持
- 完結判定・最終更新日: `https://api.syosetu.com/novelapi/api/?out=json&gzip=0&of=e-nt-gl&ncode=...`
  - `end==0` → 完結。R18は `novel18api` サブドメイン
  - `general_lastup` → JST として `DateTimeOffset` に変換
- 公式表紙URL: なし（`OfficialCoverUrl = null`）

**KakuyomuScraper（カクヨム）**
- URL正規表現: `^https?://kakuyomu\.jp/works/(\d+)`
- `__NEXT_DATA__` スクリプトタグから JSON を取り出し、`props.pageProps.__APOLLO_STATE__` を解析
- Work エンティティを `Work:` プレフィックスで特定。`id` フィールドで URL の workId と照合
- 目次: `Work.tableOfContents` → 章 → `episodeUnions`（`__ref` 参照解決）
- 著者: `Work.author.__ref` → `UserAccount.activityName`
- 公式表紙: Work 内で `coverImageUrl` を含むプロパティを走査
- 完結: `serialStatus == "COMPLETED"`
- 最終更新: `lastEpisodePublishedAt`（ISO8601）

### HttpFetcher

- ブラウザ偽装UA・cookieコンテナ（`over18=yes` を syosetu にセット）
- リクエスト間ウェイト（既定1500ms）
- 429/5xx に対してリトライ（3回、指数バックオフ）

### EPUB生成（EpubBuilder）

EPUB3 + XHTML + CSS。外部ライブラリ不使用で `ZipArchive` を直接操作する。

```
mimetype                   ← 無圧縮・先頭必須
META-INF/container.xml
OEBPS/content.opf          ← dc:title, dc:creator, spine
OEBPS/nav.xhtml            ← EPUB3必須のナビゲーション文書
OEBPS/style.css            ← 縦書き/横書き切替、ルビ、フォントサイズ
OEBPS/cover.xhtml + cover.jpg
OEBPS/ep_0001.xhtml ... ep_NNNN.xhtml  ← 1話 = 1 XHTML
OEBPS/images/img_XXXX.jpg  ← 挿絵（エピソードインデックス_連番）
```

- **分割**: `EpisodesPerFile`（既定200）話ごとに別EPUBファイルを生成（`BuildSplit`）
- **ファイル名**: `NameFormatter.Format(template, title, author, part, partCount)`
  - デフォルトテンプレート: `{title}-{part}-{author}`
  - `Sanitize` で全角スペース→半角スペース変換 + 無効文字→`_`
- **表紙**: グレースケール/リサイズ済みJPEG。分割時は各パートにタイトル`（i/n）`付き
- **テキスト校正**: `ProofreadingService.Apply(bodyHtml)` を本文・前書き・後書きに適用
  - HTML タグ内を変換しないよう `<...>` で分割してテキストノードのみ変換

### テキスト校正（ProofreadingService）

設定ファイル: `%AppData%\NovelToEink\proofreading.json`（初回起動時にデフォルト生成）

ルール型:
```json
{ "name": "三点リーダー統一", "type": "text_regex", "enabled": true,
  "pattern": "\\.\\.\\.", "replacement": "……" }
```
型: `text_replace` / `text_regex` / `html_replace` / `html_regex`

デフォルトで有効な10ルール（narou.rb互換）:
1. 感嘆符・疑問符の後に全角スペース挿入（`([！？])([^」』）】　\s！？…―])` → `$1　$2`）
2. `...` → `……`（半角ピリオド3個）
3. `・・・` → `……`（中黒3個）
4. `‥` → `…`（二点リーダー）
5. `--` → `――`
6. `－－` → `――`
7. `—` → `――`
8. `！。` → `！` / `？。` → `？`
9. アスタリスク区切り線（`html_regex`: `<p>[ 　]*[＊\*]{3,}[ 　]*</p>` → `<hr/>`）
10. 〔他1件〕

### DownloadCache

保存先: `{OutputFolder}/.cache/{site}_{workId}/`

- エピソード: `ep_NNNN.json`（URL一致チェック付き）
- 画像: `img_{md5hex}.bin`
- メタデータ: `meta.json`（TTL 1時間。`UpdateAsync` では `forceRefreshMeta:true` で強制再取得）

`TryParseWorkId(url)` でキャッシュを作成してからネット取得 → メタキャッシュを初期化できる。

### LibraryService / Library

- `LibraryEntry`: URL・Site・WorkId・Title・Author・話数・最終話タイトル・EPUBパス（分割全件）・オプション・各日時・UpdateStatus
- `library.json` を OutputFolder 直下に保存（`System.Text.Json`、`WriteIndented`、`JsonStringEnumConverter`）
- `AppSettings`: `%AppData%\NovelToEink\settings.json`
  - `OutputFolder`, `RequestDelayMs`, `Vertical`, `GrayscaleImages`, `IncludeInlineImages`,
    `KeepRuby`, `EnableProofreading`, `EpisodesPerFile`, `IsDarkMode`
- 更新チェック: `HasUpdate(toc)` = 話数差分 OR 最終話タイトル不一致
- 削除時はキャッシュディレクトリ（`.cache/{site}_{workId}/`）も削除

---

## 表紙ピッカー（CoverPickerWindow）

ダウンロード後に表示するモーダルダイアログ。

表紙候補の種類:
1. **公式表紙** — OfficialCoverUrl から取得した画像（カクヨムのみ）
2. **挿絵** — 本文中の画像を早い順に最大5枚
3. **文字生成** — タイトル・著者名を Yu Gothic で描画した画像（ImageSharp.Drawing）
4. **なし** — 表紙なしEPUB
5. **手動追加** — 以下3方式でユーザが任意画像を追加可能:
   - 「📂 ファイルから追加」ボタン → OpenFileDialog（jpg/png/bmp/webp/gif）
   - 画像ファイルのドラッグ＆ドロップ（`AllowDrop="True"`）
   - Ctrl+V 貼り付け（クリップボードのファイルパスまたは画像データ）

候補はサムネイル付きListBoxで表示。選択後「この表紙でEPUBを生成」で確定。
キャンセル時は追加スキップ（URL処理ループの次のURLへ進む）。

---

## GUI（MainWindow）

アクセントカラー: `#4F46E5`（インディゴ）

スタイル体系（`Styles.xaml`）:
- ボタン: `Accent`（塗り）/ `Ghost`（枠）/ `Chip`（小さめ）/ `Danger`（赤系）
- ソートRadioButton: `SortRadio`（チェック時にアクセント色）
- 全リソース参照は **`DynamicResource`**（ダークモード切替のため。`StaticResource` 禁止）

### ダークモード

`MainViewModel.ToggleDarkModeCommand` → `ApplyTheme(bool dark)` が
`Application.Current.Resources` を直接書き換える。

```
Light: AppBg=#F4F5F7, CardBg=#FFFFFF, Text=#111827, AccentBrush=#4F46E5
Dark:  AppBg=#1A1B1E, CardBg=#2B2D31, Text=#F3F4F6, AccentBrush=#818CF8
```

`IsDarkMode` は `AppSettings` に永続化。

### ライブラリ一覧の各アイテム

- 表紙サムネイル（60×84px）
- タイトル + 完結バッジ + 📋コピーボタン
- サイトラベル / 著者 / 話数 / 分割数
- 元URLのクリッカブルリンク（`Hyperlink` + `Command`）
- ステータスバッジ + サイト最終更新日 + 確認日 + 変換日
- 出力ファイル名テンプレート入力 + 「出力ファイル名変更」ボタン
- 操作ボタン: 確認 / 更新 / 表紙 / 開く / 📁（フォルダ表示）/ 削除

### ソート

リスト上部に RadioButton ストリップ（`SortRadio` スタイル）:
- 変換日順（デフォルト）
- サイト更新日順
- タイトル順（`StringComparer.CurrentCultureIgnoreCase`）

---

## CLI（NovelToEink.Cli）

```
NovelToEink.Cli <URL> [out.epub] [--max N] [--yoko] [--no-images] [--gray-off]
                [--cover official|generated|none|<index>] [--list]
```

**batchモード** (`batch <folder> <url...>`):
- 各URLを順にダウンロード → EPUB生成（表紙は公式→生成→なしの優先順）
- 冪等（キャッシュ活用で差分取得）
- 失敗時: `Register-ScheduledTask` で90分後に自己再試行（`Environment.ProcessPath` で自分を再スケジュール）
- 全成功でタスク解除（`Unregister-ScheduledTask`）

---

## ハマりどころ（必読）

### 1. ResourceDictionary のキー名衝突

`Styles.xaml` で `SolidColorBrush` と `Style` に同じキー名をつけると
起動時に `XamlParseException: Item has already been added. Key: Accent` が出る。
→ ブラシキーは `AccentBrush`、スタイルキーは `Accent` と分ける。

### 2. ImageSharp バージョン

v3.1.x (ImageSharp) + v2.1.x (Drawing) = Apache-2.0 ライセンス。
v4/v3 は商用ライセンス必須でビルドが実行時エラーになる。
`<PackageReference Include="SixLabors.ImageSharp" Version="3.1.x" />` で固定。

### 3. ダークモードに StaticResource は使えない

`{StaticResource X}` はスタイル適用時に一度だけ解決される。
`Application.Current.Resources` を書き換えても反映されない。
テーマ切替が必要なすべての箇所で `{DynamicResource X}` を使う。

### 4. カクヨムの __NEXT_DATA__ 解析

`JsonDocument.Parse` 後に `parsed.RootElement.Clone()` してから `parsed.Dispose()` すること。
Dispose後に JsonElement を参照するとアクセス違反になる。
`__ref` フォーマット（`{"__ref":"Episode:xxx"}`）を再帰的に解決する。

### 5. メタデータキャッシュとチキン・エッグ問題

`DownloadCache` はコンストラクタで `workId` が必要だが、
従来の設計では `workId` はネット取得（`GetMetadataAsync`）後にしか得られない。
→ `INovelScraper.TryParseWorkId(url)` でURLをローカル解析してから
   キャッシュを初期化し、その後ネット取得した結果をキャッシュに書く。

### 6. WPF 単一exeのウィンドウキャプチャ

`PublishSingleFile=true` の exe は Start Menu 未登録のため、
computer-use ツールの `request_access` でウィンドウが掴めない場合がある。
→ `SetWindowPos(HWND_TOPMOST)` + `Graphics.CopyFromScreen` でウィンドウ矩形を直接キャプチャ。

### 7. EPUB の mimetype は無圧縮・先頭

EPUB 仕様上、`mimetype` エントリは ZIP 圧縮なし（`NoCompression`）で
アーカイブの先頭に置く必要がある。`ZipArchive` の `Open` 順序に注意。

### 8. 全角スペースのファイル名問題

なろう作品タイトルには「　」（U+3000 全角スペース）が含まれることがある。
ファイル名としては正しく表示されないため、`NameFormatter.Sanitize` で
`Path.GetInvalidFileNameChars()` を処理する前に `'　' → ' '` に置換する。

---

## アプリアイコン

`src/NovelToEink.App/app.ico`（6サイズ: 256/128/64/48/32/16px）

Design B（本→E-reader）: インディゴ（#4F46E5）背景、白い本（左）、
白矢印（中央）、アンバー（#FCD34D）E-reader（右）。

生成方法: ImageSharp + ImageSharp.Drawing で各サイズを PNG にレンダリングし、
ICO フォーマット（PNG埋め込み方式）に手書きエンコード。
csproj に `<ApplicationIcon>app.ico</ApplicationIcon>` を追加。

---

## GitHub

リポジトリ: `https://github.com/simeon052/NovelToXteink` (private)

publish（単一exe）:
```
dotnet publish src/NovelToEink.App/NovelToEink.App.csproj -c Release -r win-x64
    --self-contained true -p:PublishSingleFile=true
    -p:IncludeNativeLibrariesForSelfExtract=true
```
出力: `src/NovelToEink.App/bin/Release/net10.0-windows/win-x64/publish/NovelToEink.App.exe`（約137MB）
