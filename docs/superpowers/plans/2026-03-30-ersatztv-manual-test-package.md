# ErsatzTV Manual Test Package Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a Windows self-contained manual-test zip package that users can unzip and double-click to restart ErsatzTV and automatically open the browser at `http://localhost:8409`.

**Architecture:** Keep the app itself as a self-contained `dotnet publish` output under `app/`, and add a lightweight top-level launcher (`.cmd` + PowerShell) that owns restart detection, readiness polling, and browser opening. Package assembly lives in a dedicated Windows publish script that can either run real `dotnet publish` or accept injected fake publish directories for fast layout testing.

**Tech Stack:** PowerShell 5+, Windows CMD, .NET 10 self-contained publish (`win-x64`), existing ErsatzTV project structure.

---

## File Structure

### Source files to create

- `scripts/windows/manual-test/Start-ErsatzTV.ps1` — reusable launcher logic; restart old instance, start new instance, wait for readiness, open browser, and write runtime state/logs.
- `scripts/windows/manual-test/启动 ErsatzTV.cmd` — double-click entry point that invokes the PowerShell launcher with execution-policy bypass.
- `scripts/windows/manual-test/README-手测说明.txt` — short packaged instructions for human testers.
- `scripts/windows/Publish-ManualTestPackage.ps1` — builds the self-contained `win-x64` package, merges scanner + main outputs into `app/`, copies launcher assets, and zips the package.
- `scripts/windows/tests/Start-ErsatzTV.Smoke.ps1` — smoke tests the launcher with a fake HTTP server and verifies restart semantics.
- `scripts/windows/tests/Publish-ManualTestPackage.Layout.ps1` — fast layout/zip verification using injected fake publish directories.

### Existing files to read but not modify unless necessary

- `.github/workflows/artifacts.yml` — current CI packaging reference.
- `openclaw-skills/ersatztv-source-build/scripts/build_ersatztv.ps1` — existing local helper showing the temporary Scanner-reference removal pattern.
- `ErsatzTV/ErsatzTV.csproj` — main web project.
- `ErsatzTV.Core/SystemEnvironment.cs` — confirms default port behavior (`8409`).

### Build outputs (generated, not committed)

- `build/manual-test-package/<package-name>/...`
- `build/manual-test-package/<package-name>.zip`

---

### Task 1: Add launcher assets and the first failing smoke test

**Files:**
- Create: `scripts/windows/manual-test/Start-ErsatzTV.ps1`
- Create: `scripts/windows/manual-test/启动 ErsatzTV.cmd`
- Create: `scripts/windows/manual-test/README-手测说明.txt`
- Create: `scripts/windows/tests/Start-ErsatzTV.Smoke.ps1`

- [ ] **Step 1: Write the failing smoke test for missing app validation**

Create `scripts/windows/tests/Start-ErsatzTV.Smoke.ps1` with this initial test body:

```powershell
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

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..\..')
$launcher = Join-Path $repoRoot 'scripts\windows\manual-test\Start-ErsatzTV.ps1'
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("ersatztv-launcher-smoke-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null

try {
    $failed = $false

    try {
        & powershell -NoProfile -ExecutionPolicy Bypass -File $launcher -PackageRoot $tempRoot -AppExe (Join-Path $tempRoot 'app\ErsatzTV.exe') -OpenBrowser:$false
        if ($LASTEXITCODE -eq 0) {
            throw 'launcher unexpectedly succeeded without an app exe'
        }
    }
    catch {
        $failed = $true
    }

    Assert-True $failed 'launcher should fail when app exe is missing'
    Write-Host 'PASS: launcher fails when app exe is missing'
}
finally {
    Remove-Item -Recurse -Force $tempRoot -ErrorAction SilentlyContinue
}
```

- [ ] **Step 2: Run the smoke test to verify it fails**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File D:\project\ErsatzTV\scripts\windows\tests\Start-ErsatzTV.Smoke.ps1
```

Expected: FAIL because `Start-ErsatzTV.ps1` does not exist yet.

- [ ] **Step 3: Write the minimal launcher, CMD entry point, and README**

Create `scripts/windows/manual-test/Start-ErsatzTV.ps1`:

```powershell
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

Write-Log "PackageRoot=$PackageRoot"
Write-Log "AppExe=$AppExe"
Write-Log "UiUrl=$UiUrl"

