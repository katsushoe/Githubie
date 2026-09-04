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

Windows Installerの`ProductVersion`はMSI仕様上3部構成（`MAJOR.MINOR.BUILD`、各255以下）までしか比較に使わないため、Display Versionの先頭3部をそのまま用います（例: Display Version `1.0.0.0` → Product Version `1.0.0`）。修正番号だけの更新でも既存版を置換できるよう、同一3部版のMajor Upgradeを許可します。

現在のDisplay Versionは`1.8.8.1`です（2026-09-04、Tag作成後のLocal ref永続化を修正）。

Version `1.8.8.1`では、`github_tag_create`がRemote Annotated Tagと同一のTag objectを同じ登録RepositoryのLocal Tag refへ永続化します。永続化失敗時は成功を返さず、同一Remote Tagへの`github_tag_push`は冪等に成功します。

Version `1.8.8.1`では、自動テスト366件の全件成功、MSI BuildとSHA-256、`C:\Githubie`へのUpgrade Install、Install済みVersion、Windows Service自動起動、設定検査、登録済み9 Projectの保持をWindows実機で検証しました。

Version `1.8.8.0`では、Moyai互換のGitHub Release作成・取得・公開・取下げライフサイクルを追加しました。Versionは`v{version}`へ写像し、取下げ時はReleaseのみ削除してTagを保持します。自動テスト361件が全件成功しました。

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

## 検証履歴

Version `1.0.0.0`では、Phase 1のコア実装、Windows実機での読み取り・書き込み疎通、Windows Service運用、当時の自動テスト114件、MSI Install／Upgrade／Uninstallを検証しました。実機検証で判明したMSI作成ディレクトリのACL不備（`auth set`が`IoError`になる問題）は修正済みです（[ADR 0013](docs/adr/0013-msi-directory-acl-grants.md)）。

Version `1.6.0.4`では、自動テスト285件の全件成功、`C:\Githubie`へのMSI Install／Upgrade、既存設定とRepository Dataの保持、Windows Service起動、MCP応答、大文字小文字を区別しない認証済みRepository検索をWindows実機で検証しました。

Version `1.6.0.5`では、明示的なLocal Tag pushの診断を原因別にし、既存Remote Tagが同一か競合かを判定して競合Tagの上書きを拒否します。また、`github_tag_create`がLocal Tagを作成せずRemote Annotated Tagを公開する操作であることを明確化しました。

Version `1.6.0.5`では、自動テスト291件の全件成功、MSI／Portable ZIPのBuildとSHA-256、`C:\Githubie`へのMSI Upgrade、Install済みファイルのVersion、既存設定とRepository Dataの保持、Windows Service起動、CLI設定検査、MCP応答をWindows実機で検証しました。

Version `1.6.0.6`では、Policyに従うRepository working tree diff／local commit操作を追加し、標準response/error contract、監査ログ、Provider Capabilityへ反映しました。自動テスト292件の全件成功、MSI BuildとSHA-256、`C:\Githubie`へのMSI Upgrade、Install済みVersion、既存設定とRepository Dataの保持、Windows Service起動、CLI設定検査、新ToolのMCP SchemaをWindows実機で検証しました。

Version `1.7.0.0`では、`list_projects`、push前の候補確認指示、未登録push時の候補提示を追加しました。自動テスト294件の全件成功、MSI BuildとSHA-256、`C:\Githubie`へのMSI Upgrade、Install済みVersion、既存設定とRepository Dataの保持、Windows Service起動、CLI設定検査、`list_projects`応答、未登録pushの`error.candidates`をWindows実機で検証しました。

Version `1.8.0.0`では、Repository登録承認後の最前面画面による任意Token設定を追加し、Token入力のスキップまたは保存失敗時もRepository登録を維持します。

Version `1.8.0.0`では、自動テスト296件の全件成功、MSI BuildとSHA-256、`C:\Githubie`へのMSI Upgrade、Install済みVersion、既存設定とRepository Dataの保持、Windows Service起動、CLI設定検査、MCP応答、Repository登録結果の`token_configured`／`token_status` SchemaをWindows実機で検証しました。

Version `1.8.1.0`では、CLIの`tools/call`を11分待機へ延長し、疎通確認とTool一覧の5秒待機を維持することで、承認画面とToken入力画面がClient Cancellationで閉じる問題を修正しました。自動テスト299件の全件成功、MSI BuildとSHA-256、`C:\Githubie`へのUpgrade、Install済みVersion、Windows Service自動起動、CLI設定検査、MCP Version応答、登録済みProjectの保持をWindows実機で検証しました。

Version `1.8.3.0`では、単独の`githubie auth set`からToken画面を開く場合も、登録済みRepository URLを解決して表示します。自動テスト301件の全件成功、MSI／Portable ZIPのBuildとSHA-256、`C:\Githubie`へのInstall、Install済みVersion、Windows Service自動起動、CLI設定検査、登録済みProjectの保持をWindows実機で検証しました。

Version `1.8.3.1`では、実PATによるHTTPS pull／tag push経路、自動テスト301件の全件成功、MSI BuildとSHA-256、`C:\Githubie`への同一3部版Upgrade、Install済みVersionとMCP Version、Windows Service自動起動、CLI設定検査、登録済み9 Projectの保持、手動Uninstall／ReinstallをWindows実機で検証しました。手動UninstallでServiceとProcessが消去され、設定・Database・Secret Directoryが保持されること、ReinstallでServiceが再作成・起動することを確認しました。

Version `1.8.3.2`では、MSIによるService消去ライフサイクルの確認結果と、WiX ServiceControlの変更が不要という結論を反映します。自動テスト301件の全件成功、MSI BuildとSHA-256、`C:\Githubie`への同一3部版Upgrade、Install済みVersionとMCP Version、Windows Service自動起動、CLI設定検査、登録済み9 Projectの保持をWindows実機で検証しました。

Version `1.8.4.0`では、GitHub Issue一覧・詳細の読み取り操作をMCPとCLIへ追加します。自動テスト305件の全件成功、MSI BuildとSHA-256、`C:\Githubie`へのUpgrade、Install済みVersionとMCP Version、Windows Service自動起動、設定検査、登録済み9 Projectの保持、Issue Tool 2件の公開をWindows実機で検証しました。

Version `1.8.5.0`では、初回Commit前のRepository状態取得と初回Local Commitに対応します。自動テスト307件の全件成功、MSI BuildとSHA-256、`C:\Githubie`へのUpgrade、Install済みVersionとMCP Version、Windows Service自動起動、設定検査、登録済み9 Projectの保持、unborn branchの状態出力をWindows実機で検証しました。
