# PR読取・マージのProvider契約

候補版2.1.1.0。[English正本](pr-merge-provider.md)／[ADR0043](../adr/0043-pull-request-merge-provider.md)。実装候補、実機導入、Moyai実相互受入は別工程です。

登録済みRepositoryの能力応答で`data.pr_get=true`と`data.pr_merge=true`を厳密に確認します。PR作成能力やTool存在だけから推定しません。所有ユーザーモードは従来schema1/DPAPIと毎HTTPの`X-Githubie-Session`・`X-Githubie-Generation`を保持し、Authorizationを省略します。Issuer・ES256・Project署名grantは不要です。58操作の認証対象は不変、通常能力は2.1.0.0の20項目から22項目へ増えます。

旧署名モードではget=`repository.read`、merge専用=`provider.githubie.pull_request.merge`です。旧共用`provider.githubie.pull_request.write`はマージの代わりになりません。通常の設定・権限を自動変更するものではありません。

`github_pr_get`は既存PR情報にGitHubの`head.sha`から得た`source_commit_sha`とnullableの`draft`を追加します。要求値やテストmerge SHAで補いません。PR番号は正のInt32（最大2147483647）です。

`github_pr_merge`は既存引数に`expected_source_commit_sha`を追加します。schema上は旧呼出しとの互換性のため省略可能ですが、所有ユーザー/Moyai接続では40桁SHA必須です。旧standaloneの省略呼出しは保持し、条件付き保証にはSHA指定が必要です。Consumerは承認対象SHAと事前の認証付きGET結果を照合します。Providerも実sourceと比較し、GitHub PUTの`sha`へ転送して上流ゲートでも照合します。destinationのhead固定は保証しません。

方式は`merge`/`squash`/`rebase`、共通`repository_default`はnullへ対応し、Providerで登録Repositoryの`merge_method`へ解決します。`fast_forward`を別方式へ変換しません。messageの省略/nullは既定、空白のみは拒否、非空は改変せず`commit_message`本文として転送します。

Repository経路・PR状態・Draft・SHAを変更前に確認します。所有ユーザー接続で必要なDraft/source情報が得られない場合も拒否します。GitHubの保護・レビュー・チェック・方式許可を保持し、`mergeable=true`だけで充足としません。既マージ時は実source/merge SHAを持つ観測結果を返し、新規PUTしません。過去の実行者を推定する機能ではありません。

通常成功はPUT確認と後続GETのmerged状態・merge SHA一致が必要です。送出後の通信/本文欠落・キャンセル・後続GET失敗・状態/SHA不整合は`pr_merge_result_unknown`、`RESULT_UNKNOWN`、`outcome=unknown`、`retryable=false`、`suggested_action=check_status`です。別の認証付きGETで照合し、自動再マージしません。確定したGitHub拒否まで一律unknownにはしません。

主な変更前エラーは`expected_source_commit_required`、`expected_source_commit_invalid`、`expected_source_commit_mismatch`、`pull_request_draft`、`merge_strategy_invalid`、`merge_message_invalid`です。既存の経路・状態・保護・認証エラーも保持します。通常のツール結果とHTTP認証拒否を区別し、裸403や任意のJSON-RPCエラーだけで未実行と判断しません。

CLIは`githubie mcp call github_pr_merge <JSON>`または`--file <path>`で同じToolへ委譲します。新しいSHA引数を改変せず転送し、Providerの`ok=false`やunknownは失敗終了にします。状態照合は`github_pr_get`を使います。完全な入力例・エラー対応表・受入条件はEnglish正本を参照してください。

自動テスト・ローカル成果物だけでは実Consumer受入やWindows移行の証明になりません。実PR試験は専用対象・候補hash・承認・後片付けの合意後に実施します。本番PR14・通常設定/鍵/DB/Service・公開Releaseは今回操作しません。先行2.1.0.0候補とそのsource証跡を保全します。
