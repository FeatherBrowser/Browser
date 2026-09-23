$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
[xml]$project = Get-Content (Join-Path $root 'src/FeatherBrowser/FeatherBrowser.csproj') -Raw
$version = $project.SelectSingleNode('/Project/PropertyGroup/Version').InnerText.Trim()
$installer = Join-Path $root "artifacts/installer/FeatherBrowser-v$version-win-x64-Setup.exe"
$target = Join-Path ([IO.Path]::GetTempPath()) "FeatherInstallerCheck-$([Guid]::NewGuid())"
$process = Start-Process -FilePath $installer -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/DIR=`"$target`"") -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "Installer failed: $($process.ExitCode)" }
try {
    foreach ($file in @('FeatherBrowser.exe', 'coreclr.dll', 'Microsoft.Web.WebView2.Wpf.dll', 'unins000.exe')) {
        if (-not (Test-Path (Join-Path $target $file))) { throw "Installed file missing: $file" }
    }
    $installedVersion = (Get-Item (Join-Path $target 'FeatherBrowser.exe')).VersionInfo.ProductVersion
    if (-not $installedVersion.StartsWith($version)) { throw "Unexpected installed version: $installedVersion" }
}
finally {
    $uninstaller = Join-Path $target 'unins000.exe'
    if (Test-Path $uninstaller) {
        $process = Start-Process -FilePath $uninstaller -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -Wait -PassThru
        if ($process.ExitCode -ne 0) { throw "Uninstaller failed: $($process.ExitCode)" }
    }
}
if (Test-Path (Join-Path $target 'FeatherBrowser.exe')) { throw 'Uninstall left the application executable behind.' }
Write-Host 'Installer installation and uninstall passed.' -ForegroundColor Green
