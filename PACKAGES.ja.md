# パッケージ構成

[English](PACKAGES.md) | [日本語](PACKAGES.ja.md)

## 2.0 ユーザーホスト

2.0の導入・配置・CLI・MCP登録・自動起動・安全条件・削除/復旧は[ユーザーホスト正本](USER_HOST.ja.md)を使用してください。バイナリは `C:\Githubie`、可変データはユーザー別です。旧Service設定・Tokenを自動取り込みせず、Repositoryと認証を再登録します。`configure codex|claude` は秘密を含まないstdio接続を登録します。

## 1.x の参照・復旧資料

以下のService配置・直接Token入力・導入手順は1.x向けです。2.0では上記正本を優先してください。変更していない型付きMCP Toolの引数・結果schemaは2.0でも使用します。過去の受入記録は、その記載版の結果です。

標準のWindows成果物は`Githubie-<version>-win-x64.msi`とそのSHA-256ファイルです。手動導入・ポータブル用途にはSelf-contained ZIPも配布できます。両方とも`bin` / `config` / `logs` / `data`の構成で統一します。

| パッケージ内パス | 内容 |
| :--- | :--- |
| `bin/githubie.exe` | 管理CLI |
| `bin/Githubie.Server.exe` | MCP Windows Service |
| `bin/Githubie.AskPass.exe` | Git認証Helperプロセス |
| `bin/*.dll`, `*.deps.json`, `*.runtimeconfig.json` | ランタイム依存関係 |
| `config/githubie.example.json` | Secretを含まない設定テンプレート |
| `docs/*.md` | README、設定、コマンド、運用、トラブルシューティング、セキュリティ各文書 |

パッケージには環境依存データを含めません。

- `config/githubie.json`
- `data/githubie.db`（Repository登録とPolicy。実行時に作成）
- `data/`およびDPAPI Tokenファイル
- `logs/`および監査ログ
- `.local/`、テスト結果、開発機固有データ
- シンボル・中間ビルド成果物（別途必要な場合を除く）

リリースはSelf-contained Windows x64パッケージです。導入後はSHA-256を記録したうえで`githubie.exe version`、`config check`、`doctor`を実行します。

MSIは標準インストール先`C:\Githubie`へのインストール、ディレクトリ作成、Windows Service登録、Major Upgrade、アンインストールを管理します。有効な設定・アプリケーションデータ・Token・監査ログはパッケージにもアンインストール時の削除対象にも含みません。

## ビルド

```powershell
pwsh scripts\Build-Msi.ps1 -DisplayVersion <表示バージョン> -ProductVersion <3部構成の製品バージョン>
pwsh scripts\Build-Zip.ps1 -DisplayVersion <表示バージョン>
```

出力先は`.local\installer\output`（MSI）、`.local\release\output`（ZIP）です。バージョン管理方針は[RELEASE.md](RELEASE.md)を参照してください。
