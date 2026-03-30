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
$readyMarker = Join-Path $packageDir 'ready-marker.txt'
$port = Get-FreePort
$url = "http://localhost:$port/"

New-Item -ItemType Directory -Force -Path $fakeMain, $fakeScanner | Out-Null
'fake-scanner' | Set-Content -Path (Join-Path $fakeScanner 'ErsatzTV.Scanner.exe') -Encoding ASCII

$fakeMainSource = @"
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

public static class Program
{
    public static void Main(string[] args)
    {
        int port = int.Parse(args[0]);
        int readyDelayMilliseconds = int.Parse(args[1]);
        string readyMarkerPath = args[2];

        using (var listener = new HttpListener())
        {
            listener.Prefixes.Add(string.Format("http://localhost:{0}/", port));
            Thread.Sleep(readyDelayMilliseconds);
            listener.Start();
            File.WriteAllText(readyMarkerPath, DateTime.UtcNow.ToString("o"));

            while (true)
            {
                var context = listener.GetContext();
                byte[] buffer = Encoding.UTF8.GetBytes("ok");
                context.Response.StatusCode = 200;
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                context.Response.Close();
            }
        }
    }
}
"@
Add-Type -TypeDefinition $fakeMainSource -Language CSharp -OutputAssembly (Join-Path $fakeMain 'ErsatzTV.exe') -OutputType ConsoleApplication

try {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $builder -RepoRoot $repoRoot -OutputRoot $outRoot -PackageName 'ErsatzTV-manual-test-win-x64' -SkipDotnetPublish -PublishedMainDir $fakeMain -PublishedScannerDir $fakeScanner
    Assert-True ($LASTEXITCODE -eq 0) 'package builder should succeed with fake publish dirs'
    Assert-True (Test-Path -LiteralPath $launcher -PathType Leaf) 'packaged launcher should exist'

    & $launcher -AppArgs @($port, 1500, $readyMarker) -UiUrl $url -OpenBrowser:$false
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
