$ErrorActionPreference = 'Stop'

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Get-FreePort {
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $port = ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
    $listener.Stop()
    return $port
}

function Invoke-Launcher {
    param(
        [string]$Launcher,
        [string]$PackageRoot,
        [string]$AppExe,
        [string]$UiUrl = 'http://localhost:8409/',
        [int]$ReadyTimeoutSeconds = 90
    )

    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & powershell -NoProfile -ExecutionPolicy Bypass -File $Launcher -PackageRoot $PackageRoot -AppExe $AppExe -UiUrl $UiUrl -ReadyTimeoutSeconds $ReadyTimeoutSeconds 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    return [pscustomobject]@{
        ExitCode = $exitCode
        Output = @($output)
    }
}

function Invoke-LauncherAndMeasure {
    param(
        [string]$LauncherInvoker,
        [string]$Launcher,
        [string]$PackageRoot,
        [string]$AppExe,
        [string]$FakeServer,
        [int]$Port,
        [string]$UiUrl,
        [int]$ReadyDelayMilliseconds,
        [string]$ReadyMarkerPath
    )

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $output = & powershell -NoProfile -ExecutionPolicy Bypass -File $LauncherInvoker -Launcher $Launcher -PackageRoot $PackageRoot -AppExe $AppExe -FakeServer $FakeServer -Port $Port -UiUrl $UiUrl -ReadyDelayMilliseconds $ReadyDelayMilliseconds -ReadyMarkerPath $ReadyMarkerPath 2>&1
    $exitCode = $LASTEXITCODE
    $stopwatch.Stop()

    return [pscustomobject]@{
        ExitCode = $exitCode
        Output = @($output)
        ElapsedMilliseconds = [int][Math]::Round($stopwatch.Elapsed.TotalMilliseconds)
    }
}

function Stop-ProcessIfRunning {
    param([int]$Id)

    if ($Id -le 0) {
        return
    }

    $process = Get-Process -Id $Id -ErrorAction SilentlyContinue
    if ($null -ne $process) {
        Stop-Process -Id $Id -Force -ErrorAction SilentlyContinue
    }
}

function Get-LauncherLogContent {
    param([string]$LogsDir)

    if (-not (Test-Path -LiteralPath $LogsDir -PathType Container)) {
        return ''
    }

    return ((Get-ChildItem -Path $LogsDir -Filter 'launcher-*.log' | Sort-Object LastWriteTime, Name | ForEach-Object {
        Get-Content -Path $_.FullName -Raw
    }) -join "`n")
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..\..')
$launcher = Join-Path $repoRoot 'scripts\windows\manual-test\Start-ErsatzTV.ps1'
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("ersatztv-launcher-smoke-" + [guid]::NewGuid().ToString('N'))
$appDir = Join-Path $tempRoot 'app'
$runtimeDir = Join-Path $tempRoot 'runtime'
$logsDir = Join-Path $runtimeDir 'logs'
$statePath = Join-Path $runtimeDir 'launcher-state.json'
$fakeServer = Join-Path $tempRoot 'fake-server.ps1'
$launcherInvoker = Join-Path $tempRoot 'invoke-launcher.ps1'
$packagedLauncher = Join-Path $tempRoot 'Start-ErsatzTV.ps1'
$packagedLauncherInvoker = Join-Path $tempRoot 'invoke-packaged-launcher.ps1'
$browserFailureInvoker = Join-Path $tempRoot 'invoke-launcher-browser-failure.ps1'
$readyMarker = Join-Path $tempRoot 'ready-marker.txt'
$appExe = Join-Path $PSHOME 'powershell.exe'
$packagedAppExe = Join-Path $appDir 'ErsatzTV.exe'
$readyDelayMilliseconds = 3000
$minimumObservedWaitMilliseconds = 2000
$staleProcess = $null
New-Item -ItemType Directory -Force -Path $appDir | Out-Null
Copy-Item -LiteralPath $launcher -Destination $packagedLauncher -Force
Copy-Item -LiteralPath $appExe -Destination $packagedAppExe -Force

$port = Get-FreePort
$url = "http://localhost:$port/"

@"
param(
    [int]`$Port,
    [int]`$ReadyDelayMilliseconds,
    [string]`$ReadyMarkerPath
)

`$listener = [System.Net.HttpListener]::new()
`$listener.Prefixes.Add("http://localhost:`$Port/")
Start-Sleep -Milliseconds `$ReadyDelayMilliseconds
`$listener.Start()
(Get-Date).ToString('o') | Set-Content -Path `$ReadyMarkerPath -Encoding ASCII
try {
    while (`$true) {
        `$context = `$listener.GetContext()
        `$buffer = [System.Text.Encoding]::UTF8.GetBytes('ok')
        `$context.Response.StatusCode = 200
        `$context.Response.OutputStream.Write(`$buffer, 0, `$buffer.Length)
        `$context.Response.Close()
    }
}
finally {
    if (`$listener.IsListening) {
        `$listener.Stop()
    }
}
"@ | Set-Content -Path $fakeServer -Encoding UTF8

