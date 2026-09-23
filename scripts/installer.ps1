param([string]$IsccPath)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
[xml]$project = Get-Content (Join-Path $root 'src/FeatherBrowser/FeatherBrowser.csproj') -Raw
$version = $project.SelectSingleNode('/Project/PropertyGroup/Version').InnerText.Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Invalid installer version: $version" }
$publish = Join-Path $root 'artifacts/publish/win-x64'
foreach ($file in @('FeatherBrowser.exe', 'FeatherBrowser.runtimeconfig.json', 'coreclr.dll')) {
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
$dependencies = Join-Path $root 'artifacts/dependencies'
New-Item -ItemType Directory -Path $output, $dependencies -Force | Out-Null
$bootstrapper = Join-Path $dependencies 'MicrosoftEdgeWebView2Setup.exe'
Invoke-WebRequest -Uri 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile $bootstrapper
$signature = Get-AuthenticodeSignature $bootstrapper
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation(?:,|$)') {
    throw 'WebView2 bootstrapper must have a valid Microsoft Authenticode signature.'
}
$installer = Join-Path $output "FeatherBrowser-v$version-win-x64-Setup.exe"
if (Test-Path $installer) { Remove-Item $installer -Force }
& $IsccPath "/DAppVersion=$version" "/DPublishDir=$publish" "/DOutputDirPath=$output" "/DBootstrapperPath=$bootstrapper" (Join-Path $root 'installer/FeatherBrowser.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE" }
if (-not (Test-Path $installer)) { throw "Installer not created: $installer" }
Write-Host "Installer created: $installer" -ForegroundColor Green
