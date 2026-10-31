param(
    [string]$GameDirectory = 'C:\Program Files (x86)\Steam\steamapps\common\Probably Stolen',
    [UInt64]$PublishedFileId = 0
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'Build.ps1') -GameDirectory $GameDirectory
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
[xml]$manifest = Get-Content -LiteralPath (Join-Path $projectRoot 'manifest.xml') -Raw -Encoding utf8
$releaseRoot = Join-Path $projectRoot ('dist\workshop\' + $manifest.Manifest.ModVersion)
$contentRoot = Join-Path $releaseRoot 'content'
New-Item -ItemType Directory -Force -Path (Join-Path $contentRoot 'Textures') | Out-Null
$exports = @{
    'bin\Release\netstandard2.1\ProbablyAI.dll' = 'ProbablyAI.dll'
    'manifest.xml' = 'manifest.xml'
    'workshop\README.zh.md' = 'README.md'
    'README.en.md' = 'README.en.md'
    'assets\deepseek-npc.png' = 'Textures\deepseek-npc.png'
    'assets\workshop-cover-v1.png' = 'preview.png'
}
foreach ($sourceFile in $exports.Keys) {
    Copy-Item -LiteralPath (Join-Path $projectRoot $sourceFile) -Destination (Join-Path $contentRoot $exports[$sourceFile])
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'assets\workshop-preview-v1.jpg') -Destination (Join-Path $releaseRoot 'preview.jpg')
if ((Get-Item -LiteralPath (Join-Path $releaseRoot 'preview.jpg')).Length -ge 1MB) { throw 'Workshop preview must be under 1 MB.' }
$inventory = @(& (Join-Path $PSScriptRoot 'Verify-WorkshopPackage.ps1') -ContentFolder $contentRoot)
$inventory | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $releaseRoot 'package-inventory.json') -Encoding utf8
$publicMetadata = Join-Path $projectRoot 'workshop\item.json'
if ($PublishedFileId -eq 0 -and (Test-Path -LiteralPath $publicMetadata)) { $PublishedFileId = (Get-Content -LiteralPath $publicMetadata -Raw | ConvertFrom-Json).publishedFileId }
$title = Get-Content -LiteralPath (Join-Path $projectRoot 'workshop\title.en.txt') -Raw
$description = Get-Content -LiteralPath (Join-Path $projectRoot 'workshop\description.en.bbcode') -Raw
$changenoteFile = Join-Path $projectRoot 'workshop\changenote.en.txt'
$changenote = if (Test-Path -LiteralPath $changenoteFile) { (Get-Content -LiteralPath $changenoteFile -Raw).Trim() } else { "v$($manifest.Manifest.ModVersion)" }
function Escape-Vdf([string]$value) { return $value.Replace('\', '\\').Replace('"', '\"').Replace("`r", '').Replace("`n", '\n') }
$vdf = @"
"workshopitem"
{
    "appid" "4348910"
    "publishedfileid" "$PublishedFileId"
    "contentfolder" "$(Escape-Vdf $contentRoot)"
    "previewfile" "$(Escape-Vdf (Join-Path $releaseRoot 'preview.jpg'))"
    "visibility" "0"
    "title" "$(Escape-Vdf $title.Trim())"
    "description" "$(Escape-Vdf $description.Trim())"
    "changenote" "$(Escape-Vdf $changenote)"
}
"@
$vdf | Set-Content -LiteralPath (Join-Path $releaseRoot 'workshop.vdf') -Encoding utf8
Write-Output "Verified Workshop content: $contentRoot ($($inventory.Count) allowlisted files)"
Write-Output "SteamCMD definition (outside content): $(Join-Path $releaseRoot 'workshop.vdf')"
