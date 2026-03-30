param(
    [string]$PackageRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$AppExe = (Join-Path (Split-Path -Parent $PSScriptRoot) 'app\ErsatzTV.exe'),
    [string[]]$AppArgs = @(),
    [string]$UiUrl = 'http://localhost:8409',
    [int]$ReadyTimeoutSeconds = 90,
    [string]$OpenBrowser = 'True'
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

Write-Log "PackageRoot=$PackageRoot"
Write-Log "AppExe=$AppExe"
Write-Log "UiUrl=$UiUrl"

if (-not (Test-Path $AppExe)) {
    Write-Log 'App exe not found'
    throw "App exe not found: $AppExe"
}

Write-Log 'Launcher validation passed'
exit 0
