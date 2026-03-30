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

function New-TempRoot {
    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("ersatztv-launcher-smoke-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
    return $tempRoot
}

function Invoke-Launcher {
    param(
        [string]$Launcher,
        [string]$TempRoot,
        [string]$AppExe
    )

    $output = $null
    $exitCode = 0

    try {
        $output = & powershell -NoProfile -ExecutionPolicy Bypass -File $Launcher -PackageRoot $TempRoot -AppExe $AppExe 2>&1
        $exitCode = $LASTEXITCODE
    }
    catch {
        $output = $_
        $exitCode = if ($LASTEXITCODE) { $LASTEXITCODE } else { 1 }
    }

    return [pscustomobject]@{
        Output = $output | Out-String
        ExitCode = $exitCode
    }
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..\..')
$launcher = Join-Path $repoRoot 'scripts\windows\manual-test\Start-ErsatzTV.ps1'

$tempRoot = New-TempRoot
try {
    $appExe = Join-Path $tempRoot 'app\ErsatzTV.exe'
    $result = Invoke-Launcher -Launcher $launcher -TempRoot $tempRoot -AppExe $appExe
    $logsDir = Join-Path $tempRoot 'runtime\logs'
    $logFiles = @(Get-ChildItem -LiteralPath $logsDir -Filter 'launcher-*.log' -File -ErrorAction SilentlyContinue)

    Assert-True ($result.ExitCode -ne 0) 'launcher should fail when app exe is missing'
    Assert-True ($result.Output -like '*App exe not found*') 'launcher should report missing app exe'
    Assert-True ((Test-Path -LiteralPath $logsDir -PathType Container)) 'launcher should create runtime\logs'
    Assert-True ($logFiles.Count -ge 1) 'launcher should write a launcher log file when app exe is missing'
    Assert-True (((Get-Content -LiteralPath $logFiles[0].FullName) | Out-String) -like '*App exe not found*') 'launcher log should record missing app exe'
    Write-Host 'PASS: launcher fails when app exe is missing and writes logs'
}
finally {
    Remove-Item -Recurse -Force $tempRoot -ErrorAction SilentlyContinue
}

$tempRoot = New-TempRoot
try {
    $appExe = Join-Path $tempRoot 'app\ErsatzTV.exe'
    New-Item -ItemType Directory -Force -Path $appExe | Out-Null

    $result = Invoke-Launcher -Launcher $launcher -TempRoot $tempRoot -AppExe $appExe

    Assert-True ($result.ExitCode -ne 0) 'launcher should fail when app exe path is a directory'
    Assert-True ($result.Output -like '*App exe not found*') 'launcher should treat a directory named ErsatzTV.exe as missing'
    Write-Host 'PASS: launcher rejects a directory named ErsatzTV.exe'
}
finally {
    Remove-Item -Recurse -Force $tempRoot -ErrorAction SilentlyContinue
}
