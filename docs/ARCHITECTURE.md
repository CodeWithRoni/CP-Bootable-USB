# CP Bootable USB — Solution Architecture

**COMPUTER PLUS** · Developed by MD AMZAD HOSSAIN RONI
Fokir Plaza, Ground Floor, Shop No #04, Bera, Pabna · +8801754 614 614 | +8801913 354 321

> এই ডকুমেন্টে পুরো সিস্টেমের নকশা আছে। কোডের নাম ও কমান্ড ইংরেজিতে রাখা হয়েছে, ব্যাখ্যা বাংলায়।

---

## ১. মূল সিদ্ধান্তসমূহ (চূড়ান্ত)

| বিষয় | সিদ্ধান্ত | কারণ |
|---|---|---|
| পার্টিশন টেবিল | **শুধু MBR** (hybrid MBR/GPT নয়) | একই USB Legacy BIOS ও UEFI দুই মোডেই বুট করে; Secure Boot চালু থাকলেও। Rufus/Ventoy একই পদ্ধতি। |
| বুট ফাইল সেটআপ | `bootsect /nt60` + ADK-এর BCD টেমপ্লেট, `bcdedit /store` দিয়ে কাস্টমাইজ | `bcdboot` WinPE-র RAM-disk এন্ট্রি বানাতে পারে না। |
| UsbMaker | .NET 8 WPF (x64) | সাধারণ Windows 10/11-এ চলে। |
| BootLauncher | **.NET Framework 4.8 WPF** (x64) | WinPE-NetFx আসলে .NET Framework 4.8; .NET 8 WinPE-তে ভারী ও অনির্ভরযোগ্য। |
| WinPE | **x64 WinPE** (বাধ্যতামূলক) + **x86 WinPE** (ঐচ্ছিক, `boot_x86.wim`) | x64 WinPE সব 64-bit PC-তে BIOS ও UEFI দুই মোডে বুট হয়, আর DISM দিয়ে 32-bit Windows-ও ইনস্টল করতে পারে। x86 WinPE থাকলে পুরনো 32-bit-only PC-তেও বুট হয় (শুধু Legacy BIOS মেনুতে)। |
| BootLauncher বিল্ড | **AnyCPU** (.NET Framework 4.8) | একই `BootLauncher.exe` x64 আর x86 দুই WinPE-তেই নিজস্ব আর্কিটেকচারে চলে। |
| সাপোর্টেড Windows | 7 / 8 / 8.1 / 10 / 11 — 32-bit ও 64-bit | দুইটি ইনস্টল ইঞ্জিনের মাধ্যমে (নিচে দেখুন)। |
| ডেটা পার্টিশন | NTFS (ডিফল্ট), exFAT ঐচ্ছিক | ৪ GB-র বড় ISO, crash-এ বেশি নিরাপদ। |

---

## ২. সামগ্রিক কাঠামো

```
┌──────────────────────── টেকনিশিয়ানের PC (Windows 10/11) ────────────────────────┐
│  Build-WinPE.ps1 (Phase 3)          UsbMaker.exe (.NET 8 WPF, Phase 1)          │
│  ADK copype amd64                   USB detect (WMI + WM_DEVICECHANGE)          │
│  + OCs (NetFx, PS, WMI, DISM...)    Safety guard (system/boot disk বাদ)          │
│  + BootLauncher.exe       Payload\  DiskPart: MBR → BOOT + DATA                 │
│  + startnet.cmd         ─────────▶  bootsect /nt60 + bcdedit /store             │
│  → boot.wim + media                 Payload কপি, config.json লেখা                │
│                                     মোড: Create / Update Branding Only          │
└──────────────────────────────────────────────┬──────────────────────────────────┘
                                               ▼
                      ┌──────────── USB ফ্ল্যাশ ড্রাইভ (MBR) ────────────┐
                      │ P1 CP_WIN_BOOT  FAT32 1 GB  ACTIVE  (type 0x0C)  │
                      │ P2 CP_WIN_DATA  NTFS  বাকি সব জায়গা             │
                      └────────────────────────┬─────────────────────────┘
     BIOS: MBR → bootmgr → \Boot\BCD            │    UEFI: \EFI\Boot\bootx64.efi → \EFI\Microsoft\Boot\BCD
                                                ▼
┌────────────────────────── টার্গেট PC: WinPE (RAM disk X:) ──────────────────────────┐
│ startnet.cmd → wpeinit → BootLauncher.exe (.NET 4.8 WPF, fullscreen, Phase 2)      │
│  ├─ DATA পার্টিশন খোঁজে marker ফাইল \CP\cp_data.tag দিয়ে                          │
│  ├─ config.json লোড (না থাকলে বিল্ট-ইন COMPUTER PLUS ডিফল্ট)                      │
│  ├─ হার্ডওয়্যার তথ্য: WMI (CPU, RAM, ডিস্ক) + firmware মোড (PEFirmwareType)      │
│  ├─ ISO স্ক্যান → কার্ড (নাম, সাইজ, ভার্সন, x86/x64, ইঞ্জিন ব্যাজ)                 │
│  ├─ ইঞ্জিন A: Windows Setup  (8/8.1/10/11 x64)                                   │
│  └─ ইঞ্জিন B: Direct Apply   (সব x86 + Windows 7)                                │
│ ইউটিলিটি বার: Reboot · Shutdown · Refresh · Command Prompt                       │
└─────────────────────────────────────────────────────────────────────────────────────┘
```

