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

    if (-not $content.Contains($targetLine)) {
        throw "Expected scanner ProjectReference not found in $CsprojPath"
    }

    $updated = $content.Replace($targetLine + "`r`n", '').Replace($targetLine + "`n", '').Replace($targetLine, '')
    Set-Content -Path $CsprojPath -Value $updated -Encoding UTF8
}

function Resolve-ManualTestAsset {
    param(
        [string]$AssetsDir,
        [string]$ExpectedName,
        [string]$FallbackFilter
    )

    $expectedPath = Join-Path $AssetsDir $ExpectedName
    if (Test-Path -LiteralPath $expectedPath -PathType Leaf) {
        return $expectedPath
    }

    $matches = @(Get-ChildItem -LiteralPath $AssetsDir -Filter $FallbackFilter -File)
    if ($matches.Count -eq 1) {
        return $matches[0].FullName
    }

    throw "Unable to resolve manual-test asset '$ExpectedName' in $AssetsDir"
}

function Validate-PublishedDirectory {
    param(
        [string]$Path,
        [string]$ParameterName
    )

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "-SkipDotnetPublish requires $ParameterName to be an existing directory: $Path"
    }
}

$RepoRoot = (Resolve-Path $RepoRoot).Path

$machinePath = [System.Environment]::GetEnvironmentVariable('PATH', 'Machine')
$userPath = [System.Environment]::GetEnvironmentVariable('PATH', 'User')
if ($machinePath) {
    $env:PATH = $machinePath + ';' + $userPath
}

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    throw 'dotnet command not found. Install .NET 10 SDK first.'
}

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

if ($SkipDotnetPublish) {
    Validate-PublishedDirectory -Path $PublishedMainDir -ParameterName 'PublishedMainDir'
    Validate-PublishedDirectory -Path $PublishedScannerDir -ParameterName 'PublishedScannerDir'
}
else {
    $backupPath = Join-Path $stagingRoot 'ErsatzTV.csproj.backup'
    try {
        Remove-ScannerReference -CsprojPath $mainCsproj -BackupPath $backupPath

        $PublishedScannerDir = Join-Path $stagingRoot 'scanner'
        $PublishedMainDir = Join-Path $stagingRoot 'main'

        & $dotnet.Source publish $scannerCsproj --framework net10.0 --runtime win-x64 -c Release -o $PublishedScannerDir -p:RestoreEnablePackagePruning=true -p:EnableCompressionInSingleFile=false -p:DebugType=Embedded --self-contained true
        if ($LASTEXITCODE -ne 0) { throw 'scanner publish failed' }

        & $dotnet.Source publish $mainCsproj --framework net10.0 --runtime win-x64 -c Release -o $PublishedMainDir -p:RestoreEnablePackagePruning=true -p:EnableCompressionInSingleFile=false -p:DebugType=Embedded --self-contained true
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

$launcherCmdAsset = Resolve-ManualTestAsset -AssetsDir $assetsDir -ExpectedName '启动 ErsatzTV.cmd' -FallbackFilter '*.cmd'
$readmeAsset = Resolve-ManualTestAsset -AssetsDir $assetsDir -ExpectedName 'README-手测说明.txt' -FallbackFilter 'README-*.txt'

Copy-Item -LiteralPath $launcherCmdAsset -Destination (Join-Path $packageDir '启动 ErsatzTV.cmd') -Force
Copy-Item -LiteralPath (Join-Path $assetsDir 'Start-ErsatzTV.ps1') -Destination (Join-Path $packageDir 'Start-ErsatzTV.ps1') -Force
Copy-Item -LiteralPath $readmeAsset -Destination (Join-Path $packageDir 'README-手测说明.txt') -Force

Compress-Archive -Path (Join-Path $packageDir '*') -DestinationPath $zipPath -Force
Write-Host "Package created: $zipPath"
