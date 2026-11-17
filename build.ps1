$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet build -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    $test = Start-Process -FilePath '.\bin\Release\net10.0-windows\GhostMic.exe' -ArgumentList '--self-test' -PassThru -Wait -WindowStyle Hidden
    Get-Content self-test-result.txt
    if ($test.ExitCode -ne 0) { throw 'Audio processor tests failed' }
    $uiTest = Start-Process -FilePath '.\bin\Release\net10.0-windows\GhostMic.exe' -ArgumentList '--smoke-test' -PassThru -WindowStyle Hidden
    if (-not $uiTest.WaitForExit(30000)) { $uiTest.Kill(); throw 'UI checks timed out' }
    Get-Content bin/Release/net10.0-windows/smoke-test-result.txt
    if ($uiTest.ExitCode -ne 0) { throw 'UI and menu regression tests failed' }
    $trayTest = Start-Process -FilePath '.\bin\Release\net10.0-windows\GhostMic.exe' -ArgumentList '--tray-test' -PassThru -WindowStyle Hidden
    if (-not $trayTest.WaitForExit(15000)) { $trayTest.Kill(); throw 'Tray checks timed out' }
    Get-Content bin/Release/net10.0-windows/tray-test-result.txt
    if ($trayTest.ExitCode -ne 0) { throw 'Tray lifecycle checks failed' }
    dotnet publish -c Release --no-restore --self-contained false -o dist
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
    Copy-Item -LiteralPath 'Setup-Cable.ps1','README.md','NAudio-LICENSE.txt' -Destination dist -Force
    Write-Host ('Ready: ' + (Join-Path $PSScriptRoot 'dist\GhostMic.exe'))
} finally { Pop-Location }
