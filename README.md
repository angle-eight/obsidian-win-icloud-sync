# ObsidianWinSync

Windows 上のローカル Obsidian Vault と、iCloud Drive 上の Vault を双方向に同期する常駐アプリです。
(ObsidianのiCloud同期がWindowsでうまく機能しないため、WindowsではLocal Vaultを使い、iCloud Driveと同期して使用するためのものです。)

> 現在は初期開発版です。重要な Vault で使う前にバックアップを取り、初回はテスト用 Vault で動作を確認してください。

## 主な機能

- ローカルと iCloud Drive の Vault を定期的に双方向同期
- タスクトレイから今すぐ同期、一時停止、設定変更、ログ表示、終了を操作
- Windows ログイン時の自動起動
- 上書き・削除前の自動バックアップと、画面からの復元
- 両側で変更されたファイルなどの競合を検出し、採用する側を選んで解決
- 同期状態、次回同期予定、直近の同期履歴を表示
- 一時ファイルを使った安全な書き込みと、読み取りエラー時の同期停止

## 必要なもの

- Windows 10 または Windows 11（64 bit）
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- iCloud for Windows で利用できる iCloud Drive

## コンパイル

PowerShell を開き、このリポジトリのルートへ移動して次を実行します。

```powershell
dotnet publish ObsidianWinSync.Tray -p:PublishProfile=win-x64
```

コンパイルが完了すると、次のフォルダに単一の実行ファイルが作成されます。

```text
artifacts\tray-win-x64\ObsidianWinSync.Tray.exe
```

この EXE は .NET ランタイムを含むため、実行する PC に .NET を別途インストールする必要はありません。

## EXE を使う

1. `artifacts\tray-win-x64\ObsidianWinSync.Tray.exe` をダブルクリックします。
2. 初回起動時に表示される設定画面で、ローカル Vault と iCloud Drive 上の Vault を選びます。
3. 必要に応じて同期間隔、バックアップ、通知、Windows ログイン時の自動起動を設定し、保存します。
4. タスクトレイの ObsidianWinSync アイコンを右クリックし、「今すぐ同期」を実行します。
5. 同期状態や競合、バックアップは同じタスクトレイメニューから確認します。

設定ファイルをまだ作成していない場合は、次の場所に自動作成されます。

```text
%USERPROFILE%\.obsidian-win-sync\obsidian-win-sync.json
```

同じフォルダに同期状態、履歴、ログ、バックアップも保存されます。

### 任意の場所の設定ファイルを使う

PowerShell から `--config` を付けて起動します。

```powershell
.\artifacts\tray-win-x64\ObsidianWinSync.Tray.exe --config C:\path\to\obsidian-win-sync.json
```

## 安全に使うために

- 初回同期の前に、両方の Vault をバックアップしてください。
- iCloud Drive のファイルが Windows 上へダウンロード済みであることを確認してください。
- 競合が表示された場合は、両側の内容を確認してから採用する側を選んでください。
- 同期中は EXE を強制終了したり、対象 Vault を移動したりしないでください。

CLI、設定ファイルの詳細、終了コード、テスト、常駐試験については [開発者マニュアル](docs/developer-manual.md) を参照してください。
