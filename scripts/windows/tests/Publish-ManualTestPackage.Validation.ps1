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

function Invoke-PackageBuilder {
    param(
        [string]$ScriptPath,
        [string[]]$Arguments,
        [hashtable]$EnvironmentOverrides = @{}
    )

    $previousErrorActionPreference = $ErrorActionPreference
    $previousValues = @{}
    foreach ($entry in $EnvironmentOverrides.GetEnumerator()) {
        $previousValues[$entry.Key] = [System.Environment]::GetEnvironmentVariable($entry.Key)
        [System.Environment]::SetEnvironmentVariable($entry.Key, $entry.Value)
    }

    $ErrorActionPreference = 'Continue'
    try {
        $output = & powershell -NoProfile -ExecutionPolicy Bypass -File $ScriptPath @Arguments 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
        foreach ($entry in $EnvironmentOverrides.GetEnumerator()) {
            [System.Environment]::SetEnvironmentVariable($entry.Key, $previousValues[$entry.Key])
        }
    }

    return [pscustomobject]@{
        ExitCode = $exitCode
        Output = @($output)
    }
}

function New-FakeRepo {
    param(
        [string]$Root,
        [bool]$IncludeScannerReference
    )

    $mainProjectDir = Join-Path $Root 'ErsatzTV'
    $scannerProjectDir = Join-Path $Root 'ErsatzTV.Scanner'
    $assetsDir = Join-Path $Root 'scripts\windows\manual-test'

    New-Item -ItemType Directory -Force -Path $mainProjectDir, $scannerProjectDir, $assetsDir | Out-Null

    $referenceLine = if ($IncludeScannerReference) {
        '        <ProjectReference Include="..\ErsatzTV.Scanner\ErsatzTV.Scanner.csproj" />' + "`r`n"
    }
    else {
        ''
    }

    @"
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
$referenceLine  </ItemGroup>
</Project>
"@ | Set-Content -Path (Join-Path $mainProjectDir 'ErsatzTV.csproj') -Encoding UTF8

    @"
<Project Sdk="Microsoft.NET.Sdk">
</Project>
"@ | Set-Content -Path (Join-Path $scannerProjectDir 'ErsatzTV.Scanner.csproj') -Encoding UTF8

    $sourceAssetsDir = Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..')).Path 'manual-test'
    Copy-Item -Recurse -Force (Join-Path $sourceAssetsDir '*') $assetsDir
}

function New-FakeDotnet {
    param([string]$BinDir)

    New-Item -ItemType Directory -Force -Path $BinDir | Out-Null

    @"
@echo off
setlocal enabledelayedexpansion
set "outputDir="
set "projectPath="
set "nextIsOutput="
for %%A in (%*) do (
    if defined nextIsOutput (
        set "outputDir=%%~A"
        set "nextIsOutput="
    ) else (
        if /I "%%~A"=="-o" (
            set "nextIsOutput=1"
        ) else (
            if not defined projectPath (
                if /I not "%%~A"=="publish" (
                    if "%%~A:~0,1%" neq "-" (
                        set "projectPath=%%~A"
                    )
                )
            )
        )
    )
)
if defined outputDir (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "New-Item -ItemType Directory -Force -Path '!outputDir!' | Out-Null; if ('!projectPath!' -like '*Scanner*') { 'fake-scanner' | Set-Content -Path (Join-Path '!outputDir!' 'ErsatzTV.Scanner.exe') -Encoding ASCII } else { 'fake-main' | Set-Content -Path (Join-Path '!outputDir!' 'ErsatzTV.exe') -Encoding ASCII }"
)
exit /b 0
"@ | Set-Content -Path (Join-Path $BinDir 'dotnet.cmd') -Encoding ASCII
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..\..')
$script = Join-Path $repoRoot 'scripts\windows\Publish-ManualTestPackage.ps1'
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('ersatztv-package-validation-' + [guid]::NewGuid().ToString('N'))
$fakeRepo = Join-Path $tempRoot 'repo'
$fakeBin = Join-Path $tempRoot 'bin'
$validMain = Join-Path $tempRoot 'valid-main'
$validScanner = Join-Path $tempRoot 'valid-scanner'

New-Item -ItemType Directory -Force -Path $validMain, $validScanner | Out-Null
'fake-main' | Set-Content -Path (Join-Path $validMain 'ErsatzTV.exe') -Encoding ASCII
'fake-scanner' | Set-Content -Path (Join-Path $validScanner 'ErsatzTV.Scanner.exe') -Encoding ASCII

try {
    New-FakeRepo -Root $fakeRepo -IncludeScannerReference $false
    New-FakeDotnet -BinDir $fakeBin

    $pathOverride = "$fakeBin;$env:PATH"

    $missingMainResult = Invoke-PackageBuilder -ScriptPath $script -Arguments @(
        '-RepoRoot', $fakeRepo,
        '-OutputRoot', (Join-Path $tempRoot 'missing-main-out'),
        '-PackageName', 'missing-main',
        '-SkipDotnetPublish',
        '-PublishedMainDir', (Join-Path $tempRoot 'missing-main-dir'),
        '-PublishedScannerDir', $validScanner
    )
    Assert-True ($missingMainResult.ExitCode -ne 0) 'missing PublishedMainDir should fail in skip-publish mode'
    Assert-True ((($missingMainResult.Output -join "`n") -match 'PublishedMainDir') -and (($missingMainResult.Output -join "`n") -match 'existing directory')) ("missing PublishedMainDir should produce a clear validation error. Output:`n{0}" -f ($missingMainResult.Output -join "`n"))

    $missingScannerResult = Invoke-PackageBuilder -ScriptPath $script -Arguments @(
        '-RepoRoot', $fakeRepo,
        '-OutputRoot', (Join-Path $tempRoot 'missing-scanner-out'),
        '-PackageName', 'missing-scanner',
        '-SkipDotnetPublish',
        '-PublishedMainDir', $validMain,
        '-PublishedScannerDir', (Join-Path $tempRoot 'missing-scanner-dir')
    )
    Assert-True ($missingScannerResult.ExitCode -ne 0) 'missing PublishedScannerDir should fail in skip-publish mode'
    Assert-True ((($missingScannerResult.Output -join "`n") -match 'PublishedScannerDir') -and (($missingScannerResult.Output -join "`n") -match 'existing directory')) ("missing PublishedScannerDir should produce a clear validation error. Output:`n{0}" -f ($missingScannerResult.Output -join "`n"))

    $missingReferenceResult = Invoke-PackageBuilder -ScriptPath $script -Arguments @(
        '-RepoRoot', $fakeRepo,
        '-OutputRoot', (Join-Path $tempRoot 'missing-reference-out'),
        '-PackageName', 'missing-reference'
    ) -EnvironmentOverrides @{ PATH = $pathOverride }
    Assert-True ($missingReferenceResult.ExitCode -ne 0) 'missing scanner project reference should fail explicitly'
    Assert-True ((($missingReferenceResult.Output -join "`n") -match 'ProjectReference') -and (($missingReferenceResult.Output -join "`n") -match 'not found')) ("missing scanner project reference should produce an explicit failure. Output:`n{0}" -f ($missingReferenceResult.Output -join "`n"))

    Write-Host 'PASS: package builder validates skip-publish input directories and fails explicitly when the scanner project reference is missing'
}
finally {
    Remove-Item -Recurse -Force $tempRoot -ErrorAction SilentlyContinue
}