---

## ৩. দুইটি ইনস্টল ইঞ্জিন

| ইঞ্জিন | কখন | কাজের ধাপ |
|---|---|---|
| **A — Windows Setup** | Windows 8 / 8.1 / 10 / 11, শুধু x64 | (ঐচ্ছিক) LabConfig bypass → ISO mount → `<ISO>:\sources\setup.exe` চালু। Setup নিজেই ফাইল টার্গেট ডিস্কে কপি করে, তাই রিবুটের পর USB লাগে না। |
| **B — Direct Apply** | সব x86 ISO, এবং Windows 7 (x86/x64); config-এ `forceDirectApply` দিলে সবক্ষেত্রে | ISO mount → edition বাছাই → টার্গেট ডিস্ক বাছাই ও নিশ্চিতকরণ → পার্টিশন (UEFI হলে GPT: EFI+MSR+Windows, Legacy হলে MBR: System+Windows) → `dism /Apply-Image` → (Win7 হলে) `dism /Add-Driver` → `bcdboot W:\Windows /s S: /f ALL` → রিবুট। |

**ফার্মওয়্যার সামঞ্জস্য (BootLauncher আগেই যাচাই করে সতর্ক করবে):**

| Windows | Legacy BIOS | UEFI |
|---|---|---|
| 7 x86 | ✅ | ❌ CSM/Legacy মোডে বুট করুন |
| 7 x64 | ✅ | ⚠️ CSM চালু ও Secure Boot বন্ধ থাকলে |
| 8 / 8.1 / 10 x86 | ✅ | ❌ Legacy মোডে বুট করুন |
| 8 / 8.1 / 10 x64 | ✅ | ✅ |
| 11 (শুধু x64) | ⚠️ bypass লাগবে | ✅ |

**32-bit WinPE থেকে বুট করলে (পুরনো PC):** 32-bit ISO-র `setup.exe` সরাসরি চলে (ইঞ্জিন A)। 64-bit Windows এই PC-তে চলবেই না (CPU 64-bit নয়), তাই BootLauncher সেগুলো নিষ্ক্রিয় দেখাবে।

### ৩.১ 32-bit বুট মেনু (শুধু Legacy BIOS)

`boot_x86.wim` থাকলে UsbMaker BIOS BCD-তে দ্বিতীয় এন্ট্রি যোগ করে:

```
bcdedit /store B:\Boot\BCD /copy {default} /d "CP Bootable USB - COMPUTER PLUS (32-bit - old PCs)"
bcdedit /store B:\Boot\BCD /set {new} device   ramdisk=[boot]\sources\boot_x86.wim,{ramdiskoptions}
bcdedit /store B:\Boot\BCD /set {new} osdevice ramdisk=[boot]\sources\boot_x86.wim,{ramdiskoptions}
bcdedit /store B:\Boot\BCD /displayorder {new} /addlast
bcdedit /store B:\Boot\BCD /timeout 10
```

- মেনু ১০ সেকেন্ড দেখায়; কিছু না চাপলে 64-bit চালু হয়।
- UEFI BCD-তে শুধু 64-bit এন্ট্রি থাকে (64-bit UEFI 32-bit WinPE চালাতে পারে না)।
- বুট পার্টিশন নিজে থেকেই 1536 MB হয়।
- **Phase 3 নোট:** নতুন ADK-তে x86 WinPE নেই। 32-bit WinPE বানাতে **ADK for Windows 10 version 2004**-এর WinPE add-on লাগবে (আলাদা PC/VM-এ বা আলাদা সময়ে)। Build স্ক্রিপ্ট দুইটা আলাদাভাবে বানিয়ে `boot.wim` + `boot_x86.wim` হিসেবে Payload-এ রাখবে।

