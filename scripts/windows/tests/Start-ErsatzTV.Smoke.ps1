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
    $output = $null

    try {
        $output = & powershell -NoProfile -ExecutionPolicy Bypass -File $launcher -PackageRoot $tempRoot -AppExe (Join-Path $tempRoot 'app\ErsatzTV.exe') 2>&1
    }
    catch {
        $output = $_
    }

    Assert-True ($LASTEXITCODE -ne 0) 'launcher should fail when app exe is missing'
    Assert-True (($output | Out-String) -like '*App exe not found*') 'launcher should report missing app exe'
    Write-Host 'PASS: launcher fails when app exe is missing'
}
finally {
    Remove-Item -Recurse -Force $tempRoot -ErrorAction SilentlyContinue
}