@"
param(
    [string]`$Launcher,
    [string]`$PackageRoot,
    [string]`$AppExe,
    [string]`$FakeServer,
    [int]`$Port,
    [string]`$UiUrl,
    [int]`$ReadyDelayMilliseconds,
    [string]`$ReadyMarkerPath
)

& `$Launcher -PackageRoot `$PackageRoot -AppExe `$AppExe -AppArgs @('-NoProfile','-ExecutionPolicy','Bypass','-File',`$FakeServer,'-Port',`$Port,'-ReadyDelayMilliseconds',`$ReadyDelayMilliseconds,'-ReadyMarkerPath',`$ReadyMarkerPath) -UiUrl `$UiUrl -OpenBrowser:`$false
"@ | Set-Content -Path $launcherInvoker -Encoding UTF8

@"
param(
    [string]`$Launcher,
    [string]`$FakeServer,
    [int]`$Port,
    [string]`$UiUrl,
    [int]`$ReadyDelayMilliseconds,
    [string]`$ReadyMarkerPath
)

& `$Launcher -AppArgs @('-NoProfile','-ExecutionPolicy','Bypass','-File',`$FakeServer,'-Port',`$Port,'-ReadyDelayMilliseconds',`$ReadyDelayMilliseconds,'-ReadyMarkerPath',`$ReadyMarkerPath) -UiUrl `$UiUrl -OpenBrowser:`$false
"@ | Set-Content -Path $packagedLauncherInvoker -Encoding UTF8

@"
param(
    [string]`$Launcher,
    [string]`$PackageRoot,
    [string]`$AppExe,
    [string]`$FakeServer,
    [int]`$Port,
    [string]`$UiUrl,
    [int]`$ReadyDelayMilliseconds,
    [string]`$ReadyMarkerPath
)

