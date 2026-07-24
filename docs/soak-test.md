# T-39 24時間常駐・実iCloud受け入れ試験

## 目的

実際のiCloud DriveとObsidianを使用し、長時間常駐、競合、ファイルロック、通信断、スリープ復帰、自動起動でデータ損失やリソースリークが起きないことを確認する。

この試験は実データを変更する。日常利用中のVaultを直接使わず、localとiCloudの両方に試験専用Vaultを作成すること。

## 事前条件

- WindowsとiCloud for Windowsを更新済み
- 試験専用のlocal / iCloud Vaultを用意済み
- 両Vaultの試験前バックアップを取得済み
- `obsidian-win-sync.json` の同期間隔を30秒に設定
- バックアップ、エラー通知、競合通知を有効化
- Windowsログイン時起動を有効化
- 最新のRelease版をpublish済み
- `dotnet test ObsidianWinSync.sln --no-restore` が成功

## 開始手順

1. 常駐版を起動し、初回同期が完了するまで待つ。
2. 「同期状態と履歴」で成功を確認する。
3. CLIの `sync --dry-run` で意図しない操作がないことを確認する。
4. 管理者ではないPowerShellで監視を開始する。

```powershell
./scripts/Start-SoakTest.ps1 `
  -DurationHours 24 `
  -SampleIntervalSeconds 60 `
  -ConfigPath "$env:USERPROFILE\.obsidian-win-sync\obsidian-win-sync.json"
```

生成された `artifacts/soak-yyyyMMdd-HHmmss/events.csv` に、手動操作の時刻、イベント名、結果を追記する。設定内容やノート本文などの機密情報は記録しない。

## 実施シナリオ

各操作の前後で時刻を `events.csv` に記録し、最大2回の同期間隔内に結果が反映されることを確認する。

1. local側でMarkdownを新規作成、編集、移動、削除する。
2. iCloud側で別のMarkdownを新規作成、編集、移動、削除する。
3. 日本語、絵文字、添付画像、1MB超のファイルを両方向で変更する。
4. 同じMarkdownを両側で編集し、通知が連打されず競合一覧へ1件として表示されることを確認する。
5. 競合一覧で差分を確認し、local採用とiCloud採用をそれぞれ試す。
6. ファイルを別プロセスで排他ロックし、同期が削除扱いせず失敗履歴を残すことを確認する。
7. iCloudファイルをオンラインのみの状態にして同期し、未取得時の挙動とログを記録する。
8. ネットワークを切断して編集し、再接続後に同期が回復することを確認する。
9. PCをスリープまたは休止し、復帰後に多重同期せず次回同期が動くことを確認する。
10. Windowsを再起動し、自動起動が1プロセスだけになることを確認する。
11. バックアップ一覧から1ファイルを復元し、現在版の退避と次回同期を確認する。
12. 以後は通常利用相当の編集を行い、合計24時間以上常駐させる。

## 終了手順

1. 24時間経過後、常駐版の履歴とログを確認する。
2. 未解決競合を意図どおり解決する。
3. CLIで最終確認する。

```powershell
dotnet run --project ObsidianWinSync.csproj -- sync --dry-run --config "<試験用設定JSON>"
```

4. 監視結果を集計する。

```powershell
./scripts/Summarize-SoakTest.ps1 -TestDirectory "artifacts/soak-yyyyMMdd-HHmmss"
```

5. `summary.md` の判定欄と所見を記入し、`events.csv`、アプリログ、`sync-history.json`を照合する。

## 合格条件

- 24時間以上の測定データがある
- ObsidianWinSync.Trayの同時プロセス数が常に1以下
- 同期開始から終了までの重複がログにない
- 操作終了後もメモリとハンドル数が単調増加し続けない
- 同一エラーまたは競合の通知連打がない
- ロック・通信断・スリープ後に自動同期が回復する
- 読み取れないファイルを削除済みとして反映していない
- 最終dry-runに意図しないコピー・削除・競合がない
- バックアップからの復元が成功する

## 中止条件

- 試験対象外のVaultやファイルが変更された
- 意図しない削除または上書きを検出した
- state破損、復旧不能、同時同期を検出した
- メモリ、ハンドル、ログ容量が継続的に急増した

中止時は常駐版を終了し、local / iCloud両Vault、設定、state、ログ、履歴、監視結果を保全する。原因確認前に再同期やstate削除を行わない。
