# NovelToXteink ストア用アイコン一式

公開中パッケージ (1.0.0.0) の `Assets/*.png` は、左上に 1〜2px の点があるだけのほぼ透明な画像でした。
そのため Store 掲載ページ・スタートメニュー・タスクバーのアイコンが空白になっています。
このフォルダはその差し替え用です。デザインは既存 `app.ico`（本 → E-reader、インディゴ + アンバー）を踏襲しています。

## 1. Store 掲載ページだけ先に直す（パッケージ再提出なし）

Partner Center → アプリ → Store 登録情報 → 「Store ロゴ」に `StoreListing/` の画像をアップロードして申請を更新。

| ファイル | 用途 |
|---|---|
| `AppTileIcon_1x1_300x300.png` | 1:1 アプリ タイル アイコン（掲載ページのアイコン。これが最優先） |
| `PosterArt_2x3_720x1080.png` / `_1440x2160.png` | 2:3 ポスター アート |
| `BoxArt_1x1_1080x1080.png` / `_2160x2160.png` | 1:1 ボックス アート |
| `HeroArt_16x9_1920x1080.png` / `_3840x2160.png` | 16:9 スーパー ヒーロー アート（Store がタイトルを重ねるので文字なし） |

## 2. パッケージ本体を直す（要バージョンアップ・再提出）

インストール後のスタートメニュー／タスクバーのアイコンは MSIX 内の画像なので、新しいパッケージが必要です。

1. `feature/msix-packaging` ブランチの `src/NovelToEink.App.Packaging/Assets/` を、このフォルダの `Assets/` の中身で置き換える
2. `Package.appxmanifest` の `uap:DefaultTile` を変更（小タイルに専用画像を使う）

   ```xml
   <uap:DefaultTile
     ShortName="NovelToXteink"
     Wide310x150Logo="Assets\Wide310x150Logo.png"
     Square71x71Logo="Assets\SmallTile.png"
     Square310x310Logo="Assets\LargeTile.png" />
   ```

3. `.wapproj` の `<Content Include="Assets\...png" />` 4 行を 1 行に置き換える

   ```xml
   <Content Include="Assets\**\*.png" />
   ```

4. `Identity` の `Version` と wapproj の `PackageVersion` を `1.0.1.0` などに上げて再ビルド → Partner Center に提出

### scale / targetsize 付きファイルについて

`*.scale-200.png` や `*.targetsize-24_altform-unplated.png` は、パッケージに `resources.pri` が入っている場合だけ使われます
（公開中の 1.0.0.0 には `resources.pri` が入っていません）。

- msbuild で wapproj をビルドしていれば自動生成されます
- makeappx で手動パッケージしている場合は、先に次を実行します（`<layout>` はパッケージ化するフォルダ）

  ```
  makepri createconfig /cf priconfig.xml /dl ja-JP /pv 10.0.0
  makepri new /pr <layout> /cf priconfig.xml /of <layout>\resources.pri
  ```

`resources.pri` が無くても、修飾子なしのファイル（`Square44x44Logo.png` など）だけで正しく表示されます。
`StoreLogo.png` は Store が掲載アイコンの元にするため、従来どおり 300x300 にしています。

## 3. app-icon/（任意）

タイルと同じ絵柄で描き直した `app.ico`（16〜256px の 9 サイズ）と PNG (512 / 1024)。
ウィンドウのタイトルバーも揃えたい場合は `src/NovelToEink.App/app.ico` と差し替えてください。
