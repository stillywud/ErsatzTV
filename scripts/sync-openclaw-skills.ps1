param(
    [string]$Source = 'C:\Users\intel1230\skills',
    [string]$Target = 'D:\project\ErsatzTV\openclaw-skills'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $Source)) {
    throw "Source skills directory not found: $Source"
}

New-Item -ItemType Directory -Force -Path $Target | Out-Null

Write-Host "[INFO] Mirroring OpenClaw skills"
Write-Host "       source: $Source"
Write-Host "       target: $Target"

robocopy $Source $Target /MIR /R:2 /W:1 /NFL /NDL /NP /NJH /NJS | Out-Host
$code = $LASTEXITCODE

if ($code -ge 8) {
    throw "robocopy failed with exit code $code"
}

Write-Host "[INFO] Skills mirror updated successfully."
