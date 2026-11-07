param(
    [ValidateSet("win-x64", "win-arm64", "win-x86")][string]$Runtime = "win-x64",
    [switch]$SelfContained
)
$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "../src/FeatherBrowser/FeatherBrowser.csproj"
$out = Join-Path $PSScriptRoot "../artifacts/publish/$Runtime"
$includeRuntime = $SelfContained.IsPresent.ToString().ToLowerInvariant()

if (Test-Path $out) { Remove-Item $out -Recurse -Force }

dotnet publish $project --configuration Release --runtime $Runtime --self-contained $includeRuntime --output $out -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$root = Join-Path $PSScriptRoot ".."
foreach ($file in @("LICENSE", "THIRD_PARTY_NOTICES.md", "README.md")) {
    Copy-Item (Join-Path $root $file) $out -Force
}
Copy-Item (Join-Path $root "third-party") $out -Recurse -Force

$arch = @{ "win-x64" = "x64"; "win-arm64" = "arm64"; "win-x86" = "x86" }[$Runtime]
$systemDir = if ($arch -eq "x86" -and [Environment]::Is64BitOperatingSystem) { Join-Path $env:SystemRoot "SysWOW64" } else { Join-Path $env:SystemRoot "System32" }
$vcRuntime = @("msvcp140.dll", "msvcp140_1.dll", "msvcp140_2.dll", "vcruntime140.dll", "vcruntime140_1.dll", "concrt140.dll")
foreach ($dll in $vcRuntime) {
    $source = Join-Path $systemDir $dll
    if (Test-Path $source) { Copy-Item $source $out -Force }
    elseif ($dll -in @("msvcp140.dll", "vcruntime140.dll")) {
        throw "Visual C++ runtime file $dll was not found in $systemDir. Install the Visual C++ 2022 Redistributable ($arch) on the build machine."
    }
}

foreach ($required in @("libcef.dll", "CefSharp.Core.Runtime.dll", "CefSharp.Wpf.dll", "icudtl.dat", "resources.pak", "chrome_elf.dll", "v8_context_snapshot.bin", "locales/en-US.pak")) {
    if (-not (Test-Path (Join-Path $out $required))) { throw "Chromium engine file missing from publish output: $required" }
}
Write-Host "Feather published to: $out" -ForegroundColor Green
