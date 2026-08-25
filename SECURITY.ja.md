# セキュリティ

[English](SECURITY.md) | [日本語](SECURITY.ja.md)

## 信頼境界

MCP Endpointは`127.0.0.1`（Loopback）のみで待ち受ける。既定ポートは`45460`（[CONFIG.md](CONFIG.md)）。外部ネットワークへの公開は想定せず、リバースプロキシ等でLoopback以外へ転送しないこと。

```text
Claude Code / Codex（同一マシン上）
        ↓ Loopback HTTP
      Githubie
        ↓ HTTPS
     GitHub.com
```

MCP Endpointへのリクエストは`Origin`ヘッダを検証し、送信されている場合はLoopback・Port一致・Query/Fragmentなしのみ許可する（[McpOriginValidator](src/Githubie.Server/McpOriginValidator.cs)）。DNS rebinding等のブラウザ経由攻撃を軽減する。

## 最重要Security原則

1. AgentにPersonal Access Tokenを渡さない
2. Agentに任意Remote URLを指定させない
3. Agentに任意Git Argumentを指定させない
4. Agentに任意REST APIを呼ばせない
5. Repository Allowlist必須
6. main direct push禁止
7. force push禁止
8. develop → main PRを標準経路とする
9. Tagはmain HEADを標準Targetとする
10. SecretをCommand Line / Log / Remote URLへ残さない

## Personal Access Token

- 推奨: **Fine-grained PAT**（対象Repositoryを限定し、`Contents: Read and write` / `Pull requests: Read and write`を付与）。Repository Description更新には`Administration: Read and write`、Workflow起動・run取得には`Actions: Read and write`が追加で必要。Classic PAT（`ghp_...`、Repository横断の`repo`スコープ）は新規登録では使用しない。
- 保存: `githubie.json`へ平文保存しない。`githubie.exe auth set <repository>`でDPAPI（`DataProtectionScope.LocalMachine`）暗号化のうえ`data\secrets\<repository-id>.token`へ1ファイル/Repositoryで保存する。LocalMachineスコープを用いるのは、Windows Service（LocalSystem）実行時にも復号できる必要があるため。
- ディレクトリACL: `data\secrets`は継承を切ったうえで、LocalSystem / Administrators / 現在のユーザーのみにFullControlを限定する（[WindowsSecretDirectorySecurity](src/Githubie.Infrastructure/Credentials/WindowsSecretDirectorySecurity.cs)）。
- 認証Header: `Authorization: Bearer <token>`を都度生成し、AgentやAudit Logへ露出しない。使用直後にメモリをゼロクリアする。
- Git経由の認証: Git over HTTPSでは`GIT_ASKPASS`相当（`Githubie.AskPass.exe`）が実行時だけTokenをGitへ渡す。Tokenをコマンドライン引数や`git remote`URLへ含めない。

## Repository Allowlist / Local Path

- MCP Toolは常にGithubie内部のRepository ID（`repositories.<id>`のキー）を受け取り、GitHub Owner/Repo/ローカルパスをAgentへ自由指定させない。
- ローカルRepositoryの操作は設定済み`local_root`配下のみに限定する。`..`によるroot外参照、symlink/junctionによるroot外参照は拒否する（`LocalPathValidator`）。
- Git RemoteのURLはHTTPS形式かつ`github.com/<owner>/<repo>`と一致することをGit通信前に検証する（`GitHubRemoteUrlValidator`）。SSH形式は`remote_https_required`、接続先不一致は`remote_mismatch`で拒否する。

## 任意コマンド実行の排除

Githubieは以下のようなToolを一切公開しない。

```text
shell(command)
exec(command)
run_git(args)
github_request(method, url, body)
```

内部で実行するGitコマンドは固定の引数配列（`ArgumentList`、Shellを経由しない）に限定し、Agent入力を直接連結しない（`GitCommandClient`）。

## 監査ログ

Tool呼び出しごとに`client` / `tool` / `repository` / `branch` / `pull_request_number` / `tag` / `result` / `duration_ms` / `error_code`を記録する。Personal Access Token、Authorization Header、Password、ファイル本文、その他Secretは記録しない。

## 脆弱性報告

脆弱性を発見した場合は、Public Issueでの報告を避け、リポジトリ管理者（[katsushoe](https://github.com/katsushoe)）へ直接連絡すること。再現手順・影響範囲・該当バージョンを含めることを推奨する。
