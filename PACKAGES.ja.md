# パッケージ構成

[English](PACKAGES.md) | [日本語](PACKAGES.ja.md)

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
- `data/`およびDPAPI Tokenファイル
- `logs/`および監査ログ
- `.local/`、テスト結果、開発機固有データ
- シンボル・中間ビルド成果物（別途必要な場合を除く）

リリースはSelf-contained Windows x64パッケージです。導入後はSHA-256を記録したうえで`githubie.exe version`、`config check`、`doctor`を実行します。

MSIは`%ProgramFiles%\Githubie`（または`INSTALLROOT`）配下へのインストール、ディレクトリ作成、Windows Service登録、Major Upgrade、アンインストールを管理します。有効な設定・アプリケーションデータ・Token・監査ログはパッケージにもアンインストール時の削除対象にも含みません。

## ビルド

```powershell
pwsh scripts\Build-Msi.ps1 -DisplayVersion 1.0.0.0 -ProductVersion 1.0.0
pwsh scripts\Build-Zip.ps1 -DisplayVersion 1.0.0.0
```

出力先は`.local\installer\output`（MSI）、`.local\release\output`（ZIP）です。バージョン管理方針は[RELEASE.md](RELEASE.md)を参照してください。
