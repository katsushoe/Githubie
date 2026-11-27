# ユーザーホスト（2.0）

[English](USER_HOST.md) | [日本語](USER_HOST.ja.md)

`<install-root>` はユーザーが選択した実際のインストール先です（既定は `C:\Githubie`）。設定は実行ファイルの `bin` の親にある `config\githubie.json` 一つだけを使用します。

Githubie 2.0は、ログオンユーザーの1つのプロセスでHTTPホストとトレイを実行します。バイナリは `<install-root>\bin`、本体設定・自動起動・連携モードは `<install-root>\config\githubie.json` だけに保存し、Repository DB・資格・ログはユーザー別プロフィールに配置します。旧Serviceへの復旧には、保全した旧データと対応する1.x MSIを使用します。

## インストールと更新

対象ユーザーの**非昇格**PowerShellから、配布物のコーディネーターを実行します。

```powershell
.\Install-UserHost.ps1 -MsiPath .\Githubie-2.1.0.0-win-x64.msi -InstallRoot "D:\Apps With Spaces\Githubie"
```

MSI実行だけがUAC昇格します。別の管理者がUACを承認しても、ユーザー設定は元のSID・Windowsセッションで続行します。正本JSONの自動起動設定（新規正本では既定ON）をHKCUへ反映して読戻しを確認し、更新・修復では正本JSONの user_host.autostart_enabled=false を維持します。UAC取消、MSI失敗、導入版不一致、ユーザー変更、読戻し失敗は導入未完了です。MSIによるバイナリ配置だけをユーザー設定の完了とは扱いません。新規MSIはコーディネーターのユーザー設定未完了プロパティを必須にします。

コーディネーターはCLIの初回起動待機でreadyを確認できなかった場合、最大60秒の追加待機で現在ユーザーのホスト制御応答を確認します。追加待機でも確認できなければ導入工程は未完了です。待機終了だけでホスト停止とは判断せず、再実行前に `githubie host status` とログを確認してください。

管理配布では、管理者が `GITHUBIE_USER_SETUP_PENDING=1 GITHUBIE_TARGET_SID=<対象SID>` でMSIを配置し、同じ配布工程内で対象ユーザーの非昇格セッションから `Install-UserHost.ps1 -UserSetupOnly -Quiet` を実行します。Session 0/Systemから別ユーザーのHKCUを登録しません。ユーザー工程の読戻しが成功するまで配布完了にしないでください。`-Quiet` でも自動起動を登録・確認しますが、現在のホストは起動しません。

更新時は旧 `Githubie` Windows Serviceを削除し、旧 `<install-root>\config`・`data`・暗号化Token・ログを保全します。**正本JSONは共有しますが、旧共有DB・Tokenは新規ユーザープロフィールへ自動取り込みしません。** Repositoryは再登録し、Tokenは専用画面で再入力してください。共有DBやLocalMachine形式のDPAPIデータをユーザープロフィールへコピーしないでください。

`-InstallRoot` は導入先を指定します。省略時はMSIが記憶した導入先を維持し、新規導入の既定は `C:\Githubie` です。導入先を記録しない旧版を独自の場所に導入していた場合は、その場所を明示してください。MSIは `INSTALLROOT` を受け付け、実際の配置をHKLMへ記録します。ユーザー設定工程は64bitレジストリの記録先からCLIを起動し、指定先と異なる場合は中断します。`-UserSetupOnly` でも同じ記録を使用します。

## プロフィールと操作

保存先はWindowsのLocalApplicationData既知フォルダー配下 `Akatsukisoft\Githubie`（通常 `%LOCALAPPDATA%\Akatsukisoft\Githubie`）です。

| 対象 | 配置・動作 |
| --- | --- |
| 識別情報 | `profile.json`：schema・プロフィールUUID・所有者SID |
| ホスト設定 | `<install-root>\config\githubie.json`：`user_host.moyai_integration`・`user_host.direct_unrestricted` |
| HTTP設定 | 同じ正本JSONの `mcp_port`・`mcp_path`・Provider信頼設定 |
| 自動起動の選択 | 同じ正本JSONの `user_host.autostart_enabled`（既定true）。現在ユーザーのHKCUへ反映 |
| Repositoryと保護Token | `data\githubie.db`：登録時に同じSQLiteトランザクションで確定 |
| 接続情報 | `data\connection.json`：schema 1メタデータと保護済み資格。起動ごとに更新 |
| ログ・起動状態 | `logs`・`data\host-state.json` |

