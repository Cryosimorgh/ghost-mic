param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    if (-not $SkipBuild) { & .\build.ps1 }
    $payloadNames = @('GhostMic.exe', 'GhostMic.dll', 'GhostMic.deps.json', 'GhostMic.runtimeconfig.json', 'Setup-Cable.ps1', 'README.md', 'NAudio-LICENSE.txt')
    $payload = @($payloadNames | ForEach-Object { (Get-Item -LiteralPath (Join-Path 'dist' $_)).FullName })
    $payload += @(Get-ChildItem -LiteralPath dist -Filter 'NAudio*.dll' -File | Select-Object -ExpandProperty FullName)
    New-Item -ItemType Directory -Path artifacts -Force | Out-Null
    $zipPath = Join-Path $PSScriptRoot 'artifacts\GhostMic-win-x64.zip'
    Compress-Archive -LiteralPath $payload -DestinationPath $zipPath -Force
    $hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    [System.IO.File]::WriteAllText((Join-Path $PSScriptRoot 'artifacts\GhostMic-win-x64.sha256'), "$hash  GhostMic-win-x64.zip`n")
    Write-Host "Packaged $zipPath"
} finally { Pop-Location }
