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

- 縦書きスキャン順序：**行優先**（XTG）
- 先頭バイト：`XTC\x00` (0x00435458)

## 対応デバイス（選択可能）

| 設定値 | デバイス | 解像度 |
|---|---|---|
| `X3` | Xteink X3 | 528 × 792 |
| `X4Pro` | Xteink X4 Pro | 480 × 800 |

変換時に **X3 / X4 Pro** を選択（解像度を切り替え）。

## 機能範囲

### 実装箇所
- `X4ProXtcConverter` を再実装：
  - EPUB の各章 → ページラスター化 → 1bit XTG（528×792 または 480×800）
  - バイナリ XTC 出力：56 bytes ヘッダー + 16 bytes/page インデックステーブル + XTG データエリア
- CLI 引数に `--device X3|X4Pro` を追加（既定：X4Pro）

### 出力構造
```
先頭 56bytes ヘッダー（version/pageCount/readDirection/...）
＋ インデックステーブル（16 bytes/page、offset8/size4/width2/height2）
＋ XTG データエリア（各ページ 先頭22bytes mark/width/height/colorMode/compression/dataSize/md5）
```

## 検証項目

1. 先頭バイトが `XTC\x00` である
2. pageCount が EPUB のページ数と一致する
3. 指定デバイス（X3/X4 Pro）の解像度で出力される
4. サンプル XTC と同様のバイト配置になるか hexdump で確認
5. EPUB「辺境の杖職人」で変換が完了すること（全ページ生成）

## 制約

- C# のプロジェクト慣習に従う（namespace `NovelToEink.XtcConverter`）
- パスの全角文字（U+3000、U+3001）は Python 経由で対応
- 480×800 縦書き対応（「～」→「丨」マッピング）

## 参照ドキュメント

- フォーマット仕様・調査詳細：`RESEARCH-XTC.md`
- 参考リポジトリ：bigbag/epub-to-xtc-converter（XTG/XTH 仕様書）
