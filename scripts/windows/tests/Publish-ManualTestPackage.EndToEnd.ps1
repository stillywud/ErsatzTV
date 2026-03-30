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

function Stop-ProcessIfRunning {
    param([int]$Id)

    if ($Id -le 0) { return }
    $process = Get-Process -Id $Id -ErrorAction SilentlyContinue
    if ($null -ne $process) {
        Stop-Process -Id $Id -Force -ErrorAction SilentlyContinue
    }
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..\..')
$builder = Join-Path $repoRoot 'scripts\windows\Publish-ManualTestPackage.ps1'
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('ersatztv-package-e2e-' + [guid]::NewGuid().ToString('N'))
$fakeMain = Join-Path $tempRoot 'fake-main'
$fakeScanner = Join-Path $tempRoot 'fake-scanner'
$outRoot = Join-Path $tempRoot 'out'
$packageDir = Join-Path $outRoot 'ErsatzTV-manual-test-win-x64'
$launcher = Join-Path $packageDir 'Start-ErsatzTV.ps1'
$launcherInvoker = Join-Path $tempRoot 'invoke-packaged-launcher.ps1'
$readyMarker = Join-Path $packageDir 'ready-marker.txt'
$fakeServer = Join-Path $tempRoot 'fake-server.ps1'
$port = Get-FreePort
$url = "http://localhost:$port/"

New-Item -ItemType Directory -Force -Path $fakeMain, $fakeScanner | Out-Null
Copy-Item -LiteralPath (Join-Path $PSHOME 'powershell.exe') -Destination (Join-Path $fakeMain 'ErsatzTV.exe') -Force
'fake-scanner' | Set-Content -Path (Join-Path $fakeScanner 'ErsatzTV.Scanner.exe') -Encoding ASCII

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
    [string]`$FakeServer,
    [int]`$Port,
    [string]`$UiUrl,
    [string]`$ReadyMarkerPath
)

& `$Launcher -AppArgs @('-NoProfile','-ExecutionPolicy','Bypass','-File',`$FakeServer,'-Port',`$Port,'-ReadyDelayMilliseconds',1500,'-ReadyMarkerPath',`$ReadyMarkerPath) -UiUrl `$UiUrl -OpenBrowser:`$false
"@ | Set-Content -Path $launcherInvoker -Encoding UTF8

try {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $builder -RepoRoot $repoRoot -OutputRoot $outRoot -PackageName 'ErsatzTV-manual-test-win-x64' -SkipDotnetPublish -PublishedMainDir $fakeMain -PublishedScannerDir $fakeScanner
    Assert-True ($LASTEXITCODE -eq 0) 'package builder should succeed with fake publish dirs'
    Assert-True (Test-Path -LiteralPath $launcher -PathType Leaf) 'packaged launcher should exist'

    & powershell -NoProfile -ExecutionPolicy Bypass -File $launcherInvoker -Launcher $launcher -FakeServer $fakeServer -Port $port -UiUrl $url -ReadyMarkerPath $readyMarker
    Assert-True ($LASTEXITCODE -eq 0) 'packaged launcher should succeed from produced package layout'

    $statePath = Join-Path $packageDir 'runtime\launcher-state.json'
    Assert-True (Test-Path -LiteralPath $statePath -PathType Leaf) 'packaged launcher should create launcher-state.json'
    Assert-True (Test-Path -LiteralPath $readyMarker -PathType Leaf) 'packaged launcher should wait until fake server is ready'

    $state = Get-Content -LiteralPath $statePath | ConvertFrom-Json
    $launchedPid = [int]$state.pid
    Assert-True ($launchedPid -gt 0) 'packaged launcher should record a running pid'
    Assert-True ([string]::Equals($state.appExe, (Join-Path $packageDir 'app\ErsatzTV.exe'), [System.StringComparison]::OrdinalIgnoreCase)) 'packaged launcher should resolve AppExe relative to produced package layout'
    Assert-True ((Get-Process -Id $launchedPid -ErrorAction SilentlyContinue) -ne $null) 'packaged launcher should start packaged app executable'

    Write-Host 'PASS: package builder output can launch successfully from the produced package layout'
}
finally {
    $statePath = Join-Path $packageDir 'runtime\launcher-state.json'
    if (Test-Path -LiteralPath $statePath) {
        try {
            $state = Get-Content -LiteralPath $statePath | ConvertFrom-Json
            if ($state.pid) {
                Stop-ProcessIfRunning -Id ([int]$state.pid)
            }
        }
        catch {
        }
    }

    Remove-Item -Recurse -Force $tempRoot -ErrorAction SilentlyContinue
}
