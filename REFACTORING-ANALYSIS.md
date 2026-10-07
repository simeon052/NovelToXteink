# NovelToEink リファクタリング分析

## 概要

本ドキュメントは、NovelToEink プロジェクトのコードベースに対するリファクタリング候補をまとめたもの。優先度（High / Medium / Low）で分類し、各項目について現状の問題点と提案内容を示す。

---

## High Priority

### 1. `MainViewModel` の肥大化（[`MainViewModel.cs`](src/NovelToEink.App/MainViewModel.cs:12)）

**問題**: 1083行にわたり、UIロジック・ビジネスロジック・コマンド定義がすべて1クラスに集中している。
- 20以上のコマンドプロパティを定義
- 設定バインディングが70行以上（行69〜256）で重複パターンが多い
- エクスポート/インポート処理が長い（行944〜1081）

**提案**:
- `MainViewModel` を複数のサブビューモデルに分割する
- 設定バインディングのパターンをヘルパークラスまたはメソッドに抽出する
- エクスポート/インポート処理を `LibraryService` に移動するか、専用のサービスクラスを作成する

```
MainViewModel (UI協調)
    ├── SettingsSection (XTC設定など)
    ├── LibrarySection (作品操作)
    └── ImportExportSection (エクスポート/インポート)
```

### 2. `LibraryService` と `AppSettings` の責任過多（[`LibraryService.cs`](src/NovelToEink.Core/LibraryService.cs:11)、[`Library.cs`](src/NovelToEink.Core/Library.cs:114)）

**問題**: 
- `LibraryService` はライブラリ管理に加え、エクスポート/インポートのシリアライズロジックも担当（行258〜337）
- `AppSettings` は設定データと永続化ロジックが混在
- `ApplyImportedSettings` メソッドが手動で各プロパティをスイッチケースしている（行319〜337）

**提案**:
- エクスポート/インポートロジックを `LibraryExportService` などに分離
- `AppSettings` から `Save()`/`Load()` を削除し、`SettingsService` に移動
- `ApplyImportedSettings` にリフレクションまたはマッピングライブラリを使用する

### 3. 例外の汎用 `catch` が多すぎる

**問題**: 至るところで空の `catch {}` またはコメント付きの `catch { /* ignore */ }` が見られる。
- [`LibraryService.cs`](src/NovelToEink.Core/LibraryService.cs:201): `TryDelete`, `TryDeleteDirectory`
- [`Library.cs`](src/NovelToEink.Core/Library.cs:99): `Load()` のJSONデシリアライズ失敗
- [`Library.cs`](src/NovelToEink.Core/Library.cs:182): `AppSettings.Load()`
- [`SyosetuScraper.cs`](src/NovelToEink.Core/SyosetuScraper.cs:91): APIフェッチ失敗

**提案**:
- 特定の例外のみをキャッチする（`catch (JsonException)`, `catch (IOException)` など）
- ロギングを統一する（`ILogger<T>` パターンを導入）

---

## Medium Priority

### 4. スクレイパーのコード重複（[`SyosetuScraper.cs`](src/NovelToEink.Core/SyosetuScraper.cs:9)、[`KakuyomuScraper.cs`](src/NovelToEink.Core/KakuyomuScraper.cs:9)）

**問題**: 両スクレイパーに重複するパターンが存在する。
- `GetMetadataAsync` / `GetTableOfContentsAsync` / `GetEpisodeAsync` のシグネチャが同一
- URL解析とHTML読み込みのロジックが個別に実装されている

**提案**:
- 既に `INovelScraper` インターフェースが存在するため、共通ヘルパーを追加する
- HTMLパースの共通ユーティリティ（`FirstNode`, `TextOf` など）を静的クラスに抽出

### 5. `EpubBuilder` の文字列連結によるXML生成（[`EpubBuilder.cs`](src/NovelToEink.Core/EpubBuilder.cs:271)）

**問題**: XHTML/OPF/NAVのXML生成が `StringBuilder` と文字列補間で行われている。
- 行275-282: `XhtmlDocument()` メソッドで文字列連結
- 行297-330: `NavXhtml()` メソッドで複雑なリスト構造を文字列操作

