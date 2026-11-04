param([Parameter(Mandatory = $true)][string]$ContentFolder)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$verify = Join-Path $projectRoot 'scripts\Verify-WorkshopPackage.ps1'
$fixtureRoot = Join-Path $projectRoot ('.cache\package-verification-tests\' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path (Join-Path $fixtureRoot 'Textures') | Out-Null
foreach ($file in @('ProbablyAI.dll','manifest.xml','README.md','README.en.md','preview.png','Textures\deepseek-npc.png')) {
    Copy-Item -LiteralPath (Join-Path $ContentFolder $file) -Destination (Join-Path $fixtureRoot $file)
}
function Expect-Blocked {
    $blocked = $false
    try { & $verify -ContentFolder $fixtureRoot | Out-Null } catch { $blocked = $true }
    if (!$blocked) { throw 'Package verifier failed to block an unsafe fixture.' }
}
& $verify -ContentFolder $fixtureRoot | Out-Null
$manifestFile = Join-Path $fixtureRoot 'manifest.xml'
$originalManifest = Get-Content -LiteralPath $manifestFile -Raw
[xml]$manifest = $originalManifest
$capabilities = $manifest.Manifest.Capabilities
$filesCapability = $capabilities.SelectSingleNode("Capability[text()='files']")
$null = $capabilities.RemoveChild($filesCapability)
$manifest.Save($manifestFile)
Expect-Blocked
$null = $capabilities.AppendChild($filesCapability)
$unexpectedCapability = $manifest.CreateElement('Capability')
$unexpectedCapability.InnerText = 'process'
$null = $capabilities.AppendChild($unexpectedCapability)
$manifest.Save($manifestFile)
Expect-Blocked
Set-Content -LiteralPath $manifestFile -Value $originalManifest -Encoding utf8 -NoNewline
$extraFile = Join-Path $fixtureRoot 'connection.local.json'
Set-Content -LiteralPath $extraFile -Value '{"ApiKey":"test-only"}'
Expect-Blocked
Remove-Item -LiteralPath $extraFile
$readmeFile = Join-Path $fixtureRoot 'README.en.md'
Set-Content -LiteralPath $readmeFile -Value ('sk-' + ('X' * 32))
Expect-Blocked
Set-Content -LiteralPath $readmeFile -Value 'C:\Users\Example\private\settings.json'
Expect-Blocked
Write-Output 'Package verification: clean package accepted; missing or extra capabilities, extra config, synthetic key and private path blocked.'
