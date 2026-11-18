param([string]$Dotnet = 'dotnet', [string]$Iscc = 'ISCC.exe', [switch]$Installer, [switch]$SmokeTest, [switch]$SkipPortable)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.build-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
Push-Location $projectRoot
try {
    & $Dotnet run --project tests/WindowTiler.Core.Tests -c Release -p:RestoreConfigFile="$projectRoot/NuGet.Config"
    if ($LASTEXITCODE -ne 0) { throw 'Layout tests failed' }
    & $Dotnet publish src/WindowTiler.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:RestoreConfigFile="$projectRoot/NuGet.Config" -o artifacts/publish
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
    if ($SmokeTest) {
        $env:WINDOW_TILER_TEST_OUTPUT = Join-Path $projectRoot 'artifacts/qa'
        $smoke = Start-Process -FilePath (Join-Path $projectRoot 'artifacts/publish/WindowTiler.exe') -ArgumentList '--smoke-test' -WindowStyle Hidden -PassThru
        if (-not $smoke.WaitForExit(180000)) { Stop-Process -Id $smoke.Id; throw 'Smoke test timed out' }
        if ($smoke.ExitCode -ne 0) { throw 'Smoke test failed; see artifacts/qa/smoke-report.json' }
    }
    if ($Installer) {
        & $Iscc installer/WindowTiler.iss
        if ($LASTEXITCODE -ne 0) { throw 'Installer build failed' }
    }
    if ($SkipPortable) { Write-Output 'Validation build completed; portable release unchanged.'; return }
    $portableDir = Join-Path $projectRoot 'artifacts/portable'
    New-Item -ItemType Directory -Force -Path $portableDir | Out-Null
    $publishedExe = Join-Path $projectRoot 'artifacts/publish/WindowTiler.exe'
    $releaseVersion = (Get-Item -LiteralPath $publishedExe).VersionInfo.ProductVersion
    $versionedExe = Join-Path $portableDir ('WindowTiler-' + $releaseVersion + '.exe')
    Copy-Item -LiteralPath $publishedExe -Destination $versionedExe -Force
    try { Copy-Item -LiteralPath $publishedExe -Destination (Join-Path $portableDir 'WindowTiler.exe') -Force }
    catch { Write-Warning ('Cannot update WindowTiler.exe; use the versioned EXE after exiting the old app. ' + $_.Exception.Message) }
    Write-Output ('Portable EXE: ' + $versionedExe)
} finally { Pop-Location }