if (-not (Test-Path $AppExe)) {
    Write-Log 'App exe not found'
    throw "App exe not found: $AppExe"
}

Write-Log 'Launcher validation passed'
exit 0
```

Create `scripts/windows/manual-test/启动 ErsatzTV.cmd`:

```bat
@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-ErsatzTV.ps1"
```

Create `scripts/windows/manual-test/README-手测说明.txt`:

```text
ErsatzTV 手测包

1. 双击“启动 ErsatzTV.cmd”
2. 启动器会自动拉起程序并打开浏览器
3. 默认地址：http://localhost:8409
4. 如果浏览器没有自动打开，请手动访问上面的地址
5. 启动器日志位于 runtime\logs
```

- [ ] **Step 4: Run the smoke test to verify it passes**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File D:\project\ErsatzTV\scripts\windows\tests\Start-ErsatzTV.Smoke.ps1
```

Expected: PASS with `PASS: launcher fails when app exe is missing`.

- [ ] **Step 5: Commit**

```bash
git -C D:\project\ErsatzTV add scripts/windows/manual-test/Start-ErsatzTV.ps1 scripts/windows/manual-test/启动\ ErsatzTV.cmd scripts/windows/manual-test/README-手测说明.txt scripts/windows/tests/Start-ErsatzTV.Smoke.ps1
git -C D:\project\ErsatzTV commit -m "feat: add manual test launcher skeleton"
```

### Task 2: Implement restart + readiness polling + browser opening in the launcher

**Files:**
- Modify: `scripts/windows/manual-test/Start-ErsatzTV.ps1`
- Modify: `scripts/windows/tests/Start-ErsatzTV.Smoke.ps1`

- [ ] **Step 1: Expand the smoke test to prove restart behavior against a fake HTTP server**

Replace `scripts/windows/tests/Start-ErsatzTV.Smoke.ps1` with this version:

```powershell
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

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..\..')
$launcher = Join-Path $repoRoot 'scripts\windows\manual-test\Start-ErsatzTV.ps1'
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("ersatztv-launcher-smoke-" + [guid]::NewGuid().ToString('N'))
$appDir = Join-Path $tempRoot 'app'
$runtimeDir = Join-Path $tempRoot 'runtime'
$statePath = Join-Path $runtimeDir 'launcher-state.json'
$fakeServer = Join-Path $tempRoot 'fake-server.ps1'
New-Item -ItemType Directory -Force -Path $appDir | Out-Null

$port = Get-FreePort
$url = "http://localhost:$port/"

@"
param([int]4Port)
Add-Type -AssemblyName System.Net.HttpListener
4listener = [System.Net.HttpListener]::new()
4listener.Prefixes.Add("http://localhost:4Port/")
4listener.Start()
try {
    while (4true) {
        4context = 4listener.GetContext()
        4buffer = [System.Text.Encoding]::UTF8.GetBytes('ok')
        4context.Response.StatusCode = 200
        4context.Response.OutputStream.Write(4buffer, 0, 4buffer.Length)
        4context.Response.Close()
    }
}
finally {
    4listener.Stop()
}
"@ | Set-Content -Path $fakeServer -Encoding UTF8

try {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $launcher -PackageRoot $tempRoot -AppExe "$PSHOME\powershell.exe" -AppArgs @('-NoProfile','-ExecutionPolicy','Bypass','-File',$fakeServer,'-Port',$port) -UiUrl $url -OpenBrowser:$false
    Assert-True ($LASTEXITCODE -eq 0) 'first launcher run should succeed'

    $firstState = Get-Content $statePath | ConvertFrom-Json
    $firstPid = [int]$firstState.pid
    Assert-True ($firstPid -gt 0) 'state file should contain pid after first run'
    Assert-True ((Get-Process -Id $firstPid -ErrorAction SilentlyContinue) -ne $null) 'first process should be running'

    & powershell -NoProfile -ExecutionPolicy Bypass -File $launcher -PackageRoot $tempRoot -AppExe "$PSHOME\powershell.exe" -AppArgs @('-NoProfile','-ExecutionPolicy','Bypass','-File',$fakeServer,'-Port',$port) -UiUrl $url -OpenBrowser:$false
    Assert-True ($LASTEXITCODE -eq 0) 'second launcher run should succeed'

    Start-Sleep -Seconds 2

    $secondState = Get-Content $statePath | ConvertFrom-Json
    $secondPid = [int]$secondState.pid
    Assert-True ($secondPid -gt 0) 'state file should contain pid after restart'
    Assert-True ($secondPid -ne $firstPid) 'restart should replace the old process with a new pid'
    Assert-True ((Get-Process -Id $secondPid -ErrorAction SilentlyContinue) -ne $null) 'second process should be running'
    Assert-True ((Get-Process -Id $firstPid -ErrorAction SilentlyContinue) -eq $null) 'first process should be stopped after restart'

    Write-Host 'PASS: launcher restarts old instance and waits for readiness'
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
```

