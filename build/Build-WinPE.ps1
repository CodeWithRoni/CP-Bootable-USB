<#
.SYNOPSIS
    Builds the CP Bootable USB WinPE payload (custom boot.wim + boot media) with the Windows ADK.

.DESCRIPTION
    COMPUTER PLUS · Developed by MD AMZAD HOSSAIN RONI

    1. Copies the ADK WinPE boot media (bootmgr, BCD stores, EFI loader, boot.sdi).
    2. Mounts winpe.wim and adds: WinPE-WMI, WinPE-NetFx, WinPE-Scripting, WinPE-PowerShell,
       WinPE-StorageWMI, WinPE-DismCmdlets (+ en-us language packs).
    3. Copies BootLauncher to X:\CP\ and replaces startnet.cmd so the launcher starts immediately.
    4. Commits + re-exports the image (max compression).
    5. Writes the UsbMaker payload:
         amd64 → <OutDir>\Media\...  +  <OutDir>\Tools\bootsect.exe  +  <OutDir>\payload.json
         x86   → <OutDir>\Media\sources\boot_x86.wim   (optional 32-bit WinPE for old PCs)

    Requirements (run as Administrator):
      amd64: Windows ADK 10.1.26100 + WinPE add-on   (Deployment Tools + Windows PE)
      x86  : Windows ADK for Windows 10, version 2004 + WinPE add-on (last ADK with 32-bit WinPE)

.EXAMPLE
    .\Build-WinPE.ps1 -LauncherDir ..\src\BootLauncher\bin\Release\net48
.EXAMPLE
    .\Build-WinPE.ps1 -Arch x86 -LauncherDir ..\src\BootLauncher\bin\Release\net48
#>
[CmdletBinding()]
param(
    [ValidateSet('amd64', 'x86')]
    [string]$Arch = 'amd64',

    [Parameter(Mandatory = $true)]
    [string]$LauncherDir,

    [string]$OutDir = (Join-Path $PSScriptRoot '..\src\UsbMaker\Payload'),

    [string]$WorkDir = (Join-Path $env:TEMP "cp-winpe-$Arch"),

    [string]$Version = '1.0.0',

    # Use the boot manager signed with the "Windows UEFI CA 2023" certificate (only for PCs whose
    # firmware already trusts it). Default = 2011-signed boot manager, which every Secure Boot PC accepts.
    [switch]$UseBootEx
)

$ErrorActionPreference = 'Stop'
function Step([string]$m) { Write-Host "`n==> $m" -ForegroundColor Cyan }
function Invoke-Dism([string[]]$DismArgs) {
    & $script:Dism @DismArgs "/LogPath:$WorkDir\dism.log"
    if ($LASTEXITCODE -ne 0) { throw "DISM failed ($LASTEXITCODE): $($DismArgs -join ' ')" }
}

# ---------------------------------------------------------------- checks
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script as Administrator (DISM needs it).'
}

$adk = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\Assessment and Deployment Kit'
$peRoot = Join-Path $adk "Windows Preinstallation Environment\$Arch"
$script:Dism = Join-Path $adk 'Deployment Tools\amd64\DISM\dism.exe'
if (-not (Test-Path $script:Dism)) { $script:Dism = "$env:SystemRoot\System32\dism.exe" }
$bootsect = Join-Path $adk 'Deployment Tools\amd64\BCDBoot\bootsect.exe'

if (-not (Test-Path "$peRoot\en-us\winpe.wim")) {
    throw "WinPE ($Arch) not found at $peRoot. Install the Windows ADK + WinPE add-on" +
          $(if ($Arch -eq 'x86') { ' for Windows 10 version 2004 (newer ADKs have no 32-bit WinPE).' } else { '.' })
}

$LauncherDir = (Resolve-Path $LauncherDir).Path
if (-not (Test-Path (Join-Path $LauncherDir 'BootLauncher.exe'))) { throw "BootLauncher.exe not found in $LauncherDir" }
$OutDir = [IO.Path]::GetFullPath($OutDir)

$media = Join-Path $WorkDir 'media'
$mount = Join-Path $WorkDir 'mount'
$wim = Join-Path $media 'sources\boot.wim'

Write-Host "CP Bootable USB — WinPE build ($Arch), version $Version" -ForegroundColor Green
Write-Host "ADK      : $adk"
Write-Host "DISM     : $script:Dism"
Write-Host "Launcher : $LauncherDir"
Write-Host "Output   : $OutDir"

# ---------------------------------------------------------------- clean work dir
Step 'Preparing work folder'
if (Test-Path "$mount\Windows") {
    Write-Warning 'An old image is still mounted — discarding it.'
    & $script:Dism /Unmount-Image "/MountDir:$mount" /Discard | Out-Null
}
& $script:Dism /Cleanup-Mountpoints | Out-Null
if (Test-Path $WorkDir) { Remove-Item $WorkDir -Recurse -Force }
New-Item -ItemType Directory -Force $media, $mount, (Join-Path $media 'sources') | Out-Null

# ---------------------------------------------------------------- boot media (like copype)
Step 'Copying WinPE boot media'
Copy-Item "$peRoot\Media\*" $media -Recurse -Force
Copy-Item "$peRoot\en-us\winpe.wim" $wim -Force
attrib -r -s -h "$media\*" /s /d | Out-Null

