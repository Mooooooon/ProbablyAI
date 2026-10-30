param(
    [string]$GameDirectory = 'C:\Program Files (x86)\Steam\steamapps\common\Probably Stolen',
    [switch]$Install
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot 'ProbablyAI.csproj'
if (!(Test-Path -LiteralPath (Join-Path $GameDirectory 'Probably Stolen_Data\Managed\Assembly-CSharp.dll'))) {
    throw "Game assemblies not found in $GameDirectory"
}
& dotnet build $projectFile --configuration Release "-p:GameDirectory=$GameDirectory" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$package = Join-Path $projectRoot 'dist\ProbablyAI'
New-Item -ItemType Directory -Force -Path (Join-Path $package 'Textures') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $package 'docs') | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'bin\Release\netstandard2.1\ProbablyAI.dll') -Destination $package
Copy-Item -LiteralPath (Join-Path $projectRoot 'manifest.xml') -Destination $package
Copy-Item -LiteralPath (Join-Path $projectRoot 'assets\deepseek-npc.png') -Destination (Join-Path $package 'Textures\deepseek-npc.png')
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $package
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\hands-api.md') -Destination (Join-Path $package 'docs\hands-api.md')
if ($Install) {
    if (Get-Process -Name 'Probably Stolen' -ErrorAction SilentlyContinue) {
        throw 'Close Probably Stolen before installing the build.'
    }
    $destination = Join-Path $GameDirectory 'Mods\ProbablyAI'
    New-Item -ItemType Directory -Force -Path (Join-Path $destination 'Textures') | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $destination 'docs') | Out-Null
    foreach ($file in @('ProbablyAI.dll', 'manifest.xml', 'README.md', 'Textures\deepseek-npc.png', 'docs\hands-api.md')) {
        Copy-Item -LiteralPath (Join-Path $package $file) -Destination (Join-Path $destination $file)
    }
    Write-Output "Installed: $destination"
}
Write-Output "Package: $package"