すべてのプロフィールで正本JSONのloopbackポートを使用します。その後のbind競合では起動を拒否し、別プロセスのlistenerへ接続しません。1プロフィールのホストは1つのWindowsセッションだけで実行できます。別セッションでは、所有ホストの停止・ログオフまで待ちます。接続情報のSID・プロフィール・session・PID・プロセス開始時刻を照合します。

```powershell
githubie profile status
githubie host start
githubie host status
githubie host endpoint
githubie autostart off
githubie autostart on
githubie host restart
githubie host stop
githubie logs
```

自動起動の変更は次回ログオンに反映し、現在のホストの停止・再起動とは独立して保存します。停止・再起動で選択は変わりません。古い自動起動エントリが残った場合も、業務サービス開始前に保存済み選択へ整合させます。トレイでも自動起動切替・再起動ができます。ログオフではホストと所有する入力画面をキャンセルし、期限付きで停止します。

### トレイのServerメニュー

Windowsの通知領域にある白いHのアイコンを右クリックし、「Server」を開きます。「状態」には、HTTPサーバーの起動が完了すると緑、OFF時はダークグレーのLEDを表示します。「起動」は起動中・起動済みの場合に、「終了」は終了済みの場合にグレーアウトします。終了処理中は両方をグレーアウトします。

「Server → 終了」はHTTPサーバーを停止して接続情報を無効にし、トレイは残します。「Server → 起動」で再び起動できます。サーバー停止中はHTTPクライアントとCLIのホスト操作を利用できないため、別のホストを起動せず、このトレイの「起動」または「Githubieを終了」を使います。「Githubieを終了」はアプリ全体を閉じます。サーバー起動中の従来のCLI停止・再起動は、引き続きプロセスを終了します。[ADR 0051](docs/adr/0051-tray-server-lifecycle.md)を参照してください。

## Codex・Claude Codeの登録

stdio中継が、現在ユーザーの保護済み接続情報からHTTP資格を内部設定します。PATや接続資格をクライアント設定・標準出力へ書きません。

```powershell
githubie configure codex
githubie configure claude
# 旧版の資格設定を含まない既定loopbackエントリだけを明示的に移行する場合：
githubie configure codex --replace-legacy
githubie unconfigure codex
githubie unconfigure claude
```

登録範囲はユーザー単位です。Codexは構成ホーム（指定時は `CODEX_HOME`）の `config.toml`、Claude Codeはユーザーの `~/.claude.json` の `mcpServers` を更新します。登録コマンドは導入済み `githubie.exe mcp stdio` です。他の設定・MCPサーバーを保持し、同じ所有エントリでは変更しません。別のエントリ、壊れた設定、資格設定を含む旧HTTP登録、検出した同時編集は上書きせず失敗します。TOML/JSONを検証し、一時ファイルを完成させてから原子的に置換します。変更中は他の設定編集処理を閉じてください。

登録後はAIクライアントを再起動・再接続してください。プロジェクト・管理設定がユーザー設定より優先される場合がありますが、その範囲は変更しません。解除は現在の導入物とコマンド・引数が一致するエントリだけを削除します。クライアント未導入かつユーザー設定もない場合、設定を新規作成せずエラーにします。

中継はJSON-RPC ID・通知・SSE応答・キャンセルを維持します。中継の終了では未完了HTTPをキャンセルし、ホスト自体は停止しません。通信失敗後の更新操作は自動再試行しません。状態を確認してから再実行してください。

