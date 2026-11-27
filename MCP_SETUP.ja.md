# MCPセットアップ

[English](MCP_SETUP.md) | [日本語](MCP_SETUP.ja.md)

## 2.0 ユーザーホスト

2.0の導入・配置・CLI・MCP登録・自動起動・安全条件・削除/復旧は[ユーザーホスト正本](USER_HOST.ja.md)を使用してください。バイナリは `C:\Githubie`、可変データはユーザー別です。旧Service設定・Tokenを自動取り込みせず、Repositoryと認証を再登録します。`configure codex|claude` は秘密を含まないstdio接続を登録します。

## 1.x の参照・復旧資料

以下のService配置・直接Token入力・導入手順は1.x向けです。2.0では上記正本を優先してください。変更していない型付きMCP Toolの引数・結果schemaは2.0でも使用します。過去の受入記録は、その記載版の結果です。

GithubieはStreamable HTTP TransportでMCP Serverを公開する。既定のEndpointは`http://127.0.0.1:45460/mcp`（`mcp_port` / `mcp_path`は[CONFIG.md](CONFIG.md)で変更可能）。

前提として、`Githubie.Server.exe`（または`githubie.exe start` / Windows Service）が起動していること、対象Repositoryに対して`githubie.exe auth set <repository>`でPersonal Access Tokenを登録済みであることを確認する。

## 疎通確認

MCPクライアントへ登録する前に、CLIで疎通を確認できる。

```powershell
githubie.exe mcp status
githubie.exe mcp tools
```

`mcp tools`で57個のTool定義（[COMMANDS.md](COMMANDS.md)参照）が返れば正常。

## Claude Code

Claude Code CLIから登録する場合。

```bash
claude mcp add --transport http githubie http://127.0.0.1:45460/mcp
```

または、プロジェクトの`.mcp.json`（もしくはユーザー設定）へ直接記述する。

```json
{
  "mcpServers": {
    "githubie": {
      "type": "http",
      "url": "http://127.0.0.1:45460/mcp"
    }
  }
}
```

登録後、Claude Codeのセッション内で`github_repository_status`等のToolが利用可能になっているか確認する。Loopbackのみ待ち受けるため、リモートのClaude Code環境（クラウド実行等）からは到達できない点に注意する。

## Codex

CodexのMCPクライアント設定はバージョンによって形式が変わるため、利用中のCodexが対応する正確な設定キーは`codex --help`または該当バージョンのドキュメントで確認すること。一般的には設定ファイル（例: `~/.codex/config.toml`）のMCP Server一覧へ、名前とHTTP Endpoint URLを追加する形になる。

```toml
[mcp_servers.githubie]
url = "http://127.0.0.1:45460/mcp"
```

## Originチェックについて

GithubieはMCP EndpointへのリクエストでOriginヘッダを検証する（[McpOriginValidator](src/Githubie.Server/McpOriginValidator.cs)）。Originが送信される場合は`http://127.0.0.1:<mcp_port>`または`http://localhost:<mcp_port>`と完全一致（Query/Fragmentなし）である必要がある。Originヘッダを送らないクライアント（多くのMCP Client実装を含む）はそのまま許可される。

## 複数プロジェクトでの利用

Repository AllowlistはGithubie側の設定ファイルで管理するため、MCPクライアント側は`repository`パラメータにGithubie内部のRepository ID（[CONFIG.md](CONFIG.md)の`repositories.<id>`）を指定するだけでよい。GitHub Owner/Repo/ローカルパスをクライアント側で意識する必要はない。
