$ErrorActionPreference = 'Stop'

function Assert-Exists {
    param([string]$Path)
    if (-not (Test-Path $Path)) {
        throw "Expected path to exist: $Path"
    }
}

function Assert-Contains {
    param(
        [string[]]$Items,
        [string]$Expected
    )

    if ($Items -notcontains $Expected) {
        throw "Expected collection to contain '$Expected'. Actual entries: $($Items -join ', ')"
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
'main-wins' | Set-Content -Path (Join-Path $fakeMain 'SharedDependency.dll')
'scanner-loses' | Set-Content -Path (Join-Path $fakeScanner 'SharedDependency.dll')

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

    $sharedDependencyPath = Join-Path $packageDir 'app\SharedDependency.dll'
    Assert-Exists $sharedDependencyPath
    $sharedDependencyContent = (Get-Content -Path $sharedDependencyPath -Raw).Trim()
    if ($sharedDependencyContent -ne 'main-wins') {
        throw "Expected main publish output to win duplicate-file copy order, got: $sharedDependencyContent"
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $entries = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
        Assert-Contains -Items $entries -Expected '启动 ErsatzTV.cmd'
        Assert-Contains -Items $entries -Expected 'README-手测说明.txt'
        Assert-Contains -Items $entries -Expected 'Start-ErsatzTV.ps1'
        Assert-Contains -Items $entries -Expected 'app/ErsatzTV.exe'
        Assert-Contains -Items $entries -Expected 'app/ErsatzTV.Scanner.exe'
    }
    finally {
        $archive.Dispose()
    }

    Write-Host 'PASS: package builder creates the expected layout and zip contents'
}
finally {
    Remove-Item -Recurse -Force $tempRoot -ErrorAction SilentlyContinue
}
