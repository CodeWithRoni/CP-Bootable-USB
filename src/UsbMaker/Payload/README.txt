CP Bootable USB - WinPE Payload folder
=======================================

This folder is filled automatically by build\Build-WinPE.ps1 (Phase 3).
UsbMaker copies Media\* onto the CP_WIN_BOOT partition.

Expected layout:

Payload\
  payload.json                      (optional: version info written by the build script)
  Media\
    bootmgr
    bootmgr.efi
    Boot\BCD
    Boot\boot.sdi
    EFI\Boot\bootx64.efi
    EFI\Microsoft\Boot\BCD
    sources\boot.wim                (custom 64-bit WinPE with BootLauncher)  - REQUIRED
    sources\boot_x86.wim            (custom 32-bit WinPE with BootLauncher)  - OPTIONAL
                                    If present, UsbMaker adds a "32-bit - old PCs" entry to the
                                    Legacy BIOS boot menu and uses a 1536 MB boot partition.
  Tools\
    bootsect.exe                    (from ADK: Deployment Tools\amd64\BCDBoot\bootsect.exe)

If Tools\bootsect.exe is missing, UsbMaker falls back to the default ADK install path.