- [ ] **Step 2: Run the smoke test to verify it fails on missing restart logic**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File D:\project\ErsatzTV\scripts\windows\tests\Start-ErsatzTV.Smoke.ps1
```

Expected: FAIL because the launcher does not yet create a state file, restart an old process, or wait for HTTP readiness.

- [ ] **Step 3: Implement full launcher behavior**

Replace `scripts/windows/manual-test/Start-ErsatzTV.ps1` with this version:

```powershell
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
```

- [ ] **Step 4: Run the smoke test to verify it passes**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File D:\project\ErsatzTV\scripts\windows\tests\Start-ErsatzTV.Smoke.ps1
```

Expected: PASS with `PASS: launcher restarts old instance and waits for readiness`.

- [ ] **Step 5: Commit**

```bash
git -C D:\project\ErsatzTV add scripts/windows/manual-test/Start-ErsatzTV.ps1 scripts/windows/tests/Start-ErsatzTV.Smoke.ps1
git -C D:\project\ErsatzTV commit -m "feat: implement manual test launcher restart flow"
```

### Task 3: Build the manual-test publish/zip pipeline and verify package layout

**Files:**
- Create: `scripts/windows/Publish-ManualTestPackage.ps1`
- Create: `scripts/windows/tests/Publish-ManualTestPackage.Layout.ps1`
- Reuse: `scripts/windows/manual-test/Start-ErsatzTV.ps1`
- Reuse: `scripts/windows/manual-test/启动 ErsatzTV.cmd`
- Reuse: `scripts/windows/manual-test/README-手测说明.txt`

- [ ] **Step 1: Write the failing layout test for the package builder**

Create `scripts/windows/tests/Publish-ManualTestPackage.Layout.ps1`:

```powershell
$ErrorActionPreference = 'Stop'

function Assert-Exists {
    param([string]$Path)
    if (-not (Test-Path $Path)) {
        throw "Expected path to exist: $Path"
    }
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..\..')
$script = Join-Path $repoRoot 'scripts\windows\Publish-ManualTestPackage.ps1'
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("ersatztv-package-layout-" + [guid]::NewGuid().ToString('N'))
$fakeMain = Join-Path $tempRoot 'fake-main'
$fakeScanner = Join-Path $tempRoot 'fake-scanner'
$outRoot = Join-Path $tempRoot 'out'

New-Item -ItemType Directory -Force -Path $fakeMain, $fakeScanner | Out-Null
'fake-main' | Set-Content -Path (Join-Path $fakeMain 'ErsatzTV.exe')
'fake-scanner' | Set-Content -Path (Join-Path $fakeScanner 'ErsatzTV.Scanner.exe')

try {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $script -RepoRoot $repoRoot -OutputRoot $outRoot -PackageName 'ErsatzTV-manual-test-win-x64' -SkipDotnetPublish -PublishedMainDir $fakeMain -PublishedScannerDir $fakeScanner
    if ($LASTEXITCODE -ne 0) {
        throw 'package builder failed'
    }

    $packageDir = Join-Path $outRoot 'ErsatzTV-manual-test-win-x64'
    $zipPath = Join-Path $outRoot 'ErsatzTV-manual-test-win-x64.zip'

    Assert-Exists $packageDir
    Assert-Exists $zipPath
    Assert-Exists (Join-Path $packageDir '启动 ErsatzTV.cmd')
    Assert-Exists (Join-Path $packageDir 'README-手测说明.txt')
    Assert-Exists (Join-Path $packageDir 'Start-ErsatzTV.ps1')
    Assert-Exists (Join-Path $packageDir 'app\ErsatzTV.exe')
    Assert-Exists (Join-Path $packageDir 'app\ErsatzTV.Scanner.exe')

    Write-Host 'PASS: package builder creates the expected layout and zip'
}
finally {
    Remove-Item -Recurse -Force $tempRoot -ErrorAction SilentlyContinue
}
```