**Windows 7 ড্রাইভার:** `CP_WIN_DATA\Drivers\Win7\x86` ও `...\x64` ফোল্ডারে USB 3.0 / NVMe / চিপসেট ড্রাইভার (.inf) রাখলে Direct Apply-এর সময় স্বয়ংক্রিয়ভাবে যোগ হবে।

**Windows 11 bypass (ইঞ্জিন A):** চেকবক্স অনুযায়ী `HKLM\SYSTEM\Setup\LabConfig`-এ DWORD = 1:
`BypassTPMCheck`, `BypassSecureBootCheck`, `BypassRAMCheck`, `BypassCPUCheck`, `BypassStorageCheck`।
ইঞ্জিন B-তে এই চেক এমনিতেই হয় না।

---

## ৪. USB লেআউট

| | P1 — `CP_WIN_BOOT` | P2 — `CP_WIN_DATA` |
|---|---|---|
| ফাইল সিস্টেম | FAT32, 1 GB, **active**, type `0x0C` | NTFS (বা exFAT), বাকি সব |
| কাজ | শুধু বুট | ISO + ব্র্যান্ডিং + ড্রাইভার |
| ভেতরে | `bootmgr`, `bootmgr.efi`, `Boot\BCD`, `Boot\boot.sdi`, `EFI\Boot\bootx64.efi`, `EFI\Microsoft\Boot\BCD`, `sources\boot.wim`, (ঐচ্ছিক) `sources\boot_x86.wim`, `cp_boot.tag` | `ISO\`, `config.json`, `CP\logo.png`, `CP\cp_data.tag`, `CP\logs\`, `Drivers\Win7\x86`, `Drivers\Win7\x64` |

- বুট পার্টিশন ডিস্কের **শুরুতে**: কিছু পুরনো BIOS বড় USB-র শেষ দিক পড়তে পারে না।
- `0x0C` টাইপ: `0xEF` দিলে Windows পার্টিশনটা লুকিয়ে ফেলে, তখন "Update Branding Only" কাজ করবে না।
- Windows 10 1703+ এবং WinPE রিমুভেবল ড্রাইভের দুই পার্টিশনই দেখায়।

---

## ৫. বুট কৌশল (UsbMaker যা করে)

```
select disk N
attributes disk clear readonly noerr
clean
convert mbr
create partition primary size=1024
set id=0c override
format fs=fat32 quick label=CP_WIN_BOOT
active
assign letter=<B>
create partition primary
format fs=ntfs quick label=CP_WIN_DATA
assign letter=<D>
```

তারপর:
1. `bootsect.exe /nt60 <B>: /force /mbr` — MBR ও partition boot sector-এ BOOTMGR কোড লেখে (Legacy BIOS)।
2. `Payload\Media\*` → `<B>:\` কপি (bootx64.efi Microsoft-signed, তাই Secure Boot পাস)।
3. দুই BCD store কাস্টমাইজ:
   ```
   bcdedit /store <B>:\Boot\BCD               /set {default} description "CP Bootable USB - COMPUTER PLUS"
   bcdedit /store <B>:\EFI\Microsoft\Boot\BCD /set {default} description "CP Bootable USB - COMPUTER PLUS"
   bcdedit /store <each> /timeout 0
   ```
   ADK-এর এন্ট্রিতে `ramdisk=[boot]\sources\boot.wim` থাকে; `[boot]` মানে "যে পার্টিশন থেকে বুট হয়েছে" — তাই ড্রাইভ লেটার যা-ই হোক কাজ করে।
4. `<D>:\`-এ `config.json`, লোগো, marker ফাইল, `ISO\` ও `Drivers\` ফোল্ডার।

**বুট মোড শনাক্ত (WinPE-তে):** `HKLM\SYSTEM\CurrentControlSet\Control\PEFirmwareType` → `1` = Legacy (MBR), `2` = UEFI (GPT)।

**নিরাপত্তা:**
- শুধু USB bus-এর ডিস্ক দেখায়; system/boot ডিস্ক সবসময় বাদ।
- মুছে ফেলার আগে ডিস্ক নম্বর টাইপ করে নিশ্চিত করতে হয়; ২৫৬ GB-র বড় হলে বাড়তি সতর্কবার্তা।
- ফরম্যাটের ঠিক আগে ডিস্কের পরিচয় (UniqueId + সাইজ) আবার মিলিয়ে দেখা হয় — মাঝে USB বদলালে কাজ থেমে যায়।

---

## ৬. `config.json` স্কিমা (v1)

```json
{
  "schemaVersion": 1,
  "branding": {
    "softwareName": "CP Bootable USB",
    "brandName": "COMPUTER PLUS",
    "tagline": "Fast & Reliable Windows Deployment - We Always Ready To Help You.",
    "developerCredit": "Developed by MD AMZAD HOSSAIN RONI",
    "logoPath": "CP\\logo.png"
  },
  "contact": {
    "phones": ["+8801754 614 614", "+8801913 354 321"],
    "address": "Fokir Plaza, Ground Floor, Shop No #04, Bera, Pabna"
  },
  "notice": "Welcome to COMPUTER PLUS Deployment Environment. ...",
  "theme": {
    "preset": "DarkBlueCyan",
    "background": "#0F172A",
    "surface": "#1E293B",
    "accent": "#0284C7",
    "accentLight": "#38BDF8",
    "textPrimary": "#F1F5F9",
    "textMuted": "#94A3B8"
  },
  "launcher": {
    "isoFolders": ["ISO", ""],
    "scanAllDrives": true,
    "defaultBypass": { "tpm": false, "secureBoot": false, "ram": false, "cpu": false, "storage": false },
    "showCommandPrompt": true,
    "confirmBeforeInstall": true
  },
  "install": {
    "forceDirectApply": false,
    "driversFolder": "Drivers",
    "injectDriversForWin7": true,
    "autoReboot": true
  },
  "meta": { "createdBy": "UsbMaker 1.0.0", "createdAt": "2026-11-14T12:20:00+06:00" }
}
```

**নিয়ম:** প্রতিটা ফিল্ড ঐচ্ছিক। কোনো ফিল্ড না থাকলে বা ভুল হলে বিল্ট-ইন ডিফল্ট ব্যবহার হয় (`BrandingDefaults.cs`)। ভুল হেক্স রঙ বাদ দিয়ে ডিফল্ট রঙ নেওয়া হয়। JSON-এ কমেন্ট ও trailing comma চলে। `schemaVersion` ভবিষ্যতের আপগ্রেডের জন্য।

---

## ৭. ফোল্ডার কাঠামো

```
CP Bootable USB\
├─ CPBootableUSB.sln
├─ Directory.Build.props
├─ src\
│  ├─ CP.Shared\          netstandard2.0 — দুই অ্যাপেই ব্যবহৃত (config মডেল, ডিফল্ট, ConfigService)
│  ├─ UsbMaker\           net8.0-windows WPF — Phase 1
│  │  ├─ Themes\, Views\, ViewModels\, Models\, Services\
│  │  └─ Payload\         Phase 3-এর আউটপুট (Media\ + Tools\bootsect.exe)
│  └─ BootLauncher\       net48 WPF — Phase 2
├─ build\                 Build-WinPE.ps1, startnet.cmd — Phase 3
├─ assets\                logo.png, app.ico
└─ docs\ARCHITECTURE.md
```

## ৮. WinPE উপাদান (Phase 3)

- Optional components (en-US language pack সহ): WinPE-WMI, WinPE-NetFx, WinPE-Scripting, WinPE-PowerShell, WinPE-StorageWMI, WinPE-DismCmdlets
- BootLauncher → `X:\CP\BootLauncher.exe`
- `startnet.cmd`: `wpeinit` → BootLauncher লুপে চালায়; ক্র্যাশ বা বন্ধ হলে হঠাৎ রিবুট না করে Command Prompt দেখায়।
- Secure Boot সার্টিফিকেট পরিবর্তন (2011 CA → 2023 CA): নতুন ADK হলে 2023-signed বুট মিডিয়া বেছে নেওয়ার অপশন স্ক্রিপ্টে থাকবে।

## ৯. বিল্ড ক্রম

কোড লেখা হবে 1 → 2 → 3 ক্রমে, কিন্তু প্রথম আসল টেস্টের আগে Phase 3 একবার চালিয়ে `Payload\` তৈরি করতে হবে।
বিল্ড PC-তে লাগবে: Windows ADK + WinPE add-on, Visual Studio 2022 (.NET 8 SDK + .NET Framework 4.8 targeting pack)।
