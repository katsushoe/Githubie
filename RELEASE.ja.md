# リリース

[English](RELEASE.md) | [日本語](RELEASE.ja.md)

本書はバージョニング方針とリリース手順を定めます（Buckettieの運用を踏襲）。

## バージョニング方針

`Directory.Build.props`の`<Version>` / `<AssemblyVersion>` / `<FileVersion>` / `<InformationalVersion>`を4部構成`a.b.c.d`（各パート0以上の整数）の表示バージョン（Display Version）として一元管理します（AI_prompt共通バージョニングポリシーに準拠）。

| パート | 意味 |
| --- | --- |
| a（大機能） | 大機能を追加した場合に増加。大機能＝Phase単位で完成させる機能群（例: Phase 1コア実装一式） |
| b（中機能） | 中機能を追加した場合に増加。大機能を構成するサブ機能（例: Phase 2の個別MCP Tool追加） |
| c（小機能） | 小機能を追加した場合に増加。中機能を構成するサブ機能 |
| d（修正・軽微） | バグ修正・軽微な機能追加で増加 |

上位パートが増えたら下位パートは0にリセットします。カテゴリの判断がつかない場合は、増加させる前にユーザーへ確認します。

Windows Installerの`ProductVersion`はMSI仕様上3部構成（`MAJOR.MINOR.BUILD`、各255以下）までしか比較に使わないため、Display Versionの先頭3部をそのまま用います（例: Display Version `1.0.0.0` → Product Version `1.0.0`）。

現在のDisplay Versionは`1.3.5.3`です（2026-08-21、Buckettieとの承認画面UI統一に伴うリビジョン更新）。

## Gitタグ

リリース時は`v<Display Version>`形式のTagをmainへ作成します（例: `v1.0.0.0`）。Tag対象はmain HEADのみとします（[docs/adr/](docs/adr/)の設計判断と同じ原則）。

## リリース手順

1. `Directory.Build.props`のVersion 4項目を更新する（カテゴリ判断が不明な場合はユーザーに確認する）
2. `dotnet build Githubie.slnx` / `dotnet test Githubie.slnx`が0警告・0エラー・全件成功であることを確認する
3. MSIとZIPをビルドする

   ```powershell
   pwsh scripts\Build-Msi.ps1 -DisplayVersion <version> -ProductVersion <3-part-version>
   pwsh scripts\Build-Zip.ps1 -DisplayVersion <version>
   ```

4. 生成された`.msi` / `.zip`とそれぞれの`.sha256`を確認する（[PACKAGES.md](PACKAGES.md)のパッケージ契約に一致すること）
5. MSIの実インストール・アップグレード・アンインストールを実機または管理者権限で検証する
6. mainへコミットし、`v<version>`Tagを作成する
7. GitHub Releaseを作成し、MSI・ZIP・SHA-256を添付する。リリースノートには変更点・検証結果（`dotnet test`件数、MSI/ZIPビルド結果）を記載する

## 検証項目

- 自動テスト: 全件成功（件数を記録）
- MSIリリースビルド: 0警告・0エラー
- MSI/ZIP: バージョン、SHA-256、標準ディレクトリ構成、ドキュメント同梱を確認
- MSI Install／Upgrade／Uninstallの実機または管理者権限での検証
- 設定・DPAPI Token・監査ログ等の環境依存データが成果物に含まれていないことを確認

## Phase 1の状態

コア実装・実機検証・実データ疎通確認（読み取り・書き込み双方、Windows Service運用含む）・テスト（114件）・ドキュメント・MSI Install/Upgrade/Uninstallの実機検証まで完了しています（[docs/adr/](docs/adr/)に主要設計判断を記録）。実機検証でMSI作成ディレクトリのACL不備（`auth set`が`IoError`になる問題）を発見・修正済みです（[0013](docs/adr/0013-msi-directory-acl-grants.md)）。
