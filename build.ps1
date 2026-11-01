# Builds the release ZIP: compiles Tools\VrrTest.exe and packages the VRR fix.
#   .\build.ps1                 -> dist\Atlas8-VRR-Fix-<version>.zip
#   .\build.ps1 -SkipCompile    -> reuse an existing vrr-fix\tools\VrrTest\VrrTest.exe
[CmdletBinding()]
param([switch]$SkipCompile)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$fix = Join-Path $root 'vrr-fix'
$engine = Join-Path $fix 'engine'
$toolDir = Join-Path $fix 'tools\VrrTest'

$versionLine = Select-String -LiteralPath (Join-Path $engine 'AcerAtlasVrr.ps1') -Pattern "^\`$version = '([^']+)'" | Select-Object -First 1
if (-not $versionLine) { throw 'Version not found in AcerAtlasVrr.ps1.' }
$version = $versionLine.Matches[0].Groups[1].Value
$name = "Atlas8-VRR-Fix-$version"

# Syntax check every PowerShell file.
foreach ($ps in Get-ChildItem -LiteralPath $engine -Filter *.ps1) {
    $errors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($ps.FullName, [ref]$null, [ref]$errors) | Out-Null
    if ($errors) { throw "Syntax errors in $($ps.Name): $($errors -join '; ')" }
}

if (-not $SkipCompile) {
    Write-Host 'Compiling VrrTest.exe ...'
    & cmd.exe /c "`"$(Join-Path $toolDir 'build.bat')`""
    if ($LASTEXITCODE -ne 0) { throw 'VrrTest build failed (Visual Studio C++ tools required).' }
}
$exe = Join-Path $toolDir 'VrrTest.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "$exe is missing." }

$dist = Join-Path $root 'dist'
$stage = Join-Path $dist $name
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Path (Join-Path $stage 'engine'), (Join-Path $stage 'Tools') -Force | Out-Null

Copy-Item -LiteralPath (Join-Path $fix 'SETUP.bat'), (Join-Path $fix 'README.txt') -Destination $stage
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $stage 'LICENSE.txt')
Copy-Item -Path (Join-Path $engine '*') -Destination (Join-Path $stage 'engine')
Copy-Item -LiteralPath $exe -Destination (Join-Path $stage 'Tools')

# Windows batch files must use CRLF line endings.
foreach ($file in Get-ChildItem -LiteralPath $stage -Recurse -File -Include *.bat, *.ps1, *.vbs, *.txt) {
    $text = [IO.File]::ReadAllText($file.FullName)
    $text = ($text -replace "`r`n", "`n") -replace "`n", "`r`n"
    [IO.File]::WriteAllText($file.FullName, $text, (New-Object Text.UTF8Encoding($false)))
}

$zip = Join-Path $dist "$name.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
# Write entries with forward slashes (Compress-Archive in Windows PowerShell uses backslashes).
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem -LiteralPath $stage -Recurse -File) {
        $entry = "$name/" + $file.FullName.Substring($stage.Length + 1).Replace('\', '/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entry,
            [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}
finally { $archive.Dispose() }
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
"$hash  $name.zip" | Set-Content -LiteralPath "$zip.sha256" -Encoding ASCII

Write-Host "Built $zip"
Write-Host "SHA-256 $hash"
