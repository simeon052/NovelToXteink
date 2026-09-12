# XTC形式 調査結果 (Research Findings)

## 1. 背景と目的

Xteink 端末向けに、EPUB を XTC 形式に変換する。
旧実装（`X4ProXtcConverter.cs` / `EPUBReader.cs`）はプレースホルダーの **テキスト XTC**（`<Page><Text>`）を出力しており、これは実機で読めなかった。

正解の形式を確認するため、実機で実際に読まれている **サンプル XTC** をバイト単位で解析し、
gist 仕様書・参考実装（epub2xtc）と突き合わせて確定させた。

## 2. 実装方針（確定）

- **対応フォーマット：XTC / XTG のみ（1bit モノクロ）**
  - XTH / XTCH（2bit グレイスケール）は対象外。純正ファームは読めるが描画がもっさりする。
  - カスタムファーム（Papyrix bigbag / CrossPoint Reader）も XTC native support あり。
- **対象デバイス（選択可能）：Xteink X3 / X4 Pro**
  - **X3**: 528 × 792（3.7 インチ）
  - **X4 Pro**: 480 × 800（4.3 インチ）
  - X4 Pro のパネルは 4 階調だが、出力は 1bit XTG なので 2bit 解像度は不要。

### 解像度の裏取り

公称 PPI から逆算して一致を確認した。

| 端末 | 公称 | 解像度から逆算 |
|---|---|---|
| X3 | 3.7" / 259 PPI | √(528²+792²)/3.7 = **257 PPI** |
| X4 Pro | 4.3" / 219 PPI | √(480²+800²)/4.3 = **217 PPI** |

## 3. サンプル XTC の解析結果

- パス: `sample/ゲーム知識で最強に成ったモブ兵士は、真の実力を隠したい - 岸本 和葉.xtc`
- サイズ: 80,609,758 bytes

先頭 128 バイト:

```
00000000: 5854 4300 0100 0506 0100 0000 0000 0000  XTC.............
00000010: 0000 0000 0000 0000 3000 0000 0000 0000  ........0.......
00000020: 8060 0000 0000 0000 0000 0000 0000 0000  .`..............
00000030: 8060 0000 0000 0000 46cc 0000 1002 1803  .`......F.......
```

パース結果:

| フィールド | 値 |
|---|---|
| mark | `XTC\x00` |
| version | 1 |
| pageCount | 1541 |
| readDirection | 1 (R→L) |
| hasMetadata / hasThumbnails / hasChapters | 0 / 0 / 0 |
| currentPage | 0 |
| metadataOffset | 0 |
| indexOffset | **48** |
| dataOffset | 24704 |
| thumbOffset | 0 |

**ヘッダーは 48 バイト**である。検算：`48 + 1541 × 16 = 24704 = dataOffset = page0.offset`。
オフセット 0x30 から始まるのは `chapterOffset` ではなく、インデックステーブルの先頭エントリ。

page0 のインデックスエントリと XTG ヘッダー:

| 項目 | 値 |
|---|---|
| index: offset / size / w / h | 24704 / 52294 / 528 / 792 |
| XTG: mark / w / h / colorMode / compression / dataSize | `XTG\x00` / 528 / 792 / 0 / 0 / 52272 |
| XTG: md5 (8 bytes) | `a7c31a2d41f5db17` |

- `dataSize = ((528+7)/8) × 792 = 52272` ✓
- `index.size = 22 + dataSize = 52294` ✓
- 全 1541 ページが同一サイズ・同一解像度
- 末尾に 1024 バイトの `0xff` パディング（必須ではない）

### md5 フィールドの意味（実測で確定）

```
md5(ピクセルデータ 52272 bytes) = a7c31a2d41f5db17 7b4ef425d01340bb
ヘッダーの md5 フィールド        = a7c31a2d41f5db17
```

→ **ピクセルデータのみ**の MD5 の**先頭 8 バイト**。XTG ヘッダーは含めない。

### ビット極性（実測で確定）

page0 のピクセルデータのバイト頻度は `0xff` が最頻（9880 回）。本文ページの余白は白なので、
**ビット 1 = 白 / ビット 0 = 黒**。gist 仕様書の記載と一致。

## 4. gists 仕様書 (CrazyCoder, b125f26d) との差分

- 4形式: XTG (1bit), XTH (2bit gray), XTC (コンテナ), XTCH (コンテナvariant)
- 先頭バイト: XTC=`0x00435458`, XTG=`0x00475458`, XTH=`0x00485458`, XTCH=`0x48435458`
- ページインデックス: 16 bytes/page (offset 8bytes, size 4bytes, width 2bytes, height 2bytes) — サンプルと一致
- XTG: 1bit/pixel, 行は上→下, 1バイトに8px (MSB=左) — サンプルと一致
- XTH: 2つのビット平面、**列優先 (column-major, 右→左)** の縦スキャン — 本実装では未使用

**差分：XTC ヘッダーのサイズ。**
gist は `chapterOffset` (0x30, uint64) を含む 56 bytes としているが、
実機で読めるサンプルも epub2xtc の `struct.pack("<4sHHBBBBIQQQQ")`（= 48 bytes、オフセットは
metadata / index / data / thumb の 4 つのみ）も 48 bytes を使う。
gist 自身が「章機能は Xteink ファームウェア 3.1.0 時点で未実装」と注記している。

`indexOffset` / `dataOffset` はヘッダーに明示されるため、56 bytes 前提のリーダーでも
48 bytes のファイルは正しく読める（`hasChapters=0` なので `chapterOffset` は参照されない）。
**本実装は既知の動作するファイルに合わせて 48 bytes を採用する。**

## 5. 旧実装との不一致（解消済み）

| | 旧実装 | サンプル / 現実装 |
|---|---|---|
| フォーマット | テキスト `<Page><Text>` | バイナリ 48bytes ヘッダー + 16bytes/page + XTG データ |
| ページ | テキスト/画像ブロック | 1bit 白黒ビットマップ (XTG) |

## 6. 検証（現実装の出力）

`sample/辺境の杖職人が…-01-茨木野.epub` を変換し、生成物をパースして確認した。

| 項目 | X4 Pro | X3 |
|---|---|---|
| ページ数 | 4,508 | 3,896 |
| `mark` / `version` / `readDirection` | `XTC\x00` / 1 / 1 | 同左 |
| `indexOffset` | 48 | 48 |
| `dataOffset` == `48 + pageCount*16` | 72,176 ✓ | 62,384 ✓ |
| ページサイズ | 48,022 @480×800 | **52,294 @528×792**（サンプルと一致） |
| `size == 22 + ((w+7)/8)*h` | ✓ | ✓ |
| md5 == ピクセルデータの MD5 先頭 8 バイト | ✓ | ✓ |
| インデックスのオフセットが連続 | ✓ | ✓ |
| 最終ページ末尾 == ファイル末尾 | ✓ | ✓ |

ページを PNG に戻して目視確認：縦組みの右→左送り、ルビ（`鉋` に `かんな`）、
縦組み括弧（`﹁﹂『』`）、句読点（`︑︒`）、禁則処理が正しく動作している。

**実機での表示確認は未実施。**

## 7. 参考

- gist 仕様書：CrazyCoder/b125f26d6987c0620058249f59f1327d
- 参考実装：jonasdiemer/epub2xtc（`png2xtc.py`）
- 既存の Python 実装：`python/tategakiXTC.py`（縦組みレイアウトの元ネタ）
