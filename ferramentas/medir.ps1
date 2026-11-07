$ErrorActionPreference = 'Stop'

$intervalSeconds = 5
$sampleCount = 60
$processorCount = [Environment]::ProcessorCount
$outputPath = Join-Path $PSScriptRoot 'baseline.csv'
$header = 'run_id,sample,timestamp_utc,app_cpu_percent,app_memory_mib,webview2_cpu_percent,webview2_memory_mib'
$invariantCulture = [System.Globalization.CultureInfo]::InvariantCulture

if (Test-Path -LiteralPath $outputPath) {
    $existingHeader = Get-Content -LiteralPath $outputPath -TotalCount 1
    if ($existingHeader -ne $header) {
        throw "O arquivo '$outputPath' não tem o cabeçalho esperado. Preserve-o e escolha outro nome antes de continuar."
    }
}
else {
    Set-Content -LiteralPath $outputPath -Value $header -Encoding UTF8
}

function Get-ProcessSnapshot {
    param([Parameter(Mandatory = $true)][string]$Name)

    $snapshot = @{}
    foreach ($process in @(Get-Process -Name $Name -ErrorAction SilentlyContinue)) {
        $process.Refresh()
        $snapshot[[int]$process.Id] = [pscustomobject]@{
            StartTimeUtcTicks = $process.StartTime.ToUniversalTime().Ticks
            CpuSeconds        = $process.TotalProcessorTime.TotalSeconds
            WorkingSetBytes   = [long]$process.WorkingSet64
        }
    }

    return ,$snapshot
}

function Get-ProcessMetrics {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Current,
        [Parameter(Mandatory = $true)][hashtable]$Previous,
        [Parameter(Mandatory = $true)][double]$ElapsedSeconds,
        [Parameter(Mandatory = $true)][long]$PreviousSampleUtcTicks
    )

    $cpuSeconds = 0.0
    $workingSetBytes = 0L

    foreach ($trackedProcessId in $Current.Keys) {
        $currentProcess = $Current[$trackedProcessId]
        $workingSetBytes += [long]$currentProcess.WorkingSetBytes

        if ($Previous.ContainsKey($trackedProcessId) -and
            $Previous[$trackedProcessId].StartTimeUtcTicks -eq $currentProcess.StartTimeUtcTicks) {
            $cpuSeconds += [Math]::Max(
                0.0,
                $currentProcess.CpuSeconds - $Previous[$trackedProcessId].CpuSeconds)
        }
        elseif ($currentProcess.StartTimeUtcTicks -gt $PreviousSampleUtcTicks) {
            $cpuSeconds += [Math]::Max(0.0, $currentProcess.CpuSeconds)
        }
    }

    return [pscustomobject]@{
        CpuPercent      = ($cpuSeconds / $ElapsedSeconds / $processorCount) * 100.0
        WorkingSetMiB   = $workingSetBytes / 1MB
    }
}

$previousApp = Get-ProcessSnapshot -Name 'TransparentChatFilterUnited'
if ($previousApp.Count -eq 0) {
    throw 'Abra o TransparentChatFilterUnited antes de iniciar a medição.'
}

$previousWebView2 = Get-ProcessSnapshot -Name 'msedgewebview2'
$runId = [DateTimeOffset]::UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", $invariantCulture)
$timer = [System.Diagnostics.Stopwatch]::StartNew()
$previousElapsedSeconds = $timer.Elapsed.TotalSeconds
$previousSampleUtcTicks = [DateTimeOffset]::UtcNow.UtcTicks

Write-Host "Medição iniciada (run_id=$runId): 60 amostras, uma a cada 5 segundos."
Write-Host "Mantenha o app aberto e conectado ao canal durante os 5 minutos."

for ($sample = 1; $sample -le $sampleCount; $sample++) {
    Start-Sleep -Seconds $intervalSeconds

    $currentApp = Get-ProcessSnapshot -Name 'TransparentChatFilterUnited'
    if ($currentApp.Count -eq 0) {
        throw 'O processo TransparentChatFilterUnited foi encerrado durante a medição.'
    }

    $currentWebView2 = Get-ProcessSnapshot -Name 'msedgewebview2'
    $sampleElapsedSeconds = $timer.Elapsed.TotalSeconds
    $elapsedSeconds = $sampleElapsedSeconds - $previousElapsedSeconds
    if ($elapsedSeconds -le 0) {
        throw 'Não foi possível calcular o intervalo decorrido entre amostras.'
    }

    $sampleUtc = [DateTimeOffset]::UtcNow
    $appMetrics = Get-ProcessMetrics `
        -Current $currentApp `
        -Previous $previousApp `
        -ElapsedSeconds $elapsedSeconds `
        -PreviousSampleUtcTicks $previousSampleUtcTicks
    $webView2Metrics = Get-ProcessMetrics `
        -Current $currentWebView2 `
        -Previous $previousWebView2 `
        -ElapsedSeconds $elapsedSeconds `
        -PreviousSampleUtcTicks $previousSampleUtcTicks

    $timestamp = $sampleUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", $invariantCulture)
    $appCpu = [string]::Format($invariantCulture, '{0:F2}', $appMetrics.CpuPercent)
    $appMemory = [string]::Format($invariantCulture, '{0:F2}', $appMetrics.WorkingSetMiB)
    $webView2Cpu = [string]::Format($invariantCulture, '{0:F2}', $webView2Metrics.CpuPercent)
    $webView2Memory = [string]::Format($invariantCulture, '{0:F2}', $webView2Metrics.WorkingSetMiB)
    $row = '{0},{1},{2},{3},{4},{5},{6}' -f `
        $runId, $sample, $timestamp, $appCpu, $appMemory, $webView2Cpu, $webView2Memory
    Add-Content -LiteralPath $outputPath -Value $row -Encoding UTF8

    $previousApp = $currentApp
    $previousWebView2 = $currentWebView2
    $previousElapsedSeconds = $sampleElapsedSeconds
    $previousSampleUtcTicks = $sampleUtc.UtcTicks
}

Write-Host "Medição concluída. Resultado acrescentado em: $outputPath"
