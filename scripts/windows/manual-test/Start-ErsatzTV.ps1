param(
    [string]$PackageRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$AppExe = (Join-Path (Split-Path -Parent $PSScriptRoot) 'app\ErsatzTV.exe')
)

$ErrorActionPreference = 'Stop'

$runtimeDir = Join-Path $PackageRoot 'runtime'
$logsDir = Join-Path $runtimeDir 'logs'
$logPath = Join-Path $logsDir ('launcher-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.log')

New-Item -ItemType Directory -Force -Path $runtimeDir, $logsDir | Out-Null

function Write-Log {
    param([string]$Message)

    $line = '[{0}] {1}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Message
    $line | Tee-Object -FilePath $logPath -Append
}

Write-Log 'Launcher started'
Write-Log "PackageRoot=$PackageRoot"
Write-Log "AppExe=$AppExe"

if (-not (Test-Path -LiteralPath $AppExe -PathType Leaf)) {
    Write-Log 'App exe not found'
    throw "App exe not found: $AppExe"
}

Write-Log 'Launcher validation passed'
exit 0
