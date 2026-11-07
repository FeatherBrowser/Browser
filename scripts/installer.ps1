param([string]$IsccPath)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
[xml]$project = Get-Content (Join-Path $root 'src/FeatherBrowser/FeatherBrowser.csproj') -Raw
$version = $project.SelectSingleNode('/Project/PropertyGroup/Version').InnerText.Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Invalid installer version: $version" }
$publish = Join-Path $root 'artifacts/publish/win-x64'
foreach ($file in @('FeatherBrowser.exe', 'FeatherBrowser.runtimeconfig.json', 'coreclr.dll', 'libcef.dll', 'CefSharp.Wpf.dll')) {
    if (-not (Test-Path (Join-Path $publish $file))) {
        throw "Missing $file. Run ./scripts/publish.ps1 -Runtime win-x64 -SelfContained first."
    }
}
if (-not $IsccPath) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { $IsccPath = $command.Source }
    else { $IsccPath = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe' }
}
if (-not (Test-Path $IsccPath)) { throw 'Install Inno Setup 6.3 or newer, or supply -IsccPath.' }
$output = Join-Path $root 'artifacts/installer'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$installer = Join-Path $output "FeatherBrowser-v$version-win-x64-Setup.exe"
if (Test-Path $installer) { Remove-Item $installer -Force }
& $IsccPath "/DAppVersion=$version" "/DPublishDir=$publish" "/DOutputDirPath=$output" (Join-Path $root 'installer/FeatherBrowser.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE" }
if (-not (Test-Path $installer)) { throw "Installer not created: $installer" }
Write-Host "Installer created: $installer" -ForegroundColor Green
