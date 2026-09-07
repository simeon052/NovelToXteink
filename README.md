# NovelToEink

小説家になろう（[ncode.syosetu.com](https://syosetu.com/)）とカクヨム（[kakuyomu.jp](https://kakuyomu.jp/)）の
Web小説をダウンロードし、**Xteink X3 / X4 Pro** のような非力な E-Ink 端末向けに最適化した EPUB、
および実機で読める **XTC（1bit モノクロ）** へ変換する C# アプリ。

作品は URL でも**タイトル名**でも追加できる（タイトルの場合は検索して目次URLに解決する）。
表紙画像は、スクレイピングした画像（公式表紙・本文中の挿絵）またはタイトルから自動生成した画像から選べる。

## 構成

| プロジェクト | 種別 | 役割 |
|---|---|---|
| `src/NovelToEink.Core` | classlib (net10.0) | スクレイパー・作品検索・画像処理・EPUB生成（UI非依存） |
| `src/NovelToEink.Xtc`  | classlib (net10.0) | **XTC/XTG 書き出しと縦書き組版**（単体で NuGet 配布可） |
| `src/NovelToEink.App`  | WPF (net10.0-windows) | GUI（URL/タイトル入力→ダウンロード→EPUB/XTC生成） |
| `src/NovelToEink.Cli`  | console (net10.0) | ヘッドレス／バッチ用CLI |

## 使い方（GUI）

```
dotnet run --project src/NovelToEink.App
```

ダウンロードした作品を**ライブラリ**として管理し、更新があれば EPUB を作り直せる。

1. **保存先フォルダ**を指定（ここが「ライブラリ」。`library.json`・各EPUB・表紙画像がここに置かれる）。
2. **作品URL または タイトル**を入力（**複数可**・1行に1件）して「**＋ ライブラリに追加**」。
   - なろう: `https://ncode.syosetu.com/n2267be/`
   - カクヨム: `https://kakuyomu.jp/works/1177354054891139802`
   - タイトル: `異世界のんびり農家` のように入力すると検索して解決する（後述）
3. 「表紙を自動選択」がONなら、表紙のダイアログで止まらずそのまま生成へ進む（後述）。
4. 一覧に作者・サイト・話数・状態とともに各作品が並ぶ。各行のボタンで操作:
   - **確認** … その作品の更新チェック
   - **更新** … 再ダウンロードして EPUB を作り直す（表紙は保存済みを再利用）
   - **表紙** … 表紙を選び直す（`● N` は取得済みの候補件数）
   - **XTC** … その作品の EPUB を XTC へ変換（分割ぶんもまとめて）
   - **開く** … EPUB を既定アプリで開く / **📁** … フォルダを開く / **削除**
5. ツールバーの「**🔄 更新チェック**」で全作品をまとめてチェック、
   「**⬆ 更新をまとめて適用**」で更新がある作品を一括更新。

変換オプション（縦書き・挿絵・グレースケール・ルビ・**分割話数**・リクエスト間隔）、XTC設定（端末・フォント）、
保存先は設定として保存される（`%AppData%\NovelToEink\settings.json`）。

### タイトル名での追加

入力がURLでなければ**作品タイトル**として扱い、検索して目次URLに解決する。

- **なろう**: 公式API（`api.syosetu.com`）。まず `title=1` で作品名のみを対象に検索し、
  0件ならあらすじ・キーワードも含む検索へ広げる。
- **カクヨム**: 検索ページから作品リンクと作者名を抽出。

APIの返却順は関連度順とは限らないため多め（20件）に取得し、
**タイトル完全一致 > 部分一致 > 短さ** の順で並べ替えて先頭を採用する。採用した作品名はステータス欄に表示される。

### 表紙の自動選択（追加処理を止めない）

「**表紙を自動選択**」（既定ON）にすると、追加時に表紙ダイアログで待たされない。

1. 公式表紙、無ければタイトルから生成した文字表紙を**暫定**で入れて EPUB 生成へ進む
2. 表紙候補は**バックグラウンド**で画像検索し、`.cache/covers/` に保存
3. 集まると一覧の「表紙」ボタンが `表紙 ● 5` のように候補件数を表示
4. あとから「表紙」ボタンを押すと、**キャッシュ済みの候補が検索なしで即座に並ぶ**

OFFにすると従来どおり、作品ごとに表紙選択ダイアログが開く。

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

### EPUB → XTC 変換（CLI）

```bash
# 単体ファイル（既定は X4 Pro）
NovelToEink.Cli.exe book.epub -o out -d X3

# フォルダ内の EPUB をまとめて変換
NovelToEink.Cli.exe mybooks -o out -d X4Pro -f C:/Windows/Fonts/msmincho.ttc
```

| オプション | 説明 |
|---|---|
| `-o, --output <dir>` | XTC の出力先ディレクトリ |
| `-d, --device <id>` | 対象端末: `X3`（528×792）/ `X4Pro`（480×800、既定） |
| `-f, --font <file>` | 描画に使うフォントファイル（既定は自動選択） |
| `--fontsize <px>` | 本文の文字サイズ（8〜200、既定 36）。行送りとルビは自動で追随する |
| `--padding <t,b,l,r>` | 上下左右の余白（px）。組み込みの余白に加算（既定 3,0,0,0） |
| `--threshold <n>` | 2値化しきい値（128〜250、既定 200）。大きいほど線が太い |
| `--horizontal` | 横組みで出力する（既定は日本語縦書き） |
| `--list-fonts` | 使えるフォントの一覧を表示 |

### 縦書き一括パッチ

既存の EPUB を**再ダウンロードせず**縦書き設定に差し替える（`style.css` / `content.opf` / `nav.xhtml` のみ更新）。

```bash
NovelToEink.Cli.exe patch              # settings.json の OutputFolder を使用
NovelToEink.Cli.exe patch "C:\path\to\folder"
```

## XTC 出力（Xteink 実機フォーマット）

EPUB をページ画像に組版し、実機で読める **XTC**（1bit モノクロのページ集合）として書き出す。
GUI では「**XTCも生成**」「**📖 XTCをまとめて変換**」、各作品行の「**XTC**」ボタンから実行する。

### 対応端末

| 設定値 | 端末 | 解像度 |
|---|---|---|
| `X3` | Xteink X3（3.7"） | 528 × 792 |
| `X4Pro` | Xteink X4 Pro（4.3"） | 480 × 800 |

XTH / XTCH（2bit グレイスケール）は**対象外**。純正ファームでも読めるが描画が遅いため、1bit の XTC/XTG のみを出力する。

### フォント

本文フォントは GUI のコンボボックスから選べる。同梱の `Font/` フォルダと Windows の日本語フォントを
自動列挙し、「フォント参照」で任意のファイル（`.ttf` / `.ttc` / `.otf`）も指定できる。

未選択のときは **BIZ UDゴシック** を自動で選ぶ。1bit へ 2 値化する都合上、線が太く均一な書体ほど有利で、
明朝の細い横画は小さめの文字サイズだと 1px になって飛びやすい。24px で同じ本文を描いたときの黒画素率
（高いほど線が太い）:

| フォント | 黒画素率 |
|---|---|
| BIZ UDゴシック（既定） | 3.58% |
| MS ゴシック | 3.56% |
| 游明朝 Demibold | 3.04% |
| MS 明朝 | 2.20% |

明朝の風合いを残したい場合は **游明朝 Demibold**、さらに太くしたい場合は **HG明朝E** を選ぶとよい。

### 線の太さ（2値化しきい値）

`TextThreshold`（既定 200、範囲 128〜250）はこの値より明るい画素を白にする。大きくすると
アンチエイリアスの縁が黒側に倒れて**線が太くなる**。細いフォントで文字が薄いときに上げる。
GUI では「線の太さ」、CLI では `--threshold` で指定する。

### 日本語縦書き

- 右上を起点に上→下へ字を送り、行末で1列ぶん左へ移動（右→左）。`readDirection = 1`
- 縦組み用の字形へ差し替え（`﹁﹂『』︵︶︑︒︙` ほか）。
  フォントに該当字形が無い場合は、括弧類は90度回転、句読点はマス右上へ寄せてフォールバック
- ルビは親文字列の右側に、親文字列の中央を基準に配置
- `！？` / `！！` の連続は1マスに横並びで詰める
- **禁則処理**: 行頭禁則（句読点・閉じ括弧・小書き仮名）は行末にぶら下げ、行末禁則（開き括弧）は先に改行して次行頭へ送る

### フォーマット

実機で読めるサンプル XTC のバイト解析で確定させた仕様に従う（詳細は `RESEARCH-XTC.md`）。

```
先頭 48bytes ヘッダー
  mark(4) version(2) pageCount(2) readDirection(1)
  hasMetadata(1) hasThumbnails(1) hasChapters(1) currentPage(4)
  metadataOffset(8) indexOffset(8) dataOffset(8) thumbOffset(8)
＋ インデックステーブル（16 bytes/page: offset8 / size4 / width2 / height2）
＋ XTG データエリア（各ページ 先頭22bytes: mark/width/height/colorMode/compression/dataSize/md5）
```

- ビット極性は **1 = 白 / 0 = 黒**、行優先・MSBが左端
- `dataSize = ((width + 7) / 8) * height`
- XTG の `md5` は **ピクセルデータのみ**の MD5 の**先頭8バイト**（ヘッダーは含めない）
- gist の仕様書には `chapterOffset` を加えた 56bytes 版の記載があるが、実機で読めるファイルも
  参考実装（epub2xtc）も 48bytes を使う（章機能はファーム 3.1.0 時点で未実装）

`src/NovelToEink.Xtc` は他プロジェクトに依存しない独立したライブラリで、単体で NuGet パッケージ化できる
（`SkiaSharp` で組版、`HtmlAgilityPack` で EPUB を解析）。

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

XTC の組版には SkiaSharp（MIT）を使用。
