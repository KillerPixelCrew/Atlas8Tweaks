# Builds the power button setup: one self-installing exe.
#   .\power-button\build.ps1   -> dist\Atlas8-PowerButton-Setup-<version>.exe
# Requires the .NET SDK (any recent version). The exe targets .NET Framework 4.8, which ships with Windows.
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$here = $PSScriptRoot
$root = Split-Path $here -Parent
$project = Join-Path $here 'src\Atlas8PowerButton.csproj'
$version = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw 'Version not found in Atlas8PowerButton.csproj.' }

$out = Join-Path $here 'out'
if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out -Recurse -Force }
Write-Host "Building Atlas 8 Power Button $version ..."
& dotnet build $project -c Release -o $out --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Power button build failed.' }

$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$name = "Atlas8-PowerButton-Setup-$version.exe"
$target = Join-Path $dist $name
Copy-Item -LiteralPath (Join-Path $out 'Atlas8PowerButton.exe') -Destination $target -Force
$hash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
"$hash  $name" | Set-Content -LiteralPath "$target.sha256" -Encoding ASCII

Write-Host "Built $target"
Write-Host "SHA-256 $hash"
