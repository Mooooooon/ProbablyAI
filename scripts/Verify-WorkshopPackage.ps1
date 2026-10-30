param([Parameter(Mandatory = $true)][string]$ContentFolder)
$ErrorActionPreference = 'Stop'
$packageRoot = [System.IO.Path]::GetFullPath($ContentFolder)
if (!(Test-Path -LiteralPath $packageRoot -PathType Container)) { throw 'Workshop content folder does not exist.' }
$allowed = @('ProbablyAI.dll', 'manifest.xml', 'README.md', 'README.en.md', 'preview.png', 'Textures/deepseek-npc.png')
$allEntries = @(Get-ChildItem -LiteralPath $packageRoot -Recurse -Force)
if ($allEntries | Where-Object { $_.Attributes -band [System.IO.FileAttributes]::ReparsePoint }) { throw 'Workshop package must not contain symlinks or junctions.' }
$files = @($allEntries | Where-Object { !$_.PSIsContainer })
if ($files.Count -ne $allowed.Count) { throw 'Workshop package has unexpected or missing files.' }
$inventory = foreach ($file in $files) {
    $relative = [System.IO.Path]::GetRelativePath($packageRoot, $file.FullName).Replace('\', '/')
    if ($relative -cnotin $allowed) { throw "File is not on the Workshop allowlist: $relative" }
    $fileBytes = [System.IO.File]::ReadAllBytes($file.FullName)
    foreach ($encoding in @([System.Text.Encoding]::UTF8, [System.Text.Encoding]::Unicode)) {
        $scanText = $encoding.GetString($fileBytes)
        if ($scanText -match '\bsk-[A-Za-z0-9_-]{16,}\b' -or $scanText -match '(?i)C:\\Users\\[^\\\s]+\\' -or $scanText -match '-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----') {
            throw "Potential secret or private path in $relative; upload blocked."
        }
    }
    [pscustomobject]@{ File = $relative; Bytes = $file.Length; SHA256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash }
}
[xml]$manifest = Get-Content -LiteralPath (Join-Path $packageRoot 'manifest.xml') -Raw
if ($manifest.Manifest.ID -ne 'probably_ai' -or @($manifest.Manifest.Assemblies.Assembly).Count -ne 1 -or $manifest.Manifest.Assemblies.Assembly -ne 'ProbablyAI.dll') { throw 'Unexpected Workshop manifest assembly list.' }
if (@($manifest.Manifest.Capabilities.Capability).Count -ne 1 -or $manifest.Manifest.Capabilities.Capability -ne 'network') { throw 'Unexpected Workshop capability list.' }
$inventory
