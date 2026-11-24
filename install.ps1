#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the Acer Nitro plugin and installs it into FanControl's Plugins folder.
.DESCRIPTION
    Builds in Release, then (elevated, one UAC prompt) stops FanControl, copies the DLL
    into <FanControlDir>\Plugins and starts FanControl again.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\install.ps1
#>
param(
    [string]$FanControlDir = 'C:\Program Files (x86)\FanControl'
)
$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'src\FanControl.AcerNitro.csproj'
dotnet build $project -c Release -nologo "-p:FanControlDir=$FanControlDir"
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)." }

$dll = Join-Path $PSScriptRoot 'src\bin\Release\net8.0-windows\FanControl.AcerNitro.dll'
$plugins = Join-Path $FanControlDir 'Plugins'
$exe = Join-Path $FanControlDir 'FanControl.exe'

$inner = "Get-Process FanControl -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep 2; " +
         "New-Item -ItemType Directory -Force '$plugins' | Out-Null; Copy-Item -LiteralPath '$dll' '$plugins' -Force"
$p = Start-Process powershell.exe -Verb RunAs -Wait -PassThru -ArgumentList @('-NoProfile', '-Command', $inner)
if ($p.ExitCode -ne 0) { throw "Install step failed ($($p.ExitCode))." }

Start-Process $exe
Write-Host "Installed to $plugins and restarted FanControl." -ForegroundColor Green