- [ ] **Step 2: Run the layout test to verify it fails before the builder exists**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File D:\project\ErsatzTV\scripts\windows\tests\Publish-ManualTestPackage.Layout.ps1
```

Expected: FAIL because `Publish-ManualTestPackage.ps1` does not exist yet.

- [ ] **Step 3: Implement the package builder**

Create `scripts/windows/Publish-ManualTestPackage.ps1`:

```powershell
param(
    [string]$RepoRoot = 'D:\project\ErsatzTV',
    [string]$OutputRoot = 'D:\project\ErsatzTV\build\manual-test-package',
    [string]$PackageName = 'ErsatzTV-manual-test-win-x64',
    [switch]$SkipDotnetPublish,
    [string]$PublishedMainDir = '',
    [string]$PublishedScannerDir = ''
)

$ErrorActionPreference = 'Stop'

function Remove-ScannerReference {
    param(
        [string]$CsprojPath,
        [string]$BackupPath
    )

    Copy-Item -Force $CsprojPath $BackupPath
    $content = Get-Content -Raw $CsprojPath
    $targetLine = '        <ProjectReference Include="..\ErsatzTV.Scanner\ErsatzTV.Scanner.csproj" />'
    $updated = $content.Replace($targetLine + "`r`n", '').Replace($targetLine + "`n", '').Replace($targetLine, '')
    Set-Content -Path $CsprojPath -Value $updated -Encoding UTF8
}

$RepoRoot = (Resolve-Path $RepoRoot).Path
$mainCsproj = Join-Path $RepoRoot 'ErsatzTV\ErsatzTV.csproj'
$scannerCsproj = Join-Path $RepoRoot 'ErsatzTV.Scanner\ErsatzTV.Scanner.csproj'
$assetsDir = Join-Path $RepoRoot 'scripts\windows\manual-test'
$stagingRoot = Join-Path $OutputRoot '_staging'
$packageDir = Join-Path $OutputRoot $PackageName
$zipPath = Join-Path $OutputRoot ($PackageName + '.zip')

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
Remove-Item -Recurse -Force $stagingRoot, $packageDir -ErrorAction SilentlyContinue
Remove-Item -Force $zipPath -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $stagingRoot, $packageDir, (Join-Path $packageDir 'app') | Out-Null

if (-not $SkipDotnetPublish) {
    $backupPath = Join-Path $stagingRoot 'ErsatzTV.csproj.backup'
    try {
        Remove-ScannerReference -CsprojPath $mainCsproj -BackupPath $backupPath

        $PublishedScannerDir = Join-Path $stagingRoot 'scanner'
        $PublishedMainDir = Join-Path $stagingRoot 'main'

        & dotnet publish $scannerCsproj --framework net10.0 --runtime win-x64 -c Release -o $PublishedScannerDir -p:RestoreEnablePackagePruning=true -p:EnableCompressionInSingleFile=false -p:DebugType=Embedded --self-contained true
        if ($LASTEXITCODE -ne 0) { throw 'scanner publish failed' }

        & dotnet publish $mainCsproj --framework net10.0 --runtime win-x64 -c Release -o $PublishedMainDir -p:RestoreEnablePackagePruning=true -p:EnableCompressionInSingleFile=false -p:DebugType=Embedded --self-contained true
        if ($LASTEXITCODE -ne 0) { throw 'main publish failed' }
    }
    finally {
        if (Test-Path $backupPath) {
            Copy-Item -Force $backupPath $mainCsproj
        }
    }
}

Copy-Item -Recurse -Force (Join-Path $PublishedMainDir '*') (Join-Path $packageDir 'app')
Copy-Item -Recurse -Force (Join-Path $PublishedScannerDir '*') (Join-Path $packageDir 'app')
Copy-Item -Force (Join-Path $assetsDir '启动 ErsatzTV.cmd') $packageDir
Copy-Item -Force (Join-Path $assetsDir 'Start-ErsatzTV.ps1') $packageDir
Copy-Item -Force (Join-Path $assetsDir 'README-手测说明.txt') $packageDir