if ($UseBootEx) {
    if (Test-Path "$media\EFI_EX") {
        Step 'Using 2023-CA signed boot manager (EFI_EX)'
        Copy-Item "$media\EFI_EX\*" "$media\EFI" -Recurse -Force
        if (Test-Path "$media\bootmgr_EX.efi") { Copy-Item "$media\bootmgr_EX.efi" "$media\bootmgr.efi" -Force }
    } else {
        Write-Warning 'This ADK has no EFI_EX boot files — keeping the standard boot manager.'
    }
}
# Remove the alternative boot files and extra UI languages (keep en-us) to save space.
Remove-Item "$media\EFI_EX", "$media\bootmgr_EX.efi" -Recurse -Force -ErrorAction SilentlyContinue
Get-ChildItem $media -Directory -Recurse |
    Where-Object { $_.Name -match '^[a-z]{2}-[a-z]{2,4}$' -and $_.Name -ne 'en-us' } |
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

# ---------------------------------------------------------------- customise boot.wim
Step 'Mounting boot.wim'
Invoke-Dism @('/Mount-Image', "/ImageFile:$wim", '/Index:1', "/MountDir:$mount")

try {
    $ocs = Join-Path $peRoot 'WinPE_OCs'
    $packages = 'WinPE-WMI', 'WinPE-NetFx', 'WinPE-Scripting', 'WinPE-PowerShell', 'WinPE-StorageWMI', 'WinPE-DismCmdlets'
    foreach ($p in $packages) {
        Step "Adding $p"
        $cab = Join-Path $ocs "$p.cab"
        if (-not (Test-Path $cab)) { throw "Package not found: $cab" }
        Invoke-Dism @("/Image:$mount", '/Add-Package', "/PackagePath:$cab")
        $lang = Join-Path $ocs "en-us\$($p)_en-us.cab"
        if (Test-Path $lang) { Invoke-Dism @("/Image:$mount", '/Add-Package', "/PackagePath:$lang") }
    }

    Step 'Copying BootLauncher to X:\CP'
    $cp = Join-Path $mount 'CP'
    New-Item -ItemType Directory -Force $cp | Out-Null
    Get-ChildItem $LauncherDir -File | Where-Object { $_.Extension -in '.exe', '.dll', '.config', '.json' } |
        Copy-Item -Destination $cp -Force
    Get-ChildItem $cp | ForEach-Object { Write-Host "   $($_.Name)" }

    Step 'Writing startnet.cmd'
    $startnet = @'
@echo off
title CP Bootable USB - COMPUTER PLUS
echo.
echo   CP Bootable USB - COMPUTER PLUS
echo   Starting, please wait...
echo.
wpeinit
rem High performance power plan (faster DISM / Setup)
powercfg /s 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c >nul 2>&1

:launch
start "" /wait "X:\CP\BootLauncher.exe"
echo.
echo   The CP Bootable USB screen was closed.
echo   Type  exit  to open it again,  or  wpeutil reboot  to restart the PC.
echo.
cmd /k
goto launch
'@
    Set-Content -Path (Join-Path $mount 'Windows\System32\startnet.cmd') -Value $startnet -Encoding Ascii

    Step 'Setting scratch space (512 MB)'
    Invoke-Dism @("/Image:$mount", '/Set-ScratchSpace:512')

    Step 'Committing image'
    Invoke-Dism @('/Unmount-Image', "/MountDir:$mount", '/Commit')
}
catch {
    Write-Warning "Build failed — discarding mounted image. $_"
    & $script:Dism /Unmount-Image "/MountDir:$mount" /Discard | Out-Null
    throw
}

Step 'Optimising boot.wim (export, max compression)'
$optimized = Join-Path $media 'sources\boot-opt.wim'
Invoke-Dism @('/Export-Image', "/SourceImageFile:$wim", '/SourceIndex:1', "/DestinationImageFile:$optimized", '/Compress:max', '/Bootable')
Move-Item $optimized $wim -Force
Write-Host ("   boot.wim = {0:N0} MB" -f ((Get-Item $wim).Length / 1MB))

# ---------------------------------------------------------------- payload
Step "Writing payload to $OutDir"
if ($Arch -eq 'amd64') {
    # Keep an existing 32-bit WinPE (built earlier with -Arch x86) while replacing the media.
    $x86Keep = $null
    if (Test-Path "$OutDir\Media\sources\boot_x86.wim") {
        $x86Keep = Join-Path $env:TEMP 'cp_boot_x86.wim'
        Move-Item "$OutDir\Media\sources\boot_x86.wim" $x86Keep -Force
    }
    if (Test-Path "$OutDir\Media") { Remove-Item "$OutDir\Media" -Recurse -Force }
    New-Item -ItemType Directory -Force "$OutDir\Media", "$OutDir\Tools" | Out-Null
    Copy-Item "$media\*" "$OutDir\Media" -Recurse -Force
    if ($x86Keep) { Move-Item $x86Keep "$OutDir\Media\sources\boot_x86.wim" -Force }
    if (-not (Test-Path $bootsect)) { throw "bootsect.exe not found: $bootsect" }
    Copy-Item $bootsect "$OutDir\Tools\bootsect.exe" -Force

    $adkVersion = (Get-Item $script:Dism).VersionInfo.ProductVersion
    @{
        version    = $Version
        builtAt    = (Get-Date).ToString('s')
        winpeArch  = 'amd64'
        adkVersion = $adkVersion
        bootEx     = [bool]$UseBootEx
    } | ConvertTo-Json | Set-Content "$OutDir\payload.json" -Encoding UTF8
}
else {
    New-Item -ItemType Directory -Force "$OutDir\Media\sources" | Out-Null
    Copy-Item $wim "$OutDir\Media\sources\boot_x86.wim" -Force
}

Get-ChildItem $OutDir -Recurse -File | Select-Object @{n = 'File'; e = { $_.FullName.Substring($OutDir.Length + 1) } }, @{n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } } | Format-Table -AutoSize
Remove-Item $WorkDir -Recurse -Force -ErrorAction SilentlyContinue
Write-Host "`nDone. WinPE ($Arch) payload is ready." -ForegroundColor Green
