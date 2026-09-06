# 機能仕様：NovelToXteink への XTC 出力機能追加

## 目的

NovelToXteink アプリに、EPUB → XTC（1bit モノクロ）の変換機能を追加する。
既存のテキスト XTC（`<Page><Text>`、プレースホルダー）を置き換え、実機で読めるバイナリ XTC を出力する。

詳細なフォーマット仕様・調査結果は `RESEARCH-XTC.md` 参照。

## 対応フォーマット（確定）

| 形式 | 対応 | 内容 |
|---|---|---|
| XTC / XTG | ✅ 対応 | 1bit モノクロ、XTC コンテナ + 各ページ XTG |
| XTH / XTCH | ❌ 対象外 | 2bit グレイスケール。描画がもっさり、対象外 |

- 縦書きスキャン順序：**行優先**（XTG はビットマップなので組み方向とは無関係）
- 先頭バイト：`XTC\x00` (0x00435458)
- ビット極性：**1 = 白 / 0 = 黒**

## 対応デバイス（選択可能）

| 設定値 | デバイス | 解像度 |
|---|---|---|
| `X3` | Xteink X3（3.7"） | 528 × 792 |
| `X4Pro` | Xteink X4 Pro（4.3"） | 480 × 800 |

変換時に **X3 / X4 Pro** を選択（解像度を切り替え）。

## 実装

### プロジェクト構成

XTC 生成は独立した C# クラスライブラリ **`src/NovelToEink.Xtc`**（`IsPackable=true`、NuGet 配布可）に切り出す。

| ファイル | 役割 |
|---|---|
| `XteinkDevice.cs` | 端末と解像度の対応、文字列パース |
| `XtgWriter.cs` | 22 bytes ヘッダー + 1bpp パック + md5 |
| `XtcWriter.cs` | 48 bytes ヘッダー + インデックス + データエリア（低レベル・厳密） |
| `XtcBuilder.cs` | ページ数未確定でも組めるビルダー（一時ファイルへ退避） |
| `EpubContent.cs` | EPUB → 本文ノード列（ルビ・挿絵の出現位置を保持） |
| `XtcPageRenderer.cs` | 縦組み／横組みの組版（SkiaSharp） |
| `TategakiGlyphMap.cs` | 縦組み用の字形差し替え |
| `Kinsoku.cs` | 行頭・行末の禁則判定 |
| `FontFinder.cs` | 同梱／システムフォントの列挙とロード |
| `EpubToXtc.cs` | 変換のファサード（進捗・キャンセル対応） |

依存 NuGet：`SkiaSharp` 3.119.0（+ `SkiaSharp.NativeAssets.Win32`）、`HtmlAgilityPack` 1.12.4

`X4ProXtcConverter` はこのライブラリへ委譲する薄いラッパーに再実装し、既存の呼び出し側 API は維持する。

### 日本語縦書き

- 右上を起点に上→下へ字を送り、行末で 1 列ぶん左へ移動（右→左）。`readDirection = 1`。
- 縦組み用の字形へ差し替え（`﹁﹂『』︵︶【】︑︒︙` ほか）。
  フォントに該当字形が無い場合は、括弧類は 90 度回転、句読点はマス右上へ寄せてフォールバック。
- ルビは親文字列の右側に、親文字列の中央を基準に配置。行をまたいだら基準を取り直す。
- `！？`／`！！` の連続は 1 マスに横並びで詰める。
- 禁則処理：行頭禁則（句読点・閉じ括弧・小書き仮名など）は行末にぶら下げ、
  行末禁則（開き括弧）は先に改行して次行頭へ送る。

### 出力構造

```
先頭 48bytes ヘッダー
  mark(4) version(2) pageCount(2) readDirection(1)
  hasMetadata(1) hasThumbnails(1) hasChapters(1) currentPage(4)
  metadataOffset(8) indexOffset(8) dataOffset(8) thumbOffset(8)
＋ インデックステーブル（16 bytes/page: offset8 / size4 / width2 / height2）
＋ XTG データエリア（各ページ 先頭 22bytes: mark/width/height/colorMode/compression/dataSize/md5）
```

> gist 仕様書には末尾に `chapterOffset` を加えた 56 bytes 版の記載があるが、
> 実機で読めるサンプル XTC も epub2xtc も 48 bytes を使う（章機能はファーム 3.1.0 時点で未実装）。
> 本実装は 48 bytes に合わせる。

### UI（NovelToEink.App）

- 「XTCも生成」チェックボックス
- 端末選択コンボボックス（X3 / X4 Pro）＋ 解像度表示
- フォント選択コンボボックス（同梱 Font フォルダ + Windows の日本語フォントを自動列挙）＋「フォント参照」で任意ファイル指定
- 「📖 XTCをまとめて変換」ボタン（ライブラリ一括）
- 各作品カードの「XTC」ボタン（1 作品ぶん、分割 EPUB も一括）
- 設定は `%AppData%\NovelToEink\settings.json` に永続化

### CLI（NovelToEink.Cli）

```
NovelToEink.Cli <path> [-o <outputDir>] [-d X3|X4Pro] [-f <fontFile>] [--horizontal]
NovelToEink.Cli --list-fonts
```

## 検証結果

| 項目 | 結果 |
|---|---|
| 先頭バイトが `XTC\x00` | ✅ |
| `version=1` / `readDirection=1` / `indexOffset=48` | ✅ |
| `dataOffset == 48 + pageCount*16` | ✅ |
| 各ページ `size == 22 + ((w+7)/8)*h` | ✅ |
| XTG の md5 = ピクセルデータの MD5 先頭 8 バイト | ✅ |
| インデックスのオフセットが連続し、最終ページ末尾 == ファイル末尾 | ✅ |
| X3 出力のページサイズ 52,294 bytes @528×792 がサンプル XTC と一致 | ✅ |
| EPUB「辺境の杖職人」で全ページ生成 | ✅（X4 Pro 4,508 ページ / X3 3,896 ページ） |
| 実機での表示確認 | ⏳ 未実施 |

## 制約

- C# のプロジェクト慣習に従う（namespace `NovelToEink.Xtc` / `NovelToEink.XtcConverter`）
- 圧縮（`compression`）とメタデータ・サムネイル・章はファーム側が未対応のため出力しない

## 参照ドキュメント

- フォーマット仕様・調査詳細：`RESEARCH-XTC.md`
- gist 仕様書：CrazyCoder/b125f26d6987c0620058249f59f1327d
- 参考実装：jonasdiemer/epub2xtc（`png2xtc.py`）、`python/tategakiXTC.py`
