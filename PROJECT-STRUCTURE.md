# NovelToEink プロジェクト構成

**NovelToEink**は、日本のWeb小説サイト（小説家になろう・カクヨム）から小説をダウンロードし、E-Ink電子書籍リーダー（Xteink X3/X4 Proなど）で読める形式に変換するC#アプリケーションです。

---

## 解決ファイル構成

[`NovelToEink.slnx`](NovelToEink.slnx:1) がソリューションファイルで、`src/` ディレクトリ下に5つのプロジェクトが含まれています。

---

## プロジェクト構成

| プロジェクト | 種類 | 役割 |
|---|---|---|
| [`src/NovelToEink.Core`](src/NovelToEink.Core/) | クラスライブラリ (`net10.0`) | **中核ロジック**。スクレイパー、作品検索、画像処理、EPUB生成、ライブラリ管理。UIに依存しない。 |
| [`src/NovelToEink.Xtc`](src/NovelToEink.Xtc/) | クラスライブラリ (`net10.0`) | **XTC/XTG書き出しと縦書き組版**。SkiaSharpを使用。単体でNuGet配布可能な独立モジュール。 |
| [`src/NovelToEink.App`](src/NovelToEink.App/) | WPFアプリ (`net10.0-windows`) | **GUIフロントエンド**。URL/タイトル入力→ダウンロード→EPUB/XTC生成UI。 |
| [`src/NovelToEink.Cli`](src/NovelToEink.Cli/) | コンソールアプリ (`net10.0`) | **コマンドラインインターフェース**。ヘッドレス/バッチ処理用。 |
| [`src/NovelToEink.XtcConverter`](src/NovelToEink.XtcConverter/) | コンソールアプリ (`net10.0`) | **EPUB→XTC変換ツール**。CLI版の変換ユーティリティ。 |

---

## 依存関係

```
NovelToEink.App (WPF GUI)
    ├── NovelToEink.Core
    └── NovelToEink.Xtc

NovelToEink.Core
    ├── HtmlAgilityPack (HTMLスクレイピング)
    ├── SixLabors.ImageSharp (画像処理)
    └── SixLabors.ImageSharp.Drawing (描画)
    └── NovelToEink.XtcConverter (変換ロジック)

NovelToEink.Xtc
    ├── HtmlAgilityPack
    └── SkiaSharp (縦書きレンダリング)

NovelToEink.XtcConverter
    ├── NovelToEink.Xtc
    ├── HtmlAgilityPack
    └── SixLabors.ImageSharp
```

---

## 主な機能モジュール（NovelToEink.Core内）

| ファイル | 役割 |
|---|---|
| [`SyosetuScraper.cs`](src/NovelToEink.Core/SyosetuScraper.cs) | 小説家になろう用スクレイパー |
| [`KakuyomuScraper.cs`](src/NovelToEink.Core/KakuyomuScraper.cs) | カクヨム用スクレイパー |
| [`NovelSearchService.cs`](src/NovelToEink.Core/NovelSearchService.cs) | タイトルからの作品検索サービス |
| [`NovelDownloadService.cs`](src/NovelToEink.Core/NovelDownloadService.cs) | 小説ダウンロードサービス |
| [`ImageProcessor.cs`](src/NovelToEink.Core/ImageProcessor.cs) | 画像処理・表紙生成 |
| [`ImageSearchService.cs`](src/NovelToEink.Core/ImageSearchService.cs) | 表紙画像検索サービス |
| [`EpubBuilder.cs`](src/NovelToEink.Core/EpubBuilder.cs) | EPUBファイル生成 |
| [`LibraryService.cs`](src/NovelToEink.Core/LibraryService.cs) | ライブラリ管理 |
| [`ProofreadingService.cs`](src/NovelToEink.Core/ProofreadingService.cs) | 校正サービス |
| [`XtcConversionUtility.cs`](src/NovelToEink.Core/XtcConversionUtility.cs) | XTC変換ユーティリティ |

---

## Xtcモジュールの主要コンポーネント（NovelToEink.Xtc内）

| ファイル | 役割 |
|---|---|
| [`XtcWriter.cs`](src/NovelToEink.Xtc/XtcWriter.cs) | XTCファイル書き出し |
| [`XtgWriter.cs`](src/NovelToEink.Xtc/XtgWriter.cs) | XTGファイル書き出し |
| [`XtcBuilder.cs`](src/NovelToEink.Xtc/XtcBuilder.cs) | XTCビルダー |
| [`XtcPageRenderer.cs`](src/NovelToEink.Xtc/XtcPageRenderer.cs) | ページレンダリング |
| [`PageCanvas.cs`](src/NovelToEink.Xtc/PageCanvas.cs) | 組版キャンバス |
| [`Kinsoku.cs`](src/NovelToEink.Xtc/Kinsoku.cs) | 禁則処理（行頭禁則・行末禁則） |
| [`TategakiGlyphMap.cs`](src/NovelToEink.Xtc/TategakiGlyphMap.cs) | 縦書き文字マップ |
| [`ImageDithering.cs`](src/NovelToEink.Xtc/ImageDithering.cs) | ディザリング（1bitモノクロ化） |
| [`FontFinder.cs`](src/NovelToEink.Xtc/FontFinder.cs) | フォント探索 |

---

## Pythonサブディレクトリ（`python/`）

開発用の補助ツールが含まれています：

| ファイル | 役割 |
|---|---|
| [`Komawari.py`](python/Komawari.py) | 分冊処理スクリプト |
| [`tategakiXTC.py`](python/tategakiXTC.py) | 縦書きXTC処理スクリプト |
| [`localXTCviewer.html`](python/localXTCviewer.html) | ローカルXTCビューア |

---

## リリース構成（`release/`）

ビルド済みバイナリと依存DLLが配置されています：
- [`NovelToEink.App.exe`](release/NovelToEink.App.exe) - GUIアプリケーション
- [`NovelToEink.XtcConverter.exe`](release/NovelToEink.XtcConverter.exe) - 変換ツール
- 依存DLL（SixLabors.*, SkiaSharp.*など）
- `artifacts/win-x64/` - パブリッシュ済み完全ビルド

---

## まとめ

このプロジェクトは、**C#/.NET 10.0** で構築された、Web小説をE-Ink端末向けに変換するアプリケーションです。コアロジック（スクレイピング・画像処理・EPUB生成）と、XTC形式変換エンジンが分離されており、GUI（WPF）、CLI、スタンドアロン変換ツールの3つのインターフェースから利用可能です。縦書き組版にはSkiaSharpを、画像処理にはSixLabors.ImageSharpを使用しています。
