$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Push-Location (Join-Path $PSScriptRoot '..')

try {
    [xml]$project = Get-Content 'src/FeatherBrowser/FeatherBrowser.csproj' -Raw
    $versionNode = $project.SelectSingleNode('/Project/PropertyGroup/Version')

    if ($null -eq $versionNode) {
        throw 'Project Version is missing.'
    }

    $version = $versionNode.InnerText.Trim()

    if ($version -notmatch '^\d+\.\d+\.\d+$') {
        throw "Invalid release version: $version"
    }

    $tag = "v$version"
    $changelog = Get-Content 'changelogs.md' -Raw
    $escaped = [regex]::Escape($version)
    $pattern = "(?ms)^##[ \t]+(?:\[v?$escaped\]|v?$escaped)(?=[ \t\r\n]|$)[^\r\n]*\r?\n(?<notes>.*?)(?=^##[ \t]+|\z)"
    $section = [regex]::Match($changelog, $pattern)

    if (-not $section.Success -or
        [string]::IsNullOrWhiteSpace($section.Groups['notes'].Value)) {
        throw "changelogs.md needs a nonempty ## $version section."
    }

    $tags = @(
        gh api --paginate 'repos/{owner}/{repo}/releases?per_page=100' `
            --jq '.[].tag_name'
    )

    if ($LASTEXITCODE -ne 0) {
        throw 'Could not list GitHub releases.'
    }

    if ($tags -contains $tag) {
        Write-Host "Release $tag already exists; skipping."
        return
    }

    $existingTag = @(git ls-remote --tags origin "refs/tags/$tag")

    if ($LASTEXITCODE -ne 0) {
        throw 'Could not check remote tags.'
    }

    if ($existingTag.Count -gt 0) {
        throw "Tag $tag already exists without a release. Resolve it before publishing."
    }

    if ([string]::IsNullOrWhiteSpace($env:GITHUB_SHA)) {
        throw 'GITHUB_SHA is required.'
    }

    if (-not (Test-Path 'artifacts/publish/win-x64/FeatherBrowser.exe')) {
        throw 'Published FeatherBrowser.exe is missing.'
    }

    New-Item -ItemType Directory -Path 'artifacts/release' -Force |
        Out-Null

    $notesPath = 'artifacts/release/notes.md'
    $section.Groups['notes'].Value.Trim() |
        Set-Content $notesPath -Encoding utf8

    $archive = "artifacts/release/FeatherBrowser-$tag-win-x64.zip"

    Compress-Archive -Path 'artifacts/publish/win-x64/*' `
        -DestinationPath $archive -Force

    gh release create $tag $archive `
        --target $env:GITHUB_SHA `
        --title "Feather Browser $version" `
        --notes-file $notesPath

    if ($LASTEXITCODE -ne 0) {
        throw 'GitHub release creation failed.'
    }
}
finally {
    Pop-Location
}