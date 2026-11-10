# PR元ブランチ限定公開の契約

[English](pr-source-publication.md) | [日本語](pr-source-publication.ja.md)

状態: 現行ソースの修正候補2.1.5.0です。未導入・未リリースで、従来のConsumer操作対応付けは合意済みですが、新しい公開元ポリシーのレビュー、Consumer実装と実相互受入は別工程です。[ADR0047](../adr/0047-pr-publication-source-allowlist.md)がADR0046の経路のみの認可判断を置き換えます。

## 操作と入力

`github_pr_publish_create`のoperationは`pull_request_publish_create`です。公開サービスを構成した場合にだけcapability `pull_request_publish_create=true`を返します。Consumerはこの操作を明示選択し、既存`github_pr_create`の意味は変更しません。

必須項目: 登録済み`repository`、明示`source_branch`と`destination_branch`、40桁hexの`expected_source_sha`、空白のみでない`title`（256文字以下、制御文字不可）、ハイフン付きD形式UUIDの`operation_id`。任意項目は`description`（65536文字以下）、`draft`（既定false）です。UUIDとSHAの英字大小は正規化しますが、branch名の大小は区別します。sourceとdestinationは異なる必要があります。ローカルbranch名だけを受け付け、revision式・tag・完全ref・任意refspecは拒否します。

`github_pr_publish_status`のoperationは`pull_request_publish_status`、入力は`repository`と`operation_id`です。capabilityは`pull_request_publish_status=true`。ref・PRの作成は行いません。既知の終状態は過去の記録を返し、不明な副作用は読み取りで照合して観測時刻を返します。

CLIは`githubie pr publish-create --file <arguments.json>`と`githubie pr publish-status <repository> <operation-id>`です。両方とも既存の現在ユーザーhostへ接続し、失敗時のdataを含む同じJSONを返します。失敗時の終了コードは非0です。専用MCPクライアントも同じhostへ転送します。設定は`<実際のインストール先>\config\githubie.json`だけを使い、別の設定ファイル指定は拒否します。

## 設定と権限

sourceは`pr_publication_source_branches`に完全一致し、保護対象でなく、source/destinationの組が`pull_request_routes`にも完全一致する必要があります。未指定・空は公開拒否です。廃止済み`local_commit_branches`は無視し、通常のローカルcommitは現行の現在branch方針を維持します。`require_clean_working_tree=true`ではdirtyを拒否し、いずれの設定でも未commit内容を公開しません。通常の直接push許可を必要条件へ置き換えず、拡張もしません。remoteのfetch先と単一の実効push URLが登録GitHub repositoryと一致することを検証し、曖昧・複数の送信先は拒否します。

旧Assertionモードでは作成に`repository.push`と`provider.githubie.pull_request.write`の両scope、statusに`repository.read`が必要です。Windows ownerモードでは認証済みowner SIDで認可し、記録をowner SID・repository識別子・ローカルrootへ結び付けます。旧Project対応付けの変更は記録へ影響せず、別ownerによる再利用を拒否します。現在の認可と実行直前の再検証を維持します。Provider対話承認ではrepository・source・destination・期待SHA・operation IDを表示し、拒否・対話不可では外部副作用前に停止します。

## 公開とPRの状態

- 承認前後とPR作成直前にsource commitと期待SHAを照合します。
- remote sourceがなければ固定SHAで新規作成だけを行います。同一SHAがあれば書き込みを省略し、異なるSHAはfast-forward可能でも拒否します。tag・mirror・他branch・submoduleの公開は除外します。
- PR作成時にdestinationの存在、現在のremote source、既存open PRを確認します。同一PRは再利用でき、SHA・repositoryが不一致なら拒否します。
- dataには`operation_id`、source/destination/SHA、`publication_state`、`pull_request_state`、PR番号・URL・head SHA、観測remote SHA/時刻、`stage`、`requires_reconciliation`を含みます。
- 公開状態は`not_attempted`、`created`、`already_present`、`failed`、`unknown`です。PR状態も同じ値を使います。`already_present`は今回作成した証明ではありません。
- 確認済みsourceと一致するopen PRがある場合だけ`ok=true`です。失敗時もdataを保持します。`error.outcome`は`not_executed`、`failed`、`unknown`で、unknownは`retryable=false`と`check_status`を返します。

