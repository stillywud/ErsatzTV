param(
    [string]$PackageRoot = '',
    [string]$AppExe = '',
    [string[]]$AppArgs = @(),
    [string]$UiUrl = 'http://localhost:8409',
    [int]$ReadyTimeoutSeconds = 90,
    [switch]$OpenBrowser = $true
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($PackageRoot)) {
    if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'app') -PathType Container) {
        $PackageRoot = $PSScriptRoot
    }
    else {
        $PackageRoot = Split-Path -Parent $PSScriptRoot
    }
}

if ([string]::IsNullOrWhiteSpace($AppExe)) {
    $AppExe = Join-Path $PackageRoot 'app\ErsatzTV.exe'
}

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

function Get-RecordedState {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return $null
    }

    try {
        $stateContent = Get-Content -LiteralPath $Path -Raw -ErrorAction Stop
        if ([string]::IsNullOrWhiteSpace($stateContent)) {
            Write-Log "Ignoring invalid launcher state file '$Path': file is empty"
            return $null
        }

        $state = $stateContent | ConvertFrom-Json -ErrorAction Stop

        $parsedPid = 0
        if (-not [int]::TryParse([string]$state.pid, [ref]$parsedPid) -or $parsedPid -le 0) {
            Write-Log "Ignoring invalid launcher state file '$Path': pid is missing or invalid"
            return $null
        }

        $state.pid = $parsedPid
        return $state
    }
    catch {
        Write-Log "Ignoring invalid launcher state file '$Path': $($_.Exception.Message)"
        return $null
    }
}

function Stop-RecordedProcess {
    param([string]$Path)

    $state = Get-RecordedState -Path $Path
    if ($null -eq $state -or -not $state.pid) {
        return
    }

    $process = Get-Process -Id ([int]$state.pid) -ErrorAction SilentlyContinue
    if ($null -eq $process) {
        Write-Log "Recorded pid $($state.pid) is already gone"
        return
    }

    $pathMatches = $false
    if ($state.appExe) {
        try {
            $recordedAppExe = [System.IO.Path]::GetFullPath([string]$state.appExe)
            $processPath = [System.IO.Path]::GetFullPath([string]$process.Path)
            $pathMatches = [string]::Equals($recordedAppExe, $processPath, [System.StringComparison]::OrdinalIgnoreCase)
        }
        catch {
            Write-Log "Unable to verify recorded pid $($state.pid) executable path; skipping stop"
            return
        }
    }

    $startMatches = $false
    if ($state.startedAt) {
        try {
            $recordedStartedAt = ([datetimeoffset]::Parse([string]$state.startedAt)).UtcDateTime
            $processStartedAt = $process.StartTime.ToUniversalTime()
            $startDeltaSeconds = [Math]::Abs((New-TimeSpan -Start $recordedStartedAt -End $processStartedAt).TotalSeconds)
            $startMatches = $startDeltaSeconds -lt 5
        }
        catch {
            Write-Log "Unable to verify recorded pid $($state.pid) start time; skipping stop"
            return
        }
    }

    if (-not ($pathMatches -and $startMatches)) {
        Write-Log "Recorded pid $($state.pid) does not match recorded app identity; skipping stop"
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

if (-not (Test-Path -LiteralPath $AppExe -PathType Leaf)) {
    Write-Log 'App exe not found'
    throw "App exe not found: $AppExe"
}

Stop-RecordedProcess -Path $statePath

$process = Start-Process -FilePath $AppExe -ArgumentList $AppArgs -WorkingDirectory (Split-Path -Parent $AppExe) -PassThru
Write-Log "Started pid $($process.Id)"

@{
    pid = $process.Id
    appExe = (Resolve-Path -LiteralPath $AppExe).Path
    uiUrl = $UiUrl
    startedAt = $process.StartTime.ToUniversalTime().ToString('o')
} | ConvertTo-Json | Set-Content -Path $statePath -Encoding UTF8

Wait-ForUrl -Url $UiUrl -TimeoutSeconds $ReadyTimeoutSeconds

if ($OpenBrowser) {
    try {
        Write-Log 'Opening browser'
        Start-Process $UiUrl | Out-Null
    }
    catch {
        Write-Log "Unable to open browser automatically: $($_.Exception.Message)"
    }
}

exit 0