function Start-Process {
    param(
        [Parameter(Position = 0)]
        [string]`$FilePath,
        [Parameter(Position = 1)]
        [object]`$ArgumentList,
        [string]`$WorkingDirectory,
        [switch]`$PassThru
    )

    if (`$FilePath -like 'http*') {
        throw 'simulated browser launch failure'
    }

    Microsoft.PowerShell.Management\Start-Process @PSBoundParameters
}

& `$Launcher -PackageRoot `$PackageRoot -AppExe `$AppExe -AppArgs @('-NoProfile','-ExecutionPolicy','Bypass','-File',`$FakeServer,'-Port',`$Port,'-ReadyDelayMilliseconds',`$ReadyDelayMilliseconds,'-ReadyMarkerPath',`$ReadyMarkerPath) -UiUrl `$UiUrl -OpenBrowser
"@ | Set-Content -Path $browserFailureInvoker -Encoding UTF8

try {
    $invalidAppExeResult = Invoke-Launcher -Launcher $launcher -PackageRoot $tempRoot -AppExe $appDir -UiUrl $url -ReadyTimeoutSeconds 5
    Assert-True ($invalidAppExeResult.ExitCode -ne 0) 'directory AppExe should be rejected'
    Assert-True ((($invalidAppExeResult.Output -join "`n") -match 'App exe not found:')) ("directory AppExe failure should report missing executable. Output:`n{0}" -f ($invalidAppExeResult.Output -join "`n"))
    Assert-True ((Get-ChildItem -Path $logsDir -Filter 'launcher-*.log' -ErrorAction SilentlyContinue | Measure-Object).Count -ge 1) 'directory AppExe failure should create a launcher log file'
    Assert-True ((Get-LauncherLogContent -LogsDir $logsDir) -match 'App exe not found') 'directory AppExe failure log should include missing executable details'

    $packagedRun = Invoke-LauncherAndMeasure -LauncherInvoker $packagedLauncherInvoker -Launcher $packagedLauncher -PackageRoot $tempRoot -AppExe $packagedAppExe -FakeServer $fakeServer -Port $port -UiUrl $url -ReadyDelayMilliseconds $readyDelayMilliseconds -ReadyMarkerPath $readyMarker
    Assert-True ($packagedRun.ExitCode -eq 0) ("packaged launcher should default PackageRoot/AppExe relative to its copied package location. Output:`n{0}" -f ($packagedRun.Output -join "`n"))
    Assert-True (Test-Path -LiteralPath $statePath -PathType Leaf) 'packaged launcher should create launcher-state.json in the copied package runtime directory'
    Assert-True (Test-Path -LiteralPath $readyMarker -PathType Leaf) 'packaged launcher should wait for delayed readiness before returning'
    Assert-True ($packagedRun.ElapsedMilliseconds -ge $minimumObservedWaitMilliseconds) 'packaged launcher should wait for delayed readiness before returning'

    $packagedState = Get-Content $statePath | ConvertFrom-Json
    $packagedPid = [int]$packagedState.pid
    Assert-True ($packagedPid -gt 0) 'packaged launcher should record a running pid'
    Assert-True ((Get-Process -Id $packagedPid -ErrorAction SilentlyContinue) -ne $null) 'packaged launcher should start the packaged app executable'
    Assert-True ([string]::Equals($packagedState.appExe, (Resolve-Path -LiteralPath $packagedAppExe).Path, [System.StringComparison]::OrdinalIgnoreCase)) 'packaged launcher should record the packaged app executable path'

    Stop-ProcessIfRunning -Id $packagedPid
    Start-Sleep -Seconds 1
    Remove-Item -LiteralPath $readyMarker -Force

    '{"pid": 123,' | Set-Content -Path $statePath -Encoding UTF8

    $corruptStateRun = Invoke-LauncherAndMeasure -LauncherInvoker $launcherInvoker -Launcher $launcher -PackageRoot $tempRoot -AppExe $appExe -FakeServer $fakeServer -Port $port -UiUrl $url -ReadyDelayMilliseconds $readyDelayMilliseconds -ReadyMarkerPath $readyMarker
    Assert-True ($corruptStateRun.ExitCode -eq 0) ("launcher should ignore invalid state file and still start. Output:`n{0}" -f ($corruptStateRun.Output -join "`n"))
    Assert-True (Test-Path -LiteralPath $readyMarker -PathType Leaf) 'launcher should still wait for readiness when recorded state file is invalid'
    Assert-True ($corruptStateRun.ElapsedMilliseconds -ge $minimumObservedWaitMilliseconds) 'launcher should still wait for delayed readiness when recorded state file is invalid'
    Assert-True ((Get-LauncherLogContent -LogsDir $logsDir) -match 'Ignoring invalid launcher state file') 'invalid state file should be logged and ignored'

    $corruptState = Get-Content $statePath | ConvertFrom-Json
    $corruptStatePid = [int]$corruptState.pid
    Assert-True ($corruptStatePid -gt 0) 'state file should be rewritten after ignoring invalid prior state'
    Assert-True ((Get-Process -Id $corruptStatePid -ErrorAction SilentlyContinue) -ne $null) 'process started after invalid state should be running'

    Stop-ProcessIfRunning -Id $corruptStatePid
    Start-Sleep -Seconds 1
    if (Test-Path -LiteralPath $readyMarker -PathType Leaf) {
        Remove-Item -LiteralPath $readyMarker -Force
    }

    @{
        pid = 'abc'
        appExe = 'C:\fake\ErsatzTV.exe'
        uiUrl = $url
        startedAt = '2026-03-30T00:00:00Z'
    } | ConvertTo-Json | Set-Content -Path $statePath -Encoding UTF8

    $schemaInvalidStateRun = Invoke-LauncherAndMeasure -LauncherInvoker $launcherInvoker -Launcher $launcher -PackageRoot $tempRoot -AppExe $appExe -FakeServer $fakeServer -Port $port -UiUrl $url -ReadyDelayMilliseconds $readyDelayMilliseconds -ReadyMarkerPath $readyMarker
    Assert-True ($schemaInvalidStateRun.ExitCode -eq 0) ("launcher should ignore schema-invalid state file and still start. Output:`n{0}" -f ($schemaInvalidStateRun.Output -join "`n"))
    Assert-True (Test-Path -LiteralPath $readyMarker -PathType Leaf) 'launcher should still wait for readiness when recorded state file schema is invalid'
    Assert-True ($schemaInvalidStateRun.ElapsedMilliseconds -ge $minimumObservedWaitMilliseconds) 'launcher should still wait for delayed readiness when recorded state file schema is invalid'
    Assert-True ((Get-LauncherLogContent -LogsDir $logsDir) -match 'Ignoring invalid launcher state file') 'schema-invalid state file should be logged and ignored'

    $schemaInvalidState = Get-Content $statePath | ConvertFrom-Json
    $schemaInvalidStatePid = [int]$schemaInvalidState.pid
    Assert-True ($schemaInvalidStatePid -gt 0) 'state file should be rewritten after ignoring schema-invalid prior state'
    Assert-True ((Get-Process -Id $schemaInvalidStatePid -ErrorAction SilentlyContinue) -ne $null) 'process started after schema-invalid state should be running'

    Stop-ProcessIfRunning -Id $schemaInvalidStatePid
    Start-Sleep -Seconds 1
    if (Test-Path -LiteralPath $readyMarker -PathType Leaf) {
        Remove-Item -LiteralPath $readyMarker -Force
    }

    $staleProcess = Start-Process -FilePath $appExe -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-Command','Start-Sleep -Seconds 60') -PassThru
    Assert-True ((Get-Process -Id $staleProcess.Id -ErrorAction SilentlyContinue) -ne $null) 'stale test process should be running before launcher start'

    @{
        pid = $staleProcess.Id
        appExe = $appExe
        uiUrl = $url
        startedAt = (Get-Date).AddHours(-1).ToString('o')
    } | ConvertTo-Json | Set-Content -Path $statePath -Encoding UTF8

    $firstRun = Invoke-LauncherAndMeasure -LauncherInvoker $launcherInvoker -Launcher $launcher -PackageRoot $tempRoot -AppExe $appExe -FakeServer $fakeServer -Port $port -UiUrl $url -ReadyDelayMilliseconds $readyDelayMilliseconds -ReadyMarkerPath $readyMarker
    Assert-True ($firstRun.ExitCode -eq 0) ("first launcher run should succeed. Output:`n{0}" -f ($firstRun.Output -join "`n"))
    Assert-True ((Get-Process -Id $staleProcess.Id -ErrorAction SilentlyContinue) -ne $null) 'launcher should not stop a stale recorded pid when identity does not match'
    Assert-True (Test-Path -LiteralPath $readyMarker -PathType Leaf) 'first launcher run should not return before readiness marker exists'
    Assert-True ($firstRun.ElapsedMilliseconds -ge $minimumObservedWaitMilliseconds) 'first launcher run should wait for delayed readiness before returning'

    $firstState = Get-Content $statePath | ConvertFrom-Json
    $firstPid = [int]$firstState.pid
    Assert-True ($firstPid -gt 0) 'state file should contain pid after first run'
    Assert-True ((Get-Process -Id $firstPid -ErrorAction SilentlyContinue) -ne $null) 'first process should be running'

    Remove-Item -LiteralPath $readyMarker -Force

    $secondRun = Invoke-LauncherAndMeasure -LauncherInvoker $launcherInvoker -Launcher $launcher -PackageRoot $tempRoot -AppExe $appExe -FakeServer $fakeServer -Port $port -UiUrl $url -ReadyDelayMilliseconds $readyDelayMilliseconds -ReadyMarkerPath $readyMarker
    Assert-True ($secondRun.ExitCode -eq 0) ("second launcher run should succeed. Output:`n{0}" -f ($secondRun.Output -join "`n"))
    Assert-True (Test-Path -LiteralPath $readyMarker -PathType Leaf) 'second launcher run should not return before readiness marker exists'
    Assert-True ($secondRun.ElapsedMilliseconds -ge $minimumObservedWaitMilliseconds) 'second launcher run should wait for delayed readiness before returning'

    Start-Sleep -Seconds 2

    $secondState = Get-Content $statePath | ConvertFrom-Json
    $secondPid = [int]$secondState.pid
    Assert-True ($secondPid -gt 0) 'state file should contain pid after restart'
    Assert-True ($secondPid -ne $firstPid) 'restart should replace the old process with a new pid'
    Assert-True ((Get-Process -Id $secondPid -ErrorAction SilentlyContinue) -ne $null) 'second process should be running'
    Assert-True ((Get-Process -Id $firstPid -ErrorAction SilentlyContinue) -eq $null) 'first process should be stopped after restart'

    Stop-ProcessIfRunning -Id $secondPid
    Start-Sleep -Seconds 1
    Remove-Item -LiteralPath $readyMarker -Force

    $browserFailureRun = Invoke-LauncherAndMeasure -LauncherInvoker $browserFailureInvoker -Launcher $launcher -PackageRoot $tempRoot -AppExe $appExe -FakeServer $fakeServer -Port $port -UiUrl $url -ReadyDelayMilliseconds $readyDelayMilliseconds -ReadyMarkerPath $readyMarker
    Assert-True ($browserFailureRun.ExitCode -eq 0) ("launcher should still succeed when browser launch fails. Output:`n{0}" -f ($browserFailureRun.Output -join "`n"))
    Assert-True (Test-Path -LiteralPath $readyMarker -PathType Leaf) 'browser failure run should still wait for readiness before returning'
    Assert-True ($browserFailureRun.ElapsedMilliseconds -ge $minimumObservedWaitMilliseconds) 'browser failure run should still wait for delayed readiness before returning'
    Assert-True ((Get-LauncherLogContent -LogsDir $logsDir) -match 'Unable to open browser automatically') 'browser failure should be logged and ignored'

    $browserFailureState = Get-Content $statePath | ConvertFrom-Json
    $browserFailurePid = [int]$browserFailureState.pid
    Assert-True ($browserFailurePid -gt 0) 'state file should contain pid after browser-failure run'
    Assert-True ((Get-Process -Id $browserFailurePid -ErrorAction SilentlyContinue) -ne $null) 'process should still be running after browser-failure run'

    Write-Host 'PASS: launcher rejects directory AppExe, logs failure details, ignores invalid state files, skips stale recorded pid reuse, restarts old instance, tolerates browser launch failure, and waits for delayed readiness'
}
finally {
    if (Test-Path $statePath) {
        try {
            $state = Get-Content $statePath | ConvertFrom-Json
            if ($state.pid) {
                Stop-ProcessIfRunning -Id ([int]$state.pid)
            }
        }
        catch {
        }
    }

    if ($null -ne $staleProcess) {
        Stop-ProcessIfRunning -Id $staleProcess.Id
    }

    Remove-Item -Recurse -Force $tempRoot -ErrorAction SilentlyContinue
}