## エラーと回復

型付きコードは`publication_source_not_allowed`, `publication_invalid`、`expected_head_mismatch`、`branch_not_found`、`protected_branch`、`pull_request_route_not_allowed`、`working_tree_dirty`、`provider_remote_not_found/ambiguous/mismatch`、認証・権限エラー、`approval_denied/unavailable`、`operation_conflict/in_progress/not_found`、`source_operation_unresolved`、`remote_branch_conflict`、`publication_failed/result_unknown`、`pull_request_result_unknown/head_mismatch`、記録・status・監査の利用不能エラーです。HTTP本文や秘密値を診断へ出しません。

操作記録は既存のユーザープロファイル側repository DBの`pr_publication_operations`へ保存し、各更新前に処理中状態を永続化します。同じUUID・同じ正規化入力は記録の返却・照合のみを行い、別入力は競合です。同sourceの過去結果がunknownなら新しいoperation IDも拒否します。timeout・取消・クラッシュ後は先にstatusを呼びます。PR一覧に1回見つからないだけでは再送を許可しません。同じ元入力の再送は読み取り照合だけを行い、既知結果を永続化できますが、更新を自動再開しません。後続操作には新しい明示要求と現在の許可・ref検証が必要です。PR作成失敗でも公開状態を保持し、branchを自動削除しません。

他の利用者によるGitHub refの移動を永続的にロックできません。公開とPR作成は別の副作用であり、両方の状態を確認してください。source作成の内部処理は期待値を空に固定した明示leaseを使用し、既存refは更新できません。任意のforce引数は公開しません。監査にはoperation ID・固定SHA・source/destination・段階・副作用状態・correlation IDを含み、description・token・署名は含めません。

## Upgradeと受入

既定インストール先は`C:\Githubie`ですが、選択した実際の配置先を基準にします。設定は`<実際のインストール先>\config\githubie.json`だけを読みます。操作記録は既存ユーザープロファイル側repository DBへtableを追加し、旧Serviceデータを取り込みません。ローカル候補作成で稼働hostや実機DBを変更しません。現行統合の回帰結果は候補manifestへ記録し、過去の隔離試験を現行受入の代用にしません。Consumer相互受入、ネイティブクライアント・実GitHubの受入と承認済み導入は別工程です。

## 独立公開元リストと移行

候補2.1.5.0は[ADR0047](../adr/0047-pr-publication-source-allowlist.md)の専用設定方式です。従来の`local_commit_branches`連動とは異なりますが、11/4にユーザーが独立方式を選択し、依頼元が元CRを更新済みです。実設定適用・完了承認は別工程です。通常commitが許可された`docs/other`でも、新リストにない場合は経路が許可されていても公開を拒否します。旧値は自動流用せず、通常commitや直接pushの許可を変えません。

登録済みRepositoryには`github_repository_settings_update`、または`githubie repo settings update --file <arguments.json>`（`githubiemcp`も同じ引数）を使います。JSONには`repository`と`changes`を含めます。`pr_publication_source_branches`は有効なbranch名の配列、大小を区別する完全一致です。null・重複・pattern・完全refは拒否し、空配列で無効化します。GETは新設定を含む10項目を返します。

ユーザーが選択した`codex/docs-main-v1010`を明示するレビュー例（未適用）は次です。依頼元が示したdestinationは`main`です。両配列は全置換なので、保持する既存項目も含めて適用前に確認します。

```json
{"repository":"kugutsu","changes":{"pr_publication_source_branches":["codex/docs-main-v1010"],"pull_request_routes":[{"source_branch":"codex/docs-main-v1010","destination_branch":"main"}]}}
```

これは実設定への適用依頼ではありません。既存DBへJSON seedを編集しても再取込されないため、承認済み設定操作を使います。旧版は新項目を拒否するため、更新前DB/設定を別に保全し、downgradeでは旧baselineを復元してください。拒否`publication_source_not_allowed`は副作用前・`outcome=not_executed`です。承認後/実行直前に設定が変われば`authorization_context_changed`で停止し、公開済みの場合は部分結果を保持します。
