# GitHub側ブランチ保護 — 候補契約

この文書は開発中の候補を説明します。現在の稼働版やMoyai Consumerで利用可能という意味ではありません。[English](github-branch-protection.md)／[ADR0044](../adr/0044-github-branch-protection.md)。

## 対象と認可

登録済みRepositoryの**完全一致ブランチに対するGitHubの従来型保護ルール**を扱います。Githubieの`protected_branches`はGithubie経由の操作に適用される別の方針です。GitHub rulesetも別の仕組みであり、従来型ルールを解除してもrulesetの保護は残り得ます。

読取にはGitHubのAdministration read権限、設定・更新・解除にはAdministration write権限と変更ごとのDesktop対話承認が必要です。ownerセッション認証や旧署名モードの`repository.read`／専用`provider.githubie.branch_protection.write` scopeは、GitHub権限やDesktop承認の代わりにはなりません。サービスが利用可能な場合のみ`branch_protection_read`／`branch_protection_write`を表明します。

## 操作

- `github_branch_protection_get(repository, branch)`：従来型ルールと、安全に更新できる場合の正規化設定を返します。未知の保護設定などは`can_update=false`で設定本文を返しません。GitHubの404は`branch_protection_not_found_or_inaccessible`であり、未保護や権限正常の証明にはなりません。
- `github_branch_protection_update(repository, branch, edit, create_if_missing)`：明示された項目を変更します。`edit`のnullは保持です。項目は`require_pull_request`、`required_approving_reviews`（0〜6）、`allow_force_pushes`、`allow_deletions`、`enforce_admins`、`require_status_checks`、`status_check_contexts`、`strict_status_checks`。空・矛盾した変更やワイルドカードを含むbranchは拒否します。チェックを有効にする場合はcontext配列が必要です。特定GitHub Appへの既存固定が失われるcontext変更は拒否します。
- 新規作成は`create_if_missing=true`、branchが未保護との観測、レビュー・force push・削除・管理者・チェックの明示指定が必要です。PRレビュー必須時は承認数も明示します。404だけで権限の有無は断定しません。承認画面に変更前後を表示します。
- `github_branch_protection_delete(repository, branch)`：既存の従来型ルールを表示し、承認後に解除します。内容を安全に再現できないルールは変更・解除前に拒否します。

未指定の対応済みレビュー・チェック・対象者・保護設定は保持します。未知の設定、署名必須が有効な設定、必要な観測値が欠ける設定は、保護を暗黙に緩和せず拒否します。承認後に再読取して差異があれば送信しません。ただしGitHub REST PUTに外部クライアントの変更を防ぐ文書化済み原子条件はなく、再読取後の競合は残ります。

## エラーとCLI

403は`branch_protection_access_denied`としてAdministration権限とプランの両方を確認します。ステータスだけから原因を断定しません。承認拒否・入力不備・保全不能・競合は`not_executed`です。送信後の応答喪失や結果不一致は`branch_protection_result_unknown`、`outcome=unknown`、`retryable=false`、`suggested_action=check_status`とし、独立した読取で確認します。監査には操作・結果とRepository・branchのSHA-256値を記録し、要求本文や外部エラー本文は含めません。

CLIは既存の`githubie mcp call`経路で同じJSON引数・認証・承認・エラーを使います。読取例は`githubie mcp call github_branch_protection_get '{"repository":"example","branch":"main"}'`です。例は実Repositoryへの操作許可を意味しません。