**提案**:
- `System.Xml.Linq.XElement` を使用したXML生成に変更する（可読性向上）
- または、テンプレートエンジン（最小限の置換のみ）を採用する

### 6. 設定プロパティのボイラープレート（[`MainViewModel.cs`](src/NovelToEink.App/MainViewModel.cs:69)）

**問題**: 各設定プロパティが以下のパターンを繰り返す。
```csharp
public bool Vertical
{
    get => _settings.Vertical;
    set { if (_settings.Vertical != value) { _settings.Vertical = value; _settings.Save(); OnChanged(); } }
}
```

**提案**:
- 設定バインディング用のヘルパーメソッドを作成する：
```csharp
private bool BindSetting<T>(ref T field, T value, Action onSave, string propName)
{
    if (Equals(field, value)) return false;
    field = value;
    onSave?.Invoke();
    OnChanged(propName);
    return true;
}
```

### 7. `XtcConverter` と `XtcBuilder` の責務重複（[`XtcConverter.cs`](src/NovelToEink.XtcConverter/XtcConverter.cs:16)、[`XtcBuilder.cs`](src/NovelToEink.Xtc/)）

**問題**: 
- `XtcConverter` は抽象クラスで、実体は `X4ProXtcConverter` に委譲
- `Core/XtcConversionUtility.cs` と `XtcConverter/XtcConversionUtility.cs` の2箇所に同名ユーティリティが存在

**提案**:
- `XtcConverter` 抽象クラスを削除し、`X4ProXtcConverter` を直接使用する
- `XtcConversionUtility` を `NovelToEink.Xtc` ネームスペースに統合する

---

## Low Priority

### 8. `Names.cs` と `NameFormatter` の分離（[`Library.cs`](src/NovelToEink.Core/Library.cs:39) で `NameFormatter.DefaultTemplate` を使用）

**問題**: `NameFormatter` クラスがどこに定義されているか不明確。ファイル名のテンプレート処理が分散している可能性。

**提案**: 確認後、必要に応じて統合する。

### 9. Pythonスクリプトの整理（[`python/`](python/) ディレクトリ）

**問題**: 開発用補助ツールが含まれているが、C#コードベースとは独立している。

**提案**:
- 使用されていない場合はドキュメントのみ残して削除する
- または、CLIプロジェクト内に組み込む

### 10. `Math.Clamp` の呼び出しパターン（[`MainViewModel.cs`](src/NovelToEink.App/MainViewModel.cs:177)）

**問題**: 数箇所で `Math.Clamp(value, min, max)` が呼ばれているが、境界値のドキュメントが不十分。

**提案**: 定数として明文化する：
```csharp
private const int MinFontSize = 8;
private const int MaxFontSize = 200;
```

### 11. `ApplyTheme` のハードコードされた色（[`MainViewModel.cs`](src/NovelToEink.App/MainViewModel.cs:887)）

**問題**: ダーク/ライトモードの色がメソッド内にハードコードされている。

**提案**: `Styles.xaml` にリソースとして定義し、コードビハインドから適用する。

---

## 全体的なアーキテクチャ感想

| 項目 | 評価 | コメント |
|------|------|----------|
| レイヤー分離 | 良い | Core/Xtc/App/CLI の分離が明確 |
| インターフェース設計 | 良い | `INovelScraper` が良い抽象化を提供 |
| 依存関係 | ほぼ適切 | Core → XtcConverter の逆依存に注意 |
| テスト容易性 | 改善余地 | 静的メソッドが多く、モックが難しい箇所がある |
| エラー処理 | 改善余地 | 例外のキャッチ/ログ記録が不統一 |

---

## 推奨リファクタリング順序

1. **(High)** `MainViewModel` の分割 — UIロジックの整理が最優先
2. **(High)** 例外処理の統一 — リスク低減に直結
3. **(Medium)** 設定バインディングのパターン抽出 — ボイラープレート削減
4. **(Medium)** スクレイパー共通ヘルパーの追加 — 保守性向上
5. **(Medium)** XtcConverter の統合 — 依存関係の整理
6. **(Low)** XML生成の改善 — 可読性向上
7. **(Low)** テスト導入 — リファクタリング後の検証基盤構築
