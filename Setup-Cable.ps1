$ErrorActionPreference = 'Stop'
# Only launched when the user clicks Set up virtual device. Driver installation needs UAC.
$setupDirectory = Join-Path $PSScriptRoot '.setup'
New-Item -ItemType Directory -Force -Path $setupDirectory | Out-Null
$zipPath = Join-Path $setupDirectory 'VBCABLE_Driver_Pack46.zip'
$unpackDirectory = Join-Path $setupDirectory 'VBCABLE_Driver_Pack46'
try {
    Write-Host 'Downloading VB-CABLE from the official VB-Audio site...'
    Invoke-WebRequest -Uri 'https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack46.zip' -OutFile $zipPath -UseBasicParsing
    Expand-Archive -LiteralPath $zipPath -DestinationPath $unpackDirectory -Force
    $installers = @(Get-ChildItem -LiteralPath $unpackDirectory -Filter '*Setup*x64.exe' -Recurse)
    if ($installers.Count -ne 1) { throw 'Could not identify the x64 installer. Visit https://vb-audio.com/Cable/ to install manually.' }
    $installer = $installers[0]
    $signature = Get-AuthenticodeSignature -LiteralPath $installer.FullName
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Vincent Burel|BUREL VINCENT|VB.Audio') {
        throw ('Installer signature could not be verified: ' + $signature.Status + '. Install manually from https://vb-audio.com/Cable/ instead.')
    }
    Write-Host ('Verified publisher: ' + $signature.SignerCertificate.Subject)
    Write-Host 'Click Install Driver in the installer, then reboot Windows. Reopen Ghost Mic after reboot.'
    Start-Process -FilePath $installer.FullName -WorkingDirectory $installer.DirectoryName -Verb RunAs -Wait
} catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    Read-Host 'Press Enter to close'
    exit 1
}
