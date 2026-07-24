# ObsidianWinSync 開発者マニュアル

Windows上のローカルObsidian Vaultと、iCloud Drive上のVaultを双方向同期するコンソールアプリです。

> 現在は初期開発版です。重要なVaultで使う前にバックアップを取り、必ず `--dry-run` で操作内容を確認してください。

## 設定

`obsidian-win-sync.example.json` を `obsidian-win-sync.json` としてコピーし、2つのVaultパスを設定します。
`intervalSeconds` は定期同期の間隔（秒）で、省略時は30秒です。
上書き・削除前のファイルは既定で7日間バックアップされ、同期ログは14日間保存されます。

```json
{
  "localVaultPath": "C:\\path\\to\\local-vault",
  "icloudVaultPath": "C:\\path\\to\\icloud-vault",
  "intervalSeconds": 30,
  "excludePatterns": [".obsidian/workspace*.json", "*.tmp"],
  "backup": { "enabled": true, "retentionDays": 7, "maximumSizeMb": 1024 },
  "logging": { "retentionDays": 14 }
}
```

## 使い方

```powershell
dotnet run -- validate --config obsidian-win-sync.json
dotnet run -- scan --config obsidian-win-sync.json
dotnet run -- sync --dry-run --config obsidian-win-sync.json
dotnet run -- sync --config obsidian-win-sync.json
dotnet run -- watch --config obsidian-win-sync.json
dotnet run -- backup list --config obsidian-win-sync.json
dotnet run -- backup restore <run-id> <local|icloud> <relative-path> --config obsidian-win-sync.json
```

復元先に同名ファイルがある場合は確認を求めます。非対話実行で上書きする場合だけ `--force` を明示してください。

タスクトレイ版は次のコマンドで起動できます。二重起動は防止され、メニューから今すぐ同期、一時停止、設定・ログ表示、終了を操作できます。
設定画面ではVault、同期間隔、バックアップ、成功通知、Windowsログイン時の自動起動を変更できます。

```powershell
dotnet run --project ObsidianWinSync.Tray -- --config obsidian-win-sync.json
```

常駐版で `--config` を省略した場合は、次の順で `obsidian-win-sync.json` を探します。

1. `ObsidianWinSync.Tray.exe` と同じフォルダ
2. ユーザープロファイル内の `.obsidian-win-sync`（例: `C:\Users\user\.obsidian-win-sync`）
3. `%LOCALAPPDATA%\ObsidianWinSync`

どこにも存在しない場合は、`%USERPROFILE%\.obsidian-win-sync\obsidian-win-sync.json` を新しい設定ファイルの保存先として使用します。同じフォルダに `state.json`、`logs`、`backup` を保存します。任意の場所に置く場合は `--config` で指定できます。

配布用のwin-x64単一ファイルは次のコマンドで作成し、`artifacts/tray-win-x64` に出力します。

```powershell
dotnet publish ObsidianWinSync.Tray -p:PublishProfile=win-x64
```

初回同期で両側に同名かつ内容の違うファイルがある場合や、前回同期後に両側で同じファイルを変更した場合は競合になります。対話実行ではlocal、iCloud、skipから処理を選択できます。入力をリダイレクトした非対話実行では競合を保持し、終了コード3を返します。

同期状態は既定で設定ファイルと同じフォルダの `state.json` に保存されます。状態ファイルは操作が完了した後だけ更新され、設定されたlocal / iCloud Vaultの組み合わせに関連付けられます。別のVault用の状態ファイルを検出した場合は、誤った削除履歴を適用せず同期を停止します。

`state.json` が破損している場合、アプリは同期を停止して `state.json.bak` を検査します。利用可能なバックアップがある場合も自動復旧はせず、CLIまたは常駐版の確認に同意したときだけ復旧します。元の破損ファイルは調査用に `state.json.corrupt-日時` として保存されます。入力をリダイレクトしたCLI実行では確認できないため復旧せず終了します。

スキャン中にロック、アクセス拒否、iCloud未取得などでファイルまたはフォルダを読み取れなかった場合は、対象を削除済みとは扱わず同期全体を停止します。エラーには読み取れなかった相対パスと処理内容が表示され、同期stateは更新されません。

同期ログはstateと同じ基準フォルダの `logs\yyyy-MM-dd.log` に保存されます。同期開始・完了、適用した操作、競合、state復旧、キャンセル、失敗をrun ID付きで記録します。失敗時は分類コード、再試行可能かどうか、対象相対パス、例外型を確認できます。ノート本文はログへ記録しません。

定期同期と常駐版では競合ダイアログを表示しません。競合は `state.json` の保留項目として両側の状態と初回検出時刻を保存し、競合していない他のファイルは同期を続けます。常駐版の「競合を確認」から、相対パス、競合種別、初回検出時刻、両側の更新日時とサイズを一覧表示できます。一覧で項目を選ぶと、1MB以下のUTF-8テキストは行単位差分を、バイナリ・大容量・削除競合はサイズ、更新日時、SHA-256を表示します。

競合一覧では1件または複数件を選択し、localまたはiCloud側を一括採用できます。適用直前に選択項目を再スキャンし、一覧表示後に一件でも変化していれば全件を上書きせず中止して最新状態を表示します。内容を確認してもう一度選択した場合だけ解決し、上書き・削除前には通常の同期と同じバックアップを作成します。単発の対話CLI `sync` でも引き続きlocal、iCloud、保留を選択できます。

常駐版の「同期状態と履歴」では、現在の状態、次回同期予定、最終結果と直近100回の履歴を確認できます。履歴には開始時刻、成功・競合・キャンセル・失敗、コピー・削除・競合件数、所要時間、エラー分類を保存します。構造化履歴はstateと同じ基準フォルダの `sync-history.json` に保存されます。

常駐版の「バックアップを復元」では、作成時刻、実行ID、復元先、相対パス、サイズからバックアップを選択して復元できます。復元先が存在する場合は確認なしに上書きせず、同意後も現在版を新しいバックアップへ退避してから復元します。画面を開いている間と復元中は定期同期を止め、CLIを含む他の同期処理とも同時実行しません。

常駐版の初回起動時に `obsidian-win-sync.json` が見つからない場合は、同期を開始せず設定画面を自動表示します。キャンセルした場合も「設定」または「設定ファイルを開く」から初期設定へ戻れます。同一内容の通知は既定で300秒間抑制され、連続エラーや競合による通知連打を防ぎます。抑制時間は設定画面または `notifications.minimumIntervalSeconds` で変更でき、`0` で抑制を無効にできます。

同期コピー、state、履歴、バックアップは一時ファイルへの書き込みが完了してから最終パスへ反映します。キャンセル、ディスク書き込みエラー、置換直前の失敗では既存ファイルを維持し、部分的な一時ファイルを除去します。自動テストではロック、疑似ディスク不足、部分コピー、キャンセル、長いUnicode・絵文字パス、2MBファイル、バックアップ容量、ログ保持期間を検証しています。

実iCloudを使う24時間常駐試験は [soak-test.md](soak-test.md) の手順で実施します。`../scripts/Start-SoakTest.ps1` がプロセス数、メモリ、ハンドル、スレッド、ログ・バックアップ容量を採取し、`../scripts/Summarize-SoakTest.ps1` が増減とログ上の重複同期を集計します。

## 終了コード

- `0`: 成功
- `1`: ファイル操作などの実行エラー
- `2`: 設定またはコマンドのエラー
- `3`: 未解決の競合あり

## 開発

```powershell
dotnet test ObsidianWinSync.sln
```

設計と今後の実装順は [plan.md](plan.md) を参照してください。
