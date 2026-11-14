<#
.SYNOPSIS
    Silently installs Windows ADK Deployment Tools + the Windows PE add-on (used by GitHub Actions).
    Skips installation when the requested WinPE architecture is already present.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$AdkUrl,
    [Parameter(Mandatory = $true)][string]$WinPEUrl,
    [ValidateSet('amd64', 'x86')][string]$Arch = 'amd64'
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$adk = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\Assessment and Deployment Kit'
$winpeWim = Join-Path $adk "Windows Preinstallation Environment\$Arch\en-us\winpe.wim"
if (Test-Path $winpeWim) { Write-Host "WinPE ($Arch) already installed."; return }

$tmp = Join-Path $env:RUNNER_TEMP 'adk'
if (-not $env:RUNNER_TEMP) { $tmp = Join-Path $env:TEMP 'adk' }
New-Item -ItemType Directory -Force $tmp | Out-Null

function Install([string]$url, [string]$exe, [string]$feature) {
    $path = Join-Path $tmp $exe
    Write-Host "Downloading $exe ..."
    Invoke-WebRequest -Uri $url -OutFile $path -UseBasicParsing
    Write-Host "Installing $feature (this downloads the components, several minutes) ..."
    $log = Join-Path $tmp "$exe.log"
    $p = Start-Process -FilePath $path -ArgumentList "/quiet /norestart /ceip off /features $feature /log `"$log`"" -Wait -PassThru
    if ($p.ExitCode -ne 0 -and $p.ExitCode -ne 3010) {
        Get-Content $log -Tail 40 -ErrorAction SilentlyContinue
        throw "$exe failed with exit code $($p.ExitCode)"
    }
}

Install $AdkUrl 'adksetup.exe' 'OptionId.DeploymentTools'
Install $WinPEUrl 'adkwinpesetup.exe' 'OptionId.WindowsPreinstallationEnvironment'

if (-not (Test-Path $winpeWim)) { throw "WinPE ($Arch) was not installed: $winpeWim missing." }
Write-Host "Windows ADK + WinPE ($Arch) installed." -ForegroundColor Green
