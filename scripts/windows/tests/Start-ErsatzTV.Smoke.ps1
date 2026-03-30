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
    & powershell -NoProfile -ExecutionPolicy Bypass -File $LauncherInvoker -Launcher $Launcher -PackageRoot $PackageRoot -AppExe $AppExe -FakeServer $FakeServer -Port $Port -UiUrl $UiUrl -ReadyDelayMilliseconds $ReadyDelayMilliseconds -ReadyMarkerPath $ReadyMarkerPath
    $exitCode = $LASTEXITCODE
    $stopwatch.Stop()

    return [pscustomobject]@{
        ExitCode = $exitCode
        ElapsedMilliseconds = [int][Math]::Round($stopwatch.Elapsed.TotalMilliseconds)
    }
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..\..')
$launcher = Join-Path $repoRoot 'scripts\windows\manual-test\Start-ErsatzTV.ps1'
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("ersatztv-launcher-smoke-" + [guid]::NewGuid().ToString('N'))
$appDir = Join-Path $tempRoot 'app'
$runtimeDir = Join-Path $tempRoot 'runtime'
$statePath = Join-Path $runtimeDir 'launcher-state.json'
$fakeServer = Join-Path $tempRoot 'fake-server.ps1'
$launcherInvoker = Join-Path $tempRoot 'invoke-launcher.ps1'
$readyMarker = Join-Path $tempRoot 'ready-marker.txt'
$readyDelayMilliseconds = 3000
$minimumObservedWaitMilliseconds = 2000
New-Item -ItemType Directory -Force -Path $appDir | Out-Null

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

try {
    $firstRun = Invoke-LauncherAndMeasure -LauncherInvoker $launcherInvoker -Launcher $launcher -PackageRoot $tempRoot -AppExe "$PSHOME\\powershell.exe" -FakeServer $fakeServer -Port $port -UiUrl $url -ReadyDelayMilliseconds $readyDelayMilliseconds -ReadyMarkerPath $readyMarker
    Assert-True ($firstRun.ExitCode -eq 0) 'first launcher run should succeed'
    Assert-True (Test-Path -LiteralPath $readyMarker -PathType Leaf) 'first launcher run should not return before readiness marker exists'
    Assert-True ($firstRun.ElapsedMilliseconds -ge $minimumObservedWaitMilliseconds) 'first launcher run should wait for delayed readiness before returning'

    $firstState = Get-Content $statePath | ConvertFrom-Json
    $firstPid = [int]$firstState.pid
    Assert-True ($firstPid -gt 0) 'state file should contain pid after first run'
    Assert-True ((Get-Process -Id $firstPid -ErrorAction SilentlyContinue) -ne $null) 'first process should be running'

    Remove-Item -LiteralPath $readyMarker -Force

    $secondRun = Invoke-LauncherAndMeasure -LauncherInvoker $launcherInvoker -Launcher $launcher -PackageRoot $tempRoot -AppExe "$PSHOME\\powershell.exe" -FakeServer $fakeServer -Port $port -UiUrl $url -ReadyDelayMilliseconds $readyDelayMilliseconds -ReadyMarkerPath $readyMarker
    Assert-True ($secondRun.ExitCode -eq 0) 'second launcher run should succeed'
    Assert-True (Test-Path -LiteralPath $readyMarker -PathType Leaf) 'second launcher run should not return before readiness marker exists'
    Assert-True ($secondRun.ElapsedMilliseconds -ge $minimumObservedWaitMilliseconds) 'second launcher run should wait for delayed readiness before returning'

    Start-Sleep -Seconds 2

    $secondState = Get-Content $statePath | ConvertFrom-Json
    $secondPid = [int]$secondState.pid
    Assert-True ($secondPid -gt 0) 'state file should contain pid after restart'
    Assert-True ($secondPid -ne $firstPid) 'restart should replace the old process with a new pid'
    Assert-True ((Get-Process -Id $secondPid -ErrorAction SilentlyContinue) -ne $null) 'second process should be running'
    Assert-True ((Get-Process -Id $firstPid -ErrorAction SilentlyContinue) -eq $null) 'first process should be stopped after restart'

    Write-Host 'PASS: launcher restarts old instance and waits for delayed readiness'
}
finally {
    if (Test-Path $statePath) {
        $state = Get-Content $statePath | ConvertFrom-Json
        if ($state.pid) {
            Stop-Process -Id $state.pid -Force -ErrorAction SilentlyContinue
        }
    }
    Remove-Item -Recurse -Force $tempRoot -ErrorAction SilentlyContinue
}