Compress-Archive -Path (Join-Path $packageDir '*') -DestinationPath $zipPath -Force
Write-Host "Package created: $zipPath"
```

- [ ] **Step 4: Run the layout test to verify it passes**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File D:\project\ErsatzTV\scripts\windows\tests\Publish-ManualTestPackage.Layout.ps1
```

Expected: PASS with `PASS: package builder creates the expected layout and zip`.

- [ ] **Step 5: Commit**

```bash
git -C D:\project\ErsatzTV add scripts/windows/Publish-ManualTestPackage.ps1 scripts/windows/tests/Publish-ManualTestPackage.Layout.ps1
git -C D:\project\ErsatzTV commit -m "feat: add manual test package builder"
```

### Task 4: Produce and verify the real win-x64 manual-test package

**Files:**
- Reuse: `scripts/windows/Publish-ManualTestPackage.ps1`
- Reuse: `scripts/windows/manual-test/Start-ErsatzTV.ps1`
- Reuse: `scripts/windows/tests/Start-ErsatzTV.Smoke.ps1`
- Reuse: `scripts/windows/tests/Publish-ManualTestPackage.Layout.ps1`

- [ ] **Step 1: Run the script-based smoke tests before the real publish**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File D:\project\ErsatzTV\scripts\windows\tests\Start-ErsatzTV.Smoke.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File D:\project\ErsatzTV\scripts\windows\tests\Publish-ManualTestPackage.Layout.ps1
```

Expected: both PASS.

- [ ] **Step 2: Build the real self-contained manual-test package**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File D:\project\ErsatzTV\scripts\windows\Publish-ManualTestPackage.ps1 -RepoRoot D:\project\ErsatzTV -OutputRoot D:\project\ErsatzTV\build\manual-test-package -PackageName ErsatzTV-manual-test-win-x64
```

Expected: a zip at `D:\project\ErsatzTV\build\manual-test-package\ErsatzTV-manual-test-win-x64.zip` and an extracted staging directory beside it.

- [ ] **Step 3: Verify the real package contents**

Run:

```powershell
Get-ChildItem D:\project\ErsatzTV\build\manual-test-package\ErsatzTV-manual-test-win-x64 | Select-Object Name
Get-ChildItem D:\project\ErsatzTV\build\manual-test-package\ErsatzTV-manual-test-win-x64\app | Select-Object Name
```

Expected to include:

```text
启动 ErsatzTV.cmd
Start-ErsatzTV.ps1
README-手测说明.txt
app\ErsatzTV.exe
app\ErsatzTV.Scanner.exe
```

- [ ] **Step 4: Manual handoff verification**

Run from an extracted package directory:

```powershell
cd D:\project\ErsatzTV\build\manual-test-package\ErsatzTV-manual-test-win-x64
.\启动` ErsatzTV.cmd
```

Expected:

```text
- Existing launcher-started instance is stopped if present
- A new instance starts from app\ErsatzTV.exe
- Browser opens to http://localhost:8409
- runtime\launcher-state.json is created
- runtime\logs contains a fresh launcher log
```

- [ ] **Step 5: Commit**

```bash
git -C D:\project\ErsatzTV add scripts/windows/manual-test scripts/windows/tests scripts/windows/Publish-ManualTestPackage.ps1
git -C D:\project\ErsatzTV commit -m "feat: ship manual test zip package workflow"
```

## Spec coverage check

- Zip handoff package: covered by Task 3 and Task 4.
- Self-contained `win-x64` publish: covered by Task 3 Step 3 and Task 4 Step 2.
- Top-level double-click launcher: covered by Task 1 and Task 2.
- Force-restart behavior when already running: covered by Task 2.
- Auto-open browser to `http://localhost:8409`: covered by Task 2 and Task 4.
- Human-readable packaged instructions: covered by Task 1 README asset.
- Avoid confusion from direct Debug EXE runs: addressed by package structure + README + launcher.

## Self-review notes

- No `TODO` / `TBD` placeholders remain.
- All paths are repo-relative and concrete.
- The launcher and packager are testable because both accept override parameters for non-production test injection.
- Package output is routed under `build/`, which is already ignored by `.gitignore`.
