param(
    [string]$PackageRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$AppExe = (Join-Path (Split-Path -Parent $PSScriptRoot) 'app\ErsatzTV.exe'),
    [string[]]$AppArgs = @(),
    [string]$UiUrl = 'http://localhost:8409',
    [int]$ReadyTimeoutSeconds = 90,
    [switch]$OpenBrowser = $true
)

$ErrorActionPreference = 'Stop'

$runtimeDir = Join-Path $PackageRoot 'runtime'
$logsDir = Join-Path $runtimeDir 'logs'
$statePath = Join-Path $runtimeDir 'launcher-state.json'
$logPath = Join-Path $logsDir ('launcher-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.log')

New-Item -ItemType Directory -Force -Path $runtimeDir, $logsDir | Out-Null

function Write-Log {
    param([string]$Message)

    $line = '[{0}] {1}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Message
    $line | Tee-Object -FilePath $logPath -Append
}

function Stop-RecordedProcess {
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        return
    }

    $state = Get-Content $Path | ConvertFrom-Json
    if (-not $state.pid) {
        return
    }

    $process = Get-Process -Id ([int]$state.pid) -ErrorAction SilentlyContinue
    if ($null -eq $process) {
        Write-Log "Recorded pid $($state.pid) is already gone"
        return
    }

    Write-Log "Stopping recorded pid $($state.pid)"
    Stop-Process -Id ([int]$state.pid) -Force -ErrorAction Stop
    Start-Sleep -Seconds 1
}

function Wait-ForUrl {
    param(
        [string]$Url,
        [int]$TimeoutSeconds
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 5
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 500) {
                Write-Log "Ready url responded with status $($response.StatusCode)"
                return
            }
        }
        catch {
            Start-Sleep -Seconds 1
        }
    }

    throw "Timed out waiting for $Url"
}

Write-Log "PackageRoot=$PackageRoot"
Write-Log "AppExe=$AppExe"
Write-Log "UiUrl=$UiUrl"

if (-not (Test-Path $AppExe)) {
    Write-Log 'App exe not found'
    throw "App exe not found: $AppExe"
}

Stop-RecordedProcess -Path $statePath

$process = Start-Process -FilePath $AppExe -ArgumentList $AppArgs -WorkingDirectory (Split-Path -Parent $AppExe) -PassThru
Write-Log "Started pid $($process.Id)"

@{
    pid = $process.Id
    appExe = $AppExe
    uiUrl = $UiUrl
    startedAt = (Get-Date).ToString('o')
} | ConvertTo-Json | Set-Content -Path $statePath -Encoding UTF8

Wait-ForUrl -Url $UiUrl -TimeoutSeconds $ReadyTimeoutSeconds

if ($OpenBrowser) {
    Write-Log 'Opening browser'
    Start-Process $UiUrl | Out-Null
}

exit 0
