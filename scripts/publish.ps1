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
Write-Host "Feather published to: $out" -ForegroundColor Green
