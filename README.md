# NovelToEink

小説家になろう（[ncode.syosetu.com](https://syosetu.com/)）とカクヨム（[kakuyomu.jp](https://kakuyomu.jp/)）の
Web小説をダウンロードし、**Xteink X3** のような非力な E-Ink 端末向けに最適化した EPUB へ変換する C# アプリ。

表紙画像は、スクレイピングした画像（公式表紙・本文中の挿絵）またはタイトルから自動生成した画像から**選択**できる。

## 構成

| プロジェクト | 種別 | 役割 |
|---|---|---|
| `src/NovelToEink.Core` | classlib (net10.0) | スクレイパー・画像処理・EPUB生成（UI非依存） |
| `src/NovelToEink.App`  | WPF (net10.0-windows) | GUI（URL入力→情報取得→ダウンロード→表紙選択→保存） |
| `src/NovelToEink.Cli`  | console (net10.0) | ヘッドレス／バッチ用CLI |

## 使い方（GUI）

```
dotnet run --project src/NovelToEink.App
```

配布用に publish する場合:

```bash
dotnet publish src/NovelToEink.App/NovelToEink.App.csproj -c Release
```

環境によって出力先が次のいずれかになる:
- `src\NovelToEink.App\bin\Release\net10.0-windows\publish\`
- `src\NovelToEink.App\bin\Release\net10.0-windows\win-x64\publish\`

ダウンロードした作品を**ライブラリ**として管理し、更新があれば EPUB を作り直せる。

1. **保存先フォルダ**を指定（ここが「ライブラリ」。`library.json`・各EPUB・表紙画像がここに置かれる）。
2. **作品URL** を貼り付け（**複数可**・改行/スペース区切り）て「**＋ ライブラリに追加**」。
   - なろう: `https://ncode.syosetu.com/n2267be/`
   - カクヨム: `https://kakuyomu.jp/works/1177354054891139802`
3. ダウンロード後、作品ごとに**表紙を選ぶ**ダイアログ（公式表紙／挿絵／文字で自動生成／なし）。
4. 一覧に作者・サイト・話数・状態とともに各作品が並ぶ。各行のボタンで操作:
   - **確認** … その作品の更新チェック
   - **更新** … 再ダウンロードして EPUB を作り直す（表紙は保存済みを再利用）
   - **表紙** … 表紙を選び直す
   - **開く** … EPUB を既定アプリで開く / **📁** … フォルダを開く / **削除**
5. ツールバーの「**🔄 更新チェック**」で全作品をまとめてチェック、
   「**⬆ 更新をまとめて適用**」で更新がある作品を一括更新。

変換オプション（縦書き・挿絵・グレースケール・ルビ・**分割話数**・リクエスト間隔）と保存先は設定として保存される
（`%AppData%\NovelToEink\settings.json`）。

### ライブラリのエクスポート・インポート

**📤 エクスポート** ボタンでライブラリ全体と設定を JSON ファイルに保存できます。
別の PC や環境へのバックアップ、ライブラリの複製に使用します。

**📥 インポート** ボタンでエクスポートした JSON ファイルを読み込みます。
- 既存作品は更新されます。
- 新規作品は追加されます。
- インポート後、設定も反映させるか確認できます。

エクスポート JSON ファイルの構成:
```json
{
  "ExportedAt": "2025-09-12T10:30:00+09:00",
  "AppVersion": "1.0",
  "Library": [
    { "Url": "...", "Title": "...", ... },
    ...
  ],
  "Settings": {
    "Vertical": true,
    "GrayscaleImages": true,
    "IncludeInlineImages": true,
    "KeepRuby": true,
    "EnableProofreading": true,
    "EpisodesPerFile": 200,
    "RequestDelayMs": 1500,
    "IsDarkMode": false
  }
}
```

### EPUB分割

長編は **指定話数ごとに分割**できる（既定 **200話/ファイル**）。分割すると各EPUBのタイトルに `（1/5）` のような
通し番号が付く。出力ファイル名は一覧で**編集可能**（プレースホルダ `{title}` `{author}` `{part}`、
既定 `{title}-{part}-{author}`）。編集して「改名」を押すと既存ファイルもリネームされる。

### 完結・最終更新

一覧には **完結バッジ**（緑）と **サイト上の最終更新日** を表示する。
- なろう: 公式API（`api.syosetu.com`）の `end` と `general_lastup`
- カクヨム: `serialStatus` と `lastEpisodePublishedAt`

## ヘッドレス一括変換（CLI batch）

GUIなしで複数作品をまとめて取得・変換し、ライブラリ（DB）へ登録する。接続制限などで失敗した作品は
**Windowsタスクスケジューラに自動で再試行を登録**（90分後）し、全完了で自動解除する（冪等・取得済みはスキップ）。

```bash
NovelToEink.Cli.exe batch "C:\path\to\folder" <url1> <url2> ...
```

> 自己再スケジュールのため、`dotnet run` ではなく**publish済みの単一exe**で実行すること。

## 使い方（CLI）

```bash
# 目次の確認だけ
dotnet run --project src/NovelToEink.Cli -- <URL> --list

# EPUB生成（縦書き・グレースケール既定）
dotnet run --project src/NovelToEink.Cli -- <URL> out.epub

# オプション例
dotnet run --project src/NovelToEink.Cli -- <URL> out.epub \
    --max 50 --yoko --no-images --cover generated
```

| オプション | 説明 |
|---|---|
| `--max N` | 先頭N話だけ取得 |
| `--yoko` | 横書き（既定は縦書き） |
| `--no-images` | 挿絵を含めない |
| `--gray-off` | グレースケール化しない |
| `--cover official\|generated\|none\|<index>` | 表紙の選び方 |
| `--list` | メタ情報と目次の表示のみ |

### 縦書き一括パッチ

既存の EPUB を**再ダウンロードせず**縦書き設定に差し替える（`style.css` / `content.opf` / `nav.xhtml` のみ更新）。

```bash
NovelToEink.Cli.exe patch              # settings.json の OutputFolder を使用
NovelToEink.Cli.exe patch "C:\path\to\folder"
```

## Xteink X3 向け最適化

CrossPoint-JP での知見（巨大CSSのRAM展開でメモリエラー）を踏まえ、以下を既定とする。

- **最小限のCSS**（1ファイル・数行）。インラインスタイルなし。
- **1エピソード=1XHTML**（1ファイルあたりのDOMを小さく保つ）。
- 画像は **JPEG**（WebP非対応端末向け）に再エンコード、任意でグレースケール化・リサイズ。
- ルビ（`<ruby>`）は保持/除去を選択可能。
- 縦書き/横書きを選択可能。

## 実装メモ

- **なろう**: 目次HTMLを解析（新 `p-eplist`/`p-novel` と旧 `novel_sublist2`/`novel_honbun` の両対応）。
  目次は `?p=N` ページングを自動追従。R18(novel18)は `over18` クッキー対応。
- **カクヨム**: Next.js SSR の `__NEXT_DATA__`（Apollo state）から目次・章構成・公式表紙URLを復元。本文はエピソードページのHTMLから抽出。
- サーバ負荷に配慮し、リクエスト間に既定 1.5 秒のウェイトを入れる（変更可）。

## 注意

ダウンロードした小説は著作権で保護されている。**私的利用の範囲**で使用すること。
各サイトの利用規約・robots を尊重し、過度なアクセスは行わないこと。

## ライセンス上の補足

画像処理に SixLabors.ImageSharp / ImageSharp.Drawing を使用。**Apache-2.0 の最終版**
（ImageSharp 3.1.x / Drawing 2.1.x）にピン留めしている（v4/v3 は商用ライセンスが必要なため）。
