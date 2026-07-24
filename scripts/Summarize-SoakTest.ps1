[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$TestDirectory
)

$ErrorActionPreference = "Stop"
$testPath = [IO.Path]::GetFullPath($TestDirectory)
$metricsPath = Join-Path $testPath "metrics.csv"
if (-not (Test-Path -LiteralPath $metricsPath)) {
    throw "metrics.csv was not found: $metricsPath"
}
$metrics = @(Import-Csv -LiteralPath $metricsPath)
if ($metrics.Count -eq 0) { throw "No metric samples were found." }

$running = @($metrics | Where-Object { [int]$_.ProcessCount -gt 0 })
$first = $running | Select-Object -First 1
$last = $running | Select-Object -Last 1
$maxProcesses = ($metrics | Measure-Object -Property ProcessCount -Maximum).Maximum
$maxWorkingSet = ($running | Measure-Object -Property WorkingSetMb -Maximum).Maximum
$maxPrivate = ($running | Measure-Object -Property PrivateMemoryMb -Maximum).Maximum
$maxHandles = ($running | Measure-Object -Property HandleCount -Maximum).Maximum
$maxThreads = ($running | Measure-Object -Property ThreadCount -Maximum).Maximum
$missingSamples = @($metrics | Where-Object { [int]$_.ProcessCount -eq 0 }).Count
$overlapCount = 0
$unmatchedRuns = 0
$manifestPath = Join-Path $testPath "manifest.json"
if (Test-Path -LiteralPath $manifestPath) {
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.configPath -and (Test-Path -LiteralPath $manifest.configPath)) {
        $configuration = Get-Content -LiteralPath $manifest.configPath -Raw | ConvertFrom-Json
        $configuredState = "state.json"
        if ($configuration.stateFilePath) {
            $configuredState = [string]$configuration.stateFilePath
        }
        if ([IO.Path]::IsPathRooted($configuredState)) {
            $statePath = [IO.Path]::GetFullPath($configuredState)
        } else {
            $statePath = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $manifest.configPath) $configuredState))
        }
        $logDirectory = Join-Path (Split-Path -Parent $statePath) "logs"
        if (Test-Path -LiteralPath $logDirectory) {
            $activeRuns = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            foreach ($line in Get-ChildItem -LiteralPath $logDirectory -Filter "*.log" -File | Sort-Object Name | Get-Content) {
                if ($line -match "event=sync_started run=([^ ]+)") {
                    if ($activeRuns.Count -gt 0) { $overlapCount++ }
                    [void]$activeRuns.Add($Matches[1])
                } elseif ($line -match "event=(sync_finished|sync_failed|sync_cancelled) run=([^ ]+)") {
                    [void]$activeRuns.Remove($Matches[2])
                }
            }
            $unmatchedRuns = $activeRuns.Count
        }
    }
}

function Get-Delta($Start, $End, [string]$Property) {
    if (-not $Start -or -not $End) { return "n/a" }
    return [math]::Round(([double]$End.$Property - [double]$Start.$Property), 2)
}

$startTime = [datetime]$metrics[0].Timestamp
$endTime = [datetime]$metrics[-1].Timestamp
$lines = @(
    "# ObsidianWinSync 24-hour soak test summary",
    "",
    "- Started: $($startTime.ToString('yyyy-MM-dd HH:mm:ss'))",
    "- Finished: $($endTime.ToString('yyyy-MM-dd HH:mm:ss'))",
    "- Duration: $([math]::Round(($endTime - $startTime).TotalHours, 2)) hours",
    "- Samples: $($metrics.Count)",
    "- Samples with no process: $missingSamples",
    "- Maximum simultaneous processes: $maxProcesses",
    "- Overlapping sync starts in logs: $overlapCount",
    "- Sync runs without a terminal event: $unmatchedRuns",
    "",
    "| Metric | Start | End | Delta | Maximum |",
    "|---|---:|---:|---:|---:|",
    "| Working Set (MB) | $($first.WorkingSetMb) | $($last.WorkingSetMb) | $(Get-Delta $first $last 'WorkingSetMb') | $maxWorkingSet |",
    "| Private Memory (MB) | $($first.PrivateMemoryMb) | $($last.PrivateMemoryMb) | $(Get-Delta $first $last 'PrivateMemoryMb') | $maxPrivate |",
    "| Handles | $($first.HandleCount) | $($last.HandleCount) | $(Get-Delta $first $last 'HandleCount') | $maxHandles |",
    "| Threads | $($first.ThreadCount) | $($last.ThreadCount) | $(Get-Delta $first $last 'ThreadCount') | $maxThreads |",
    "",
    "## Acceptance checklist",
    "",
    "- [ ] Measurement lasted at least 24 hours",
    "- [ ] Simultaneous process count never exceeded one",
    "- [ ] Memory did not continuously grow after operations stopped",
    "- [ ] Handle count did not continuously grow",
    "- [ ] Notifications were not repeated excessively",
    "- [ ] Sync executions did not overlap",
    "- [ ] Final dry-run had no unintended changes",
    "",
    "## Notes",
    "",
    "Review events.csv, application logs, and sync-history.json."
)
$summaryPath = Join-Path $testPath "summary.md"
$lines | Set-Content -LiteralPath $summaryPath -Encoding utf8
Write-Host "Summary created: $summaryPath"
