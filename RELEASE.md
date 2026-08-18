# リリース

Githubieは現在Phase 1開発中で、まだバージョン付きリリースを発行していません。本書はバージョニング方針とリリース手順を定めます（Buckettieの運用を踏襲）。

## バージョニング方針

`Directory.Build.props`の`<Version>` / `<AssemblyVersion>` / `<FileVersion>` / `<InformationalVersion>`を4部構成（`MAJOR.MINOR.PATCH.REVISION`）の表示バージョン（Display Version）として一元管理します。

| 部位 | 意味 |
| --- | --- |
| MAJOR | 破壊的変更（設定スキーマ非互換、MCP Tool削除・シグネチャ非互換変更等） |
| MINOR | 後方互換の機能追加（新規MCP Tool追加、Phase 2/3機能等） |
| PATCH | 後方互換のバグ修正 |
| REVISION | ビルド・パッケージング上の再リリース（コード変更なし） |

Windows Installerの`ProductVersion`はMSI仕様上3部構成（`MAJOR.MINOR.BUILD`、各255以下）までしか比較に使わないため、Display Versionの先頭3部をそのまま用います（例: Display Version `0.1.0.0` → Product Version `0.1.0`）。

現在のDisplay Versionは`0.1.0.0`（Phase 1、未リリース）です。

## Gitタグ

リリース時は`v<Display Version>`形式のTagをmainへ作成します（例: `v0.1.0.0`）。Tag対象はmain HEADのみとします（[docs/adr/](docs/adr/)の設計判断と同じ原則）。

## リリース手順

1. `Directory.Build.props`のVersion 4項目を更新する
2. `dotnet build Githubie.slnx` / `dotnet test Githubie.slnx`が0警告・0エラー・全件成功であることを確認する
3. MSIとZIPをビルドする

   ```powershell
   pwsh scripts\Build-Msi.ps1 -DisplayVersion <version> -ProductVersion <3-part-version>
   pwsh scripts\Build-Zip.ps1 -DisplayVersion <version>
   ```

4. 生成された`.msi` / `.zip`とそれぞれの`.sha256`を確認する（[PACKAGES.md](PACKAGES.md)のパッケージ契約に一致すること）
5. mainへコミットし、`v<version>`Tagを作成する
6. GitHub Releaseを作成し、MSI・ZIP・SHA-256を添付する。リリースノートには変更点・検証結果（`dotnet test`件数、MSI/ZIPビルド結果）を記載する

## 検証項目

- 自動テスト: 全件成功（件数を記録）
- MSIリリースビルド: 0警告・0エラー
- MSI/ZIP: バージョン、SHA-256、標準ディレクトリ構成、ドキュメント同梱を確認
- 設定・DPAPI Token・監査ログ等の環境依存データが成果物に含まれていないことを確認

## Phase 1の状態

コア実装・実機検証・実データ疎通確認（読み取り・書き込み双方）・テスト・ドキュメントは完了していますが、MSI/ZIP自体のビルド実行と検証はまだ行っていません（[docs/adr/](docs/adr/)に主要設計判断を記録）。最初のリリースに向けては、上記手順の実地検証が次のステップです。