範囲・優先順位は公式の [Codex MCP文書](https://learn.chatgpt.com/docs/extend/mcp?surface=cli) と [Claude Code MCP文書](https://code.claude.com/docs/en/mcp) を参照してください。

## Repository登録とToken

MCPまたは `githubie mcp call github_repository_register '<JSON引数>'` で登録します。承認・Token画面はホスト自身のユーザー・セッションで開き、別のactive desktopを選びません。Tokenは専用UIから入力します。**Tokenなしで登録**を明示的に選んだ場合だけ、ローカル操作用の登録を作成します。承認拒否、キャンセル・閉じる、起動・protocol・timeout・暗号化・永続化失敗では新規登録を残しません。設定と暗号化Tokenを同時確定し、既存IDの設定・Tokenを上書きしません。名前変更・登録解除も、設定と資格のIDを1つのDBトランザクションで変更します。

`githubie auth set <repository>` はホストのToken画面を開き、取消・保存失敗では既存Tokenを保持します。`auth delete`・`auth test` もホストへ依頼し、CLIはRepository DBを開きません。Tokenをコマンド引数・JSON・ログへ書かないでください。旧 `--console` 入力は1.xの機能であり、2.0ユーザーホストの入力方式ではありません。

GitHub Repository作成は外部APIを含むため、ローカルSQLiteだけでは原子的に戻せません。GitHub作成後にlocal remote設定・登録が失敗した場合は、返却された作成先と外部状態を確認してください。無条件の再実行や、暗黙のrollbackとしての外部Repository削除は行いません。

## HTTP・IPC・Moyai

loopbackだけをユーザー認証には使いません。bootstrapを含む全ユーザーホストHTTP要求に、起動ごとの `X-Githubie-Session`、`X-Githubie-Generation`、正しいHost/Originを要求します。資格はCurrentUser DPAPIと非共有ACLで保護し、旧LocalMachine形式を取り込みません。CurrentUserOnly制御Pipeでは、OSが報告する接続元Windows sessionと起動資格も照合します。

Moyaiモードは `<install-root>\config\githubie.json` の `user_host` で有効化し、`githubie.json` に従来のissuer・trust・replay・project対応を設定します。所有者認証の後もES256署名・replay防止・操作束縛・選択済み直接接続方針を維持します。所有者資格とAssertionは相互に代用できません。

ユーザーセッションのMoyaiクライアントは、全HTTP要求に同じユーザーの所有者資格と起動世代を付け、保護Provider操作には署名Assertionも付けます。旧共有LocalSystemクライアントの権限を暗黙に引き継ぎません。新プロフィールのendpoint・信頼鍵・project対応を再設定し、通常切替前にConsumerを検証してください。保護接続資格を共有machine設定や標準出力helperへ書かないでください。

### 版付き接続検出契約（候補・Consumer合意待ち）

対象は現在ユーザーのLocalApplicationData/Akatsukisoft/Githubie配下の既定profileだけです。他profileやportの探索、旧Serviceや版なし`connection.dpapi`へのフォールバックはしません。endpointはuserinfo/query/fragmentのない`http://127.0.0.1:<port>/mcp`だけとします。portは正本JSONだけから読み、再起動時も自動選択しません。

UTF-8 JSONの13項目を固定します。`schema_version`=1、`provider_id`=`githubie`、`profile_id`=GUID、`owner_sid`、`session_id`=Int32、`process_id`=Int32、`process_start_utc_ticks`=.NET UTC ticksのInt64、`endpoint`、`generation`=GUIDのN形式32文字、`runtime_path`=実行ファイル絶対path、`runtime_sha256`、`profile_sha256`、`protected_credential`です。SHA-256は選択した実行ファイルと`profile.json`の生byte列の大文字16進値です。Consumerは選択runtime/profileを固定します。hashは署名や任意runtimeを選択する権限ではありません。

`protected_credential`はUTF-8の`Githubie.UserDPAPI.v2`＋NUL＋DPAPI CurrentUser blobを標準padding付きBase64にした値です。追加entropyはUTF-8の`Githubie/UserHost/Connection/v1`。DPAPI平文はランダム32byteの標準padding付きBase64文字列をUTF-8にしたものです。headerにはこの平文文字列をそのまま付け、暗号blobは送りません。平文はメモリー内だけで扱い、文字列表現・検出エラーには資格や保護本文を含めません。

復号前に、重複・未知・不足field、65536byte超、非対応版/Provider、path内reparse、所有者以外に開いたACLを拒否します。root/data directoryは継承を切り現在SIDだけを許可し、profile/discovery fileはその所有者だけの継承を許可します。検出のたびに現在SID/profile/session、生存PIDと正確な開始ticks、実行ファイルpath/hash、profile hashを照合します。同directory内の一時fileからatomicに公開し、毎起動で資格とgenerationを更新します。旧世代の終了処理は新世代のdiscoveryを削除できません。

安定したローカルエラーは`discovery_missing`、`discovery_invalid`、`discovery_unsupported`、`discovery_profile_mismatch`、`discovery_acl_rejected`、`discovery_reparse_rejected`、`discovery_host_stale`、`discovery_runtime_mismatch`、`discovery_runtime_unavailable`、`discovery_credential_invalid`、`discovery_endpoint_rejected`です。HTTPの所有者/世代拒否は403と`X-Githubie-Error: user_host_owner_rejected`。ES256エラーは既存の別認証契約を維持します。

native clientはredirectを禁止します。明示的な所有者拒否で、応答frame未出力のinitialize/ping/tools-list/prompts-list/get-versionメタデータ要求だけ、discoveryを1回再検出します。書込や結果不明の通信失敗は自動再送しません。他Consumerも上限を守り、書込を再送する前に状態を照合してください。この候補はMoyai相互運用完了や実機導入承認を意味しません。既存706件/MSIの証跡は改訂前sourceの結果です。

## 削除と復旧

バイナリ削除前に、登録したAIクライアントの `unconfigure`、ホスト停止、自動起動OFFを実施してから、管理者承認でMSIを削除します。ユーザー・旧データは保持し、他ユーザーのプロフィールは変更しません。

旧Serviceへ戻す場合は新ホスト・自動起動を停止し、2.0 MSIを削除した後、保管した対応1.x MSIを従前のMoyai・直接接続モード指定付きで導入します。旧版・Service ready・旧データを確認してからクライアントを再接続してください。新CurrentUser資格をmachine領域へ戻しません。ユーザープロフィールは再試行用に別途保持します。

## 受入条件

新規導入、OFFを保持する更新・修復、別管理者UAC、取消・失敗、ユーザー工程読戻し、削除・再導入・復旧、ログオン・ログオフ、トレイ、同session画面を確認します。承認・Token成功、skip、cancel、launch/timeout/protocol失敗、トランザクションrollback、既存登録保持も対象です。無資格HTTP/IPC、port/session競合、資格更新と、Codex・Claude Code双方および対象Moyai Consumerで代表的なread/write/errorを検証します。MSI作成だけで実機受入済みとはしません。

## 唯一の本体設定（2.1.0.0）

読込・保存先は `<install-root>\config\githubie.json` だけです。`user_host` に `moyai_integration`（省略時false）・`direct_unrestricted`（false）・`autostart_enabled`（true）を保存します。`direct_unrestricted=true` には `moyai_integration=true` が必要です。旧起動モード引数はJSONで有効な値の再指定だけを許可します。旧プロフィール内の `githubie.json`・`host.json`・`startup.json` は読まず、新規作成・自動統合もしません。

共有の自動起動設定は、setup/logon時に現在ユーザーのHKCUへ反映します。ON/OFF変更には正本ファイルの書込権限が必要です。他のJSON設定は保持し、保存・読戻し失敗時はHKCUを元に戻します。他ユーザーは次のsetup/logon時に反映します。MSIは正本がない場合だけ初期JSONを作り、更新・アンインストール時も既存内容を保持します。旧1.x読込処理は新しい `user_host` を認識しないため、実機切替時に確認してください。既存正本をサンプルで上書きしないでください。判断の正本は[ADR0041](docs/adr/0041-single-canonical-configuration.md)です。識別・接続・DBファイルは実行データであり、別の本体設定ではありません。



## 同一Windowsユーザー認証（候補2.1.0.0）

唯一のJSONの `user_host.authentication_mode` に `windows_owner` を明示すると、所有者認証済みユーザーホスト接続でMoyai/nativeを同じWindowsユーザーとして扱います。省略値 `legacy` は従来動作を保持します。新規MSIの既定JSONは `windows_owner` を明示しますが、既存JSONは上書きしません。このモードは `--user-host` 専用で、旧Serviceでは拒否します。

全HTTP要求にschema1 discoveryの `X-Githubie-Session` と `X-Githubie-Generation` が必要です。ES256 Assertion・Issuer鍵・Project署名scope・legacy tokenは必須にしません。旧Moyai/direct設定とtrust資産は保持しますが、このモードの認可に使用しません。Repository/branch制限・本人承認・外部サービス資格・監査は維持します。監査主体はWindows所有SIDです。

Consumerは版と `github_provider_capabilities.authentication.owner_authentication` の明示契約・対象tool名を確認してから操作します。通常のcapabilityだけでは署名不要と判断できません。ownerモードへAuthorizationを渡すと403/`user_host_auth_mode_mismatch`、所有者資格・世代等の拒否は既存の403/`user_host_owner_rejected`です。別経路fallbackと結果不明mutationの自動再送を行いません。

正確なwire・能力フィールド・対象操作・エラーは[Windows owner認証v1](docs/specifications/windows-owner-authentication-v1.md)、設計判断は[ADR0042](docs/adr/0042-windows-owner-authentication.md)を参照してください。現在は契約提案・候補の段階です。Moyai Consumer合意/相互受入と通常環境切替は別工程で、実装候補を稼働中の設定へ自動適用しません。既存鍵・DB・trustを削除しないでください。
