$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
Push-Location $repoRoot

try {
    $projectPath = 'src/FeatherBrowser/FeatherBrowser.csproj'

    if (-not (Test-Path $projectPath)) {
        throw "Project file not found: $projectPath"
    }

    [xml]$project = Get-Content $projectPath -Raw
    $versionNode = $project.SelectSingleNode('/Project/PropertyGroup/Version')

    if ($null -eq $versionNode) {
        throw 'Project Version is missing.'
    }

    $version = $versionNode.InnerText.Trim()

    if ($version -notmatch '^\d+\.\d+\.\d+$') {
        throw "Invalid release version: $version"
    }

    $tag = "v$version"

    Write-Host "Preparing Feather Browser $version"
    Write-Host "Release tag: $tag"

    $changelogCandidates = @(
        'changelogs.md',
        'CHANGELOG.md',
        'Changelog.md',
        'CHANGELOG.MD'
    )

    $changelogPath = $null

    foreach ($candidate in $changelogCandidates) {
        if (Test-Path $candidate) {
            $changelogPath = $candidate
            break
        }
    }

    $releaseNotes = $null

    if ($null -ne $changelogPath) {
        Write-Host "Using changelog: $changelogPath"

        $changelog = Get-Content $changelogPath -Raw
        $escaped = [regex]::Escape($version)
        $pattern = "(?ms)^##[ \t]+(?:\[v?$escaped\]|v?$escaped)(?=[ \t\r\n]|$)[^\r\n]*\r?\n(?<notes>.*?)(?=^##[ \t]+|\z)"
        $section = [regex]::Match($changelog, $pattern)

        if ($section.Success -and
            -not [string]::IsNullOrWhiteSpace($section.Groups['notes'].Value)) {
            $releaseNotes = $section.Groups['notes'].Value.Trim()
        }
        else {
            Write-Warning "No non-empty ## $version section was found in $changelogPath."
        }
    }
    else {
        Write-Warning 'No changelog file was found.'
    }

    if ([string]::IsNullOrWhiteSpace($releaseNotes)) {
        $releaseNotes = @"
## Feather Browser $version

Automated release of Feather Browser $version.

### Installation

Download FeatherBrowser-v$version-win-x64-Setup.exe below and run the installer.
"@
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
        $tagCommit = (git rev-list -n 1 $tag).Trim()

        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($tagCommit)) {
            throw "Could not resolve existing tag $tag."
        }

        $workflowCommit = (git rev-parse "$($env:GITHUB_SHA)^{commit}").Trim()

        if (-not [string]::Equals(
            $tagCommit,
            $workflowCommit,
            [StringComparison]::OrdinalIgnoreCase)) {
            throw "Tag $tag does not point to the commit being released."
        }

        Write-Host "Using existing verified release tag $tag."
    }

    if ([string]::IsNullOrWhiteSpace($env:GITHUB_SHA)) {
        throw 'GITHUB_SHA is required.'
    }

    $installer = "artifacts/installer/FeatherBrowser-$tag-win-x64-Setup.exe"
    if (-not (Test-Path $installer)) {
        throw "Installer is missing: $installer"
    }

    $releaseDirectory = 'artifacts/release'

    New-Item `
        -ItemType Directory `
        -Path $releaseDirectory `
        -Force |
        Out-Null

    $notesPath = Join-Path $releaseDirectory 'notes.md'

    $releaseNotes |
        Set-Content `
            -Path $notesPath `
            -Encoding utf8

    gh release create $tag $installer `
        --title "Feather Browser $version" `
        --notes-file $notesPath

    if ($LASTEXITCODE -ne 0) {
        throw 'GitHub release creation failed.'
    }

    Write-Host "Successfully published Feather Browser $version."
}
finally {
    Pop-Location
}