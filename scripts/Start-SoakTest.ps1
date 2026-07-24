[CmdletBinding()]
param(
    [ValidateRange(0.01, 168)]
    [double]$DurationHours = 24,

    [ValidateRange(0, 604800)]
    [int]$DurationSeconds = 0,

    [ValidateRange(5, 3600)]
    [int]$SampleIntervalSeconds = 60,

    [string]$ProcessName = "ObsidianWinSync.Tray",

    [string]$ConfigPath,

    [string]$OutputDirectory = (Join-Path "artifacts" ("soak-" + (Get-Date -Format "yyyyMMdd-HHmmss")))
)

$ErrorActionPreference = "Stop"
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputPath) {
    throw "Output directory already exists: $outputPath"
}
[void](New-Item -ItemType Directory -Path $outputPath)

$resolvedConfig = $null
$statePath = $null
if ($ConfigPath) {
    $resolvedConfig = [IO.Path]::GetFullPath($ConfigPath)
    if (-not (Test-Path -LiteralPath $resolvedConfig)) {
        throw "Configuration file was not found: $resolvedConfig"
    }
    $configuration = Get-Content -LiteralPath $resolvedConfig -Raw | ConvertFrom-Json
    $configuredState = "state.json"
    if ($configuration.stateFilePath) {
        $configuredState = [string]$configuration.stateFilePath
    }
    if ([IO.Path]::IsPathRooted($configuredState)) {
        $statePath = [IO.Path]::GetFullPath($configuredState)
    } else {
        $statePath = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $resolvedConfig) $configuredState))
    }
}

function Get-DirectoryBytes([string]$Path) {
    if (-not $Path -or -not (Test-Path -LiteralPath $Path)) { return 0L }
    try {
        return [long](Get-ChildItem -LiteralPath $Path -File -Recurse -ErrorAction Stop |
            Measure-Object -Property Length -Sum).Sum
    } catch {
        return -1L
    }
}

$metricsPath = Join-Path $outputPath "metrics.csv"
$eventsPath = Join-Path $outputPath "events.csv"
$manifestPath = Join-Path $outputPath "manifest.json"
"timestamp,event,notes" | Set-Content -LiteralPath $eventsPath -Encoding utf8
$utf8NoBom = New-Object Text.UTF8Encoding($false)
$metricsHeader = "Timestamp,ProcessCount,ProcessId,WorkingSetMb,PrivateMemoryMb,HandleCount,ThreadCount,CpuSeconds,StateBytes,HistoryBytes,LogBytes,BackupBytes"
[IO.File]::WriteAllText($metricsPath, $metricsHeader + [Environment]::NewLine, $utf8NoBom)

$manifest = [ordered]@{
    startedAt = (Get-Date).ToString("o")
    durationHours = $DurationHours
    durationSecondsOverride = $DurationSeconds
    sampleIntervalSeconds = $SampleIntervalSeconds
    processName = $ProcessName
    configPath = $resolvedConfig
    computerName = $env:COMPUTERNAME
    userName = $env:USERNAME
    osVersion = [Environment]::OSVersion.VersionString
    powershellVersion = $PSVersionTable.PSVersion.ToString()
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding utf8

$deadline = (Get-Date).AddHours($DurationHours)
if ($DurationSeconds -gt 0) {
    $deadline = (Get-Date).AddSeconds($DurationSeconds)
}
Write-Host "Monitoring started. Scheduled end: $($deadline.ToString('yyyy-MM-dd HH:mm:ss'))"
Write-Host "Record manual operations in: $eventsPath"

while ((Get-Date) -lt $deadline) {
    $timestamp = Get-Date
    $processes = @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue)
    $process = $processes | Sort-Object StartTime | Select-Object -First 1
    $stateDirectory = $null
    if ($statePath) {
        $stateDirectory = Split-Path -Parent $statePath
    }
    $processId = 0
    $workingSetMb = 0
    $privateMemoryMb = 0
    $handleCount = 0
    $threadCount = 0
    $cpuSeconds = 0
    if ($process) {
        $processId = $process.Id
        $workingSetMb = [math]::Round($process.WorkingSet64 / 1MB, 2)
        $privateMemoryMb = [math]::Round($process.PrivateMemorySize64 / 1MB, 2)
        $handleCount = $process.HandleCount
        $threadCount = $process.Threads.Count
        $cpuSeconds = [math]::Round($process.CPU, 2)
    }
    $stateBytes = 0L
    $historyBytes = 0L
    $logBytes = 0L
    $backupBytes = 0L
    if ($statePath -and (Test-Path -LiteralPath $statePath)) {
        $stateBytes = (Get-Item -LiteralPath $statePath).Length
    }
    if ($stateDirectory) {
        $historyPath = Join-Path $stateDirectory "sync-history.json"
        if (Test-Path -LiteralPath $historyPath) {
            $historyBytes = (Get-Item -LiteralPath $historyPath).Length
        }
        $logBytes = Get-DirectoryBytes (Join-Path $stateDirectory "logs")
        $backupBytes = Get-DirectoryBytes (Join-Path $stateDirectory "backup")
    }
    $culture = [Globalization.CultureInfo]::InvariantCulture
    $fields = @(
        $timestamp.ToString("o", $culture),
        $processes.Count.ToString($culture),
        $processId.ToString($culture),
        ([double]$workingSetMb).ToString($culture),
        ([double]$privateMemoryMb).ToString($culture),
        $handleCount.ToString($culture),
        $threadCount.ToString($culture),
        ([double]$cpuSeconds).ToString($culture),
        $stateBytes.ToString($culture),
        $historyBytes.ToString($culture),
        $logBytes.ToString($culture),
        $backupBytes.ToString($culture)
    )
    [IO.File]::AppendAllText($metricsPath, (($fields -join ",") + [Environment]::NewLine), $utf8NoBom)
    Start-Sleep -Seconds $SampleIntervalSeconds
}

Write-Host "Monitoring completed: $outputPath"
Write-Host "Next: scripts/Summarize-SoakTest.ps1 -TestDirectory `"$outputPath`""
