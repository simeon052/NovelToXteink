# NovelToXteink v1.0.0

## 概要
- 初期リリース。Web小説（ncode.syosetu.com、kakuyomu.jp）をダウンロードし、E‑Ink端末向けに最適化した EPUB と XTC（1bit モノクロ）へ変換する C# アプリです。

## 主な機能
| 機能 | 内容 |
|---|---|
| 作品追加 | URL もしくはタイトル名で追加。タイトルは検索して目次 URL を自動解決。 |
| 表紙選択 | 公式表紙・本文中の挿絵・自動生成画像から選択。自動選択モードで待ち時間を排除。 |
| ライブラリ管理 | 作品を「ライブラリ」に登録し、更新チェック・再ダウンロード・EPUB/XTC 生成を行う。 |
| GUI | WPF ベースで URL/タイトル入力 → ダウンロード → EPUB/XTC 生成までをワンクリックで実行。 |
| CLI | バッチで複数作品を取得・変換し、ライブラリへ登録。 `dotnet run --project src/NovelToEink.Cli` で実行。 |
| XTC 変換 | Xteink X3 / X4 Pro 向け 1bit XTC を生成。フォント、文字サイズ、余白、しきい値などを CLI で指定可能。 |
| EPUB 分割 | 長編は指定話数ごとに分割し、ファイル名に話数番号を付与。 |
| エクスポート/インポート | ライブラリ全体と設定を JSON でエクスポートし、別環境へインポート可能。 |

## ビルド・配布
```bash
# GUI アプリをビルド & publish
dotnet publish src/NovelToEink.App/NovelToEink.App.csproj -c Release
# 生成物は publish ディレクトリに出力されます。
```

## 使い方
1. **GUI** で `dotnet run --project src/NovelToEink.App` を実行。
2. 作品 URL またはタイトルを入力し「+ ライブラリに追加」。
3. 表紙自動選択をオンにすると、待ち時間なく生成へ進む。
4. 「XTC」ボタンで EPUB → XTC 変換。 |

## CLI 例
```bash
# 1 つの作品を EPUB へ変換
NovelToEink.Cli.exe https://ncode.syosetu.com/n2267be/ out.epub

# XTC 変換（X4 Pro）
NovelToEink.Cli.exe out.epub -o out -d X4Pro
```

## 備考
- 作品は著作権で保護されています。私的利用範囲内でご利用ください。
- 本リリースは `net10.0` をターゲットにしています。
- 詳細は README.md を参照してください。