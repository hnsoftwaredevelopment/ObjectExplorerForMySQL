<#
  Writes overview.md, the page text for the Visual Studio Marketplace, from README.md.

  The Marketplace refuses links to SVG files and shows the page outside the repository, so the overview:
    - leaves out the badges (they are SVG images);
    - turns relative links to docs/ into absolute GitHub URLs (images through raw.githubusercontent.com);
    - leaves out the sections for contributors and ends with a link to the repository.

  Run it after a change to README.md, then paste overview.md into the Marketplace page or let the release
  workflow publish it (publishManifest.json uses overview.md).

  Example:
    powershell -File tools\New-MarketplaceOverview.ps1
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$repository = 'https://github.com/hnsoftwaredevelopment/ObjectExplorerForMySQL'
$raw = 'https://raw.githubusercontent.com/hnsoftwaredevelopment/ObjectExplorerForMySQL/main/'
$leaveOut = @('## Building from source', '## Documentation for contributors')

$lines = Get-Content (Join-Path $root 'README.md') -Encoding UTF8
$result = New-Object System.Collections.Generic.List[string]
$skip = $false
foreach ($line in $lines) {
    if ($line -match '^## ') { $skip = $leaveOut -contains $line.Trim() }
    if ($skip) { continue }

    # Badges are SVG images; the Marketplace does not accept them.
    if ($line -match '^\[!\[') { continue }

    # Contents entries of the sections that are left out.
    if ($line -match '^- \[(Building from source|Documentation for contributors)\]') { continue }

    $line = $line -replace '(src="|\]\()docs/', ('$1' + $raw + 'docs/')
    $result.Add($line)
}

# No two empty lines in a row where the badges were.
$text = ($result -join "`n") -replace "`n{3,}", "`n`n"
$text = $text.TrimEnd() + "`n`n## More information`n`nSource code, issues and the changelog: [$repository]($repository).`n"
[IO.File]::WriteAllText((Join-Path $root 'overview.md'), $text, (New-Object Text.UTF8Encoding($false)))
Write-Host "overview.md written."
