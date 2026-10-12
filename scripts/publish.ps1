<#
.SYNOPSIS
    Publishes a self-contained ScaleOS for a runtime, so the target machine doesn't need .NET installed.

.EXAMPLE
    ./scripts/publish.ps1 -Runtime linux-arm64     # WSL on an ARM PC, or a 64-bit Raspberry Pi
    ./scripts/publish.ps1 -Runtime linux-x64       # WSL on an Intel/AMD PC
    ./scripts/publish.ps1 -Runtime win-arm64

    The result goes to out/<runtime>/ and runs as out/<runtime>/sos (sos.exe on Windows).
#>
param(
    [Parameter(Mandatory)][string]$Runtime,
    [string]$Configuration = 'Release',
    [string]$Output
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (-not $Output) { $Output = Join-Path $repo "out/$Runtime" }

if (Test-Path $Output) { Remove-Item $Output -Recurse -Force }

# The core and the PowerShell host are separate programs; publish each for the target runtime.
# The core's own build copies a host built for this machine into host/, so the real host is published over it.
dotnet publish (Join-Path $repo 'src/Sos.Core') -c $Configuration -r $Runtime --self-contained -o $Output
if ($LASTEXITCODE) { throw 'Publishing the core failed.' }

$hostDir = Join-Path $Output 'host'
if (Test-Path $hostDir) { Remove-Item $hostDir -Recurse -Force }
dotnet publish (Join-Path $repo 'src/Sos.Host') -c $Configuration -r $Runtime --self-contained -o $hostDir
if ($LASTEXITCODE) { throw 'Publishing the PowerShell host failed.' }

Copy-Item (Join-Path $repo 'src/Sos.Sdk') (Join-Path $Output 'sdk') -Recurse -Force
Copy-Item (Join-Path $repo 'apps') (Join-Path $Output 'apps') -Recurse -Force

$size = (Get-ChildItem $Output -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
"Published ScaleOS for $Runtime to $Output ({0:N0} MB)" -f $size
