$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "../tests/FeatherBrowser.StructureChecks/FeatherBrowser.StructureChecks.csproj"
dotnet run --project $project --configuration Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
