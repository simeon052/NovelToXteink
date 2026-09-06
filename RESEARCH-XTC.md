# XTC形式 調査方針 (Research Direction)

## 1. 背景と目的

X4 Pro (480×800) デバイス向けに、EPUB を XTC 形式に変換する。
既存の実装（`X4ProXtcConverter.cs` / `EPUBReader.cs`）はプレースホルダーの **テキスト XTC**（`<Page><Text>`）を出力しているが、これは実機で読めない。

正解の形式を確認するため、実機で実際に読まれている **サンプル XTC** を解析し、そのフォーマットに合わせる。

## 2. 既知の事実

### 2.1 サンプル XTC（解析対象の1本）
- パス: `sample/ゲーム知識で最強に成ったモブ兵士は、真の実力を隠したい - 岸本 和葉.xtc`
- サイズ: 80,609,758 bytes
- 先頭バイト: `XTC\x00` (0x00435458)
- ヘッダー: 56 バイト
- version: 1
- pageCount: 1541
- readDirection: 1 (R→L)
- page0 XTG: 528×792px, dataSize=52272

### 2.2 gists 仕様書 (CrazyCoder, b125f26d)
- 4形式: XTG (1bit), XTH (4bit gray), XTC (コンテナ), XTCH (コンテナvariant)
- 先頭バイト: XTC=`0x00435458`, XTG=`0x00475458`, XTH=`0x00485458`, XTCH=`0x48435458`
- XTC ヘッダー: 56 bytes, version 2bytes, pageCount 2bytes, readDirection 1byte, hasMetadata/Thumbnails/Chapters 各1byte, currentPage 4bytes, metadataOffset/indexOffset/dataOffset/thumbOffset/chapterOffset 各8bytes (uint64)
- ページインデックス: 16 bytes/page (offset 8bytes, size 4bytes, width 2bytes, height 2bytes)
- XTG: 1bit/pixel, 行は上→下、1バイトに8px (MSB=左), dataSize=((width+7)/8)*height
- XTH: 2-bit plane (bit1=0x24, bit2=0x26), 各ビット平面は **列優先 (column-major, 右→左) の縦スキャン**、LUTレベルは 00=白/01=濃灰/10=薄灰/11=黒 (中間2値が入れ替わり)

### 2.3 我々の実装との不一致
| | 我々の実装 | サンプル / gists |
|---|---|---|
| フォーマット | テキスト `<Page><Text>` | バイナリ56bytesヘッダー + 16bytes/page + XTGデータ |
| ページ | テキスト/画像ブロック | 1bit白黒ビットマップ (XTG) |
| 先頭バイト | `XTC\x01\x00...` | `XTC\x00\x00\x01...` |

## 3. 調査事項 (未確定)

### 3.1 X4 Pro の表示形式
- **480×800 が XTG(1bit) か XTH(4bit gray) か** — これが最重要。X4 Pro は Xteink 製だが、サンプルは ESP32 系 e-paper (1bit) の可能性が高い。
- サンプルの 528×792 は X4 Pro ではない可能性があるので、実機または資料で解像度と bit depth を確認する。
- 縦書きスキャン順序は XTH のみ列優先、XTG は行優先。X4 Pro の形式次第で変わる。

### 3.2 サンプル XTC の完全解析
1541 ページのヘッダーとインデックステーブルを完全にパースし、以下を特定する：
- データエリアの配置 (dataOffset, thumbOffset)
- 各ページの XTG/XTH ヘッダーの差分
- chapter/section の区切り方があればそれに対応する章構造

### 3.3 EPUB ページのスキャン
- EPUB の XHTML → ページ画像をラスター化
- 対象 bit depth (1bit / 4bit) への変換
- 縦書き対応スキャン順序 (XTG: 行優先 / XTH: 列優先)

### 3.4 バイナリ XTC の出力
- 56 bytes ヘッダーの構築
- pageCount × 16 bytes のページインデックステーブル
- XTG/XTH データエリア (先頭22 bytes: mark/width/height/colorMode/compression/dataSize/md5)

## 4. 実装計画 (調査後)

1. `X4ProXtcConverter` を再実装：EPUB の各章 → ページラスター化 → 対象 bit depth の XTG/XTH へ
2. バイナリ XTC を 56 bytes ヘッダー + 16 bytes/page インデックス + データエリアで出力
3. サンプル XTC と同様の構造になるよう検証（ヘッダー・ページ数・解像度・先頭バイト）
4. X4 Pro の解像度・bit depth が確定次第、解像度・bit depth を修正

## 5. 検証方法
- 解析した XTC ヘッダー構造をコードで再現
- サンプル XTC と同様のバイト配置になるか hexdump で確認
- X4 Pro の実機で読めるか（可能なら）

## 6. 注意点
- パスの全角文字 (U+3000, U+3001) は Python 経由で対応（bash/MSYS は破綻）
- C# のプロジェクト慣習に従う（namespace `NovelToEink.XtcConverter` 等）
