param(
    [string]$RepoRoot = 'D:\project\ErsatzTV',
    [string]$Configuration = 'Release',
    [string]$Runtime = '',
    [switch]$SkipClean,
    [switch]$SkipTests,
    [switch]$PublishWinX64,
    [switch]$PlanOnly,
    [string]$LogsDir = ''
)

$ErrorActionPreference = 'Stop'

function Write-Info([string]$Message) {
    Write-Host "[INFO] $Message"
}

function Write-WarnMsg([string]$Message) {
    Write-Host "[WARN] $Message" -ForegroundColor Yellow
}

function Run-Step {
    param(
        [string]$Name,
        [string[]]$Command,
        [string]$WorkingDirectory,
        [string]$LogPath,
        [switch]$PlanOnlyMode
    )

    $display = ($Command | ForEach-Object {
        if ($_ -match '\s') { '"' + $_ + '"' } else { $_ }
    }) -join ' '

    Write-Info "$Name"
    Write-Host "       cwd: $WorkingDirectory"
    Write-Host "       cmd: $display"
    Write-Host "       log: $LogPath"

    if ($PlanOnlyMode) {
        return
    }

    & $Command[0] $Command[1..($Command.Length - 1)] 2>&1 | Tee-Object -FilePath $LogPath
    if ($LASTEXITCODE -ne 0) {
        throw "Step failed: $Name (exit code $LASTEXITCODE)"
    }
}

function Remove-ScannerReference {
    param(
        [string]$CsprojPath,
        [string]$BackupPath
    )

    Copy-Item -Force $CsprojPath $BackupPath
    $content = Get-Content -Raw $CsprojPath
    $targetLine = '        <ProjectReference Include="..\ErsatzTV.Scanner\ErsatzTV.Scanner.csproj" />'

    if ($content -notmatch [regex]::Escape('..\ErsatzTV.Scanner\ErsatzTV.Scanner.csproj')) {
        Write-WarnMsg 'Scanner project reference line was not found; leaving csproj unchanged.'
        return $false
    }

    $newContent = $content.Replace($targetLine + "`r`n", '').Replace($targetLine + "`n", '').Replace($targetLine, '')
    Set-Content -Path $CsprojPath -Value $newContent -Encoding UTF8
    return $true
}

$RepoRoot = (Resolve-Path $RepoRoot).Path
$SolutionPath = Join-Path $RepoRoot 'ErsatzTV.sln'
$MainCsproj = Join-Path $RepoRoot 'ErsatzTV\ErsatzTV.csproj'
$ScannerCsproj = Join-Path $RepoRoot 'ErsatzTV.Scanner\ErsatzTV.Scanner.csproj'

if (-not (Test-Path $SolutionPath)) {
    throw "Solution not found: $SolutionPath"
}
if (-not (Test-Path $MainCsproj)) {
    throw "Main csproj not found: $MainCsproj"
}
if (-not (Test-Path $ScannerCsproj)) {
    throw "Scanner csproj not found: $ScannerCsproj"
}

$machinePath = [System.Environment]::GetEnvironmentVariable('PATH', 'Machine')
$userPath = [System.Environment]::GetEnvironmentVariable('PATH', 'User')
if ($machinePath) {
    $env:PATH = $machinePath + ';' + $userPath
}

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    throw 'dotnet command not found. Install .NET 10 SDK first.'
}

$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
if ([string]::IsNullOrWhiteSpace($LogsDir)) {
    $LogsDir = Join-Path $RepoRoot (Join-Path 'build-logs' $timestamp)
}
New-Item -ItemType Directory -Force -Path $LogsDir | Out-Null

$backupPath = Join-Path $LogsDir 'ErsatzTV.csproj.backup'
$scannerRemoved = $false

try {
    if ($PlanOnly) {
        Write-Info "Plan-only mode: would temporarily remove Scanner project reference from $MainCsproj"
    }
    else {
        $scannerRemoved = Remove-ScannerReference -CsprojPath $MainCsproj -BackupPath $backupPath
        if ($scannerRemoved) {
            Write-Info "Temporarily removed Scanner project reference from $MainCsproj"
        }
    }

    $cleanCmd = @('dotnet', 'clean', '--configuration', $Configuration)
    $restoreCmd = @('dotnet', 'restore')
    if (-not [string]::IsNullOrWhiteSpace($Runtime)) {
        $restoreCmd += @('-r', $Runtime)
    }
    $buildCmd = @('dotnet', 'build', '--configuration', $Configuration, '--no-restore')
    $testCmd = @('dotnet', 'test', '--blame-hang-timeout', '2m', '--no-restore', '--verbosity', 'normal')

    if (-not $SkipClean) {
        Run-Step -Name 'Clean' -Command $cleanCmd -WorkingDirectory $RepoRoot -LogPath (Join-Path $LogsDir '01-clean.log') -PlanOnlyMode:$PlanOnly
    }

    Run-Step -Name 'Restore' -Command $restoreCmd -WorkingDirectory $RepoRoot -LogPath (Join-Path $LogsDir '02-restore.log') -PlanOnlyMode:$PlanOnly
    Run-Step -Name 'Build' -Command $buildCmd -WorkingDirectory $RepoRoot -LogPath (Join-Path $LogsDir '03-build.log') -PlanOnlyMode:$PlanOnly

    if (-not $SkipTests) {
        Run-Step -Name 'Test' -Command $testCmd -WorkingDirectory $RepoRoot -LogPath (Join-Path $LogsDir '04-test.log') -PlanOnlyMode:$PlanOnly
    }

    if ($PublishWinX64) {
        $publishRoot = Join-Path $RepoRoot 'publish'
        $scannerOut = Join-Path $publishRoot 'scanner'
        $mainOut = Join-Path $publishRoot 'main'
        New-Item -ItemType Directory -Force -Path $scannerOut, $mainOut | Out-Null

        $publishScannerCmd = @(
            'dotnet', 'publish', $ScannerCsproj,
            '--framework', 'net10.0', '--runtime', 'win-x64',
            '-c', $Configuration,
            '-o', $scannerOut,
            '-p:RestoreEnablePackagePruning=true',
            '-p:EnableCompressionInSingleFile=true',
            '-p:DebugType=Embedded',
            '-p:PublishSingleFile=true',
            '--self-contained', 'true'
        )

        $publishMainCmd = @(
            'dotnet', 'publish', $MainCsproj,
            '--framework', 'net10.0', '--runtime', 'win-x64',
            '-c', $Configuration,
            '-o', $mainOut,
            '-p:RestoreEnablePackagePruning=true',
            '-p:EnableCompressionInSingleFile=true',
            '-p:DebugType=Embedded',
            '-p:PublishSingleFile=true',
            '--self-contained', 'true'
        )

        Run-Step -Name 'Publish Scanner (win-x64)' -Command $publishScannerCmd -WorkingDirectory $RepoRoot -LogPath (Join-Path $LogsDir '05-publish-scanner.log') -PlanOnlyMode:$PlanOnly
        Run-Step -Name 'Publish Main (win-x64)' -Command $publishMainCmd -WorkingDirectory $RepoRoot -LogPath (Join-Path $LogsDir '06-publish-main.log') -PlanOnlyMode:$PlanOnly
    }

    Write-Info "Completed. Logs directory: $LogsDir"
}
finally {
    if (Test-Path $backupPath) {
        Copy-Item -Force $backupPath $MainCsproj
        Write-Info 'Restored original ErsatzTV.csproj from backup.'
    }
}
