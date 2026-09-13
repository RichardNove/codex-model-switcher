# Builds CodexModelSwitcher.exe with an embedded application icon and high-DPI manifest,
# then runs the program's built-in self-test.
#
# Only the C# compiler that ships with the .NET Framework is required — no Visual Studio,
# no .NET SDK, and no build server.

param(
    [string]$OutDir = (Join-Path $PSScriptRoot 'dist')
)

$ErrorActionPreference = 'Stop'

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) {
    throw "C# compiler not found at $csc. Install .NET Framework 4.x."
}

$source = Join-Path $PSScriptRoot 'CodexModelSwitcher.cs'
$iconSource = Join-Path $PSScriptRoot 'build\IconGenerator.cs'
$icon = Join-Path $PSScriptRoot 'build\CodexModelSwitcher.ico'
$manifest = Join-Path $PSScriptRoot 'build\app.manifest'
$exe = Join-Path $OutDir 'CodexModelSwitcher.exe'

New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

Write-Host 'Generating application icon...'
$iconBuilder = Join-Path ([System.IO.Path]::GetTempPath()) 'CodexModelSwitcher-IconGenerator.exe'
& $csc /nologo /target:exe /optimize+ /out:$iconBuilder `
    /reference:System.dll /reference:System.Drawing.dll $iconSource
if ($LASTEXITCODE -ne 0) { throw "Icon generator failed to compile ($LASTEXITCODE)." }
& $iconBuilder $icon
if ($LASTEXITCODE -ne 0) { throw "Icon generation failed ($LASTEXITCODE)." }

Write-Host 'Compiling CodexModelSwitcher.exe...'
& $csc /nologo /warn:4 /target:winexe /optimize+ /platform:anycpu `
    /win32icon:$icon /win32manifest:$manifest `
    /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll `
    /reference:System.Security.dll /reference:System.Windows.Forms.dll `
    /out:$exe $source
if ($LASTEXITCODE -ne 0) { throw "Compilation failed ($LASTEXITCODE)." }

Write-Host 'Running self-test...'
$process = Start-Process -FilePath $exe -ArgumentList '--self-test' -Wait -PassThru -NoNewWindow
if ($process.ExitCode -ne 0) { throw "Self-test failed (exit code $($process.ExitCode))." }

$hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
Write-Host ''
Write-Host "Built: $exe"
Write-Host "SHA256: $hash"
