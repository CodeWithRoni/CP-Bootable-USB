# CP Bootable USB — COMPUTER PLUS

Developed by MD AMZAD HOSSAIN RONI · Fokir Plaza, Ground Floor, Shop No #04, Bera, Pabna · +8801754 614 614 | +8801913 354 321

পুরো নকশা: [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)

## অবস্থা

| Phase | অংশ | অবস্থা |
|---|---|---|
| 1 | `UsbMaker.exe` + `CP.Shared` | ✅ |
| 2 | `BootLauncher.exe` (WinPE বুট স্ক্রিন, দুই ইনস্টল ইঞ্জিন) | ✅ |
| 3 | `build\Build-WinPE.ps1` (64-bit + 32-bit boot.wim + Payload) | ✅ |
| — | Installer (`installer\CPBootableUSB.iss`) | ✅ |

## Release বানানো (সব কিছু GitHub নিজেই বানায়)

1. GitHub → **Releases → Draft a new release** → নতুন tag দিন (যেমন `v1.3.0`) → **Publish**।
2. GitHub Actions ADK ইনস্টল করে 64-bit + 32-bit WinPE বানায় (~১০ মিনিট), তারপর Release-এ যোগ করে:
   - `CP-Bootable-USB-Setup-x.y.z.exe` — পূর্ণ installer (WinPE বুট ফাইলসহ)
   - `CP-Bootable-USB-portable.exe` — শুধু অ্যাপ (Payload আলাদা লাগবে)
   - `CP-Bootable-USB-Payload.zip` — WinPE বুট ফাইল

## নিজের PC-তে WinPE বানানো (ঐচ্ছিক)

Administrator PowerShell, Windows ADK + WinPE add-on ইনস্টল থাকতে হবে:

```powershell
dotnet build CPBootableUSB.sln -c Release
.\build\Build-WinPE.ps1 -LauncherDir .\src\BootLauncher\bin\Release\net48
# 32-bit (ADK for Windows 10 version 2004 লাগবে):
.\build\Build-WinPE.ps1 -Arch x86 -LauncherDir .\src\BootLauncher\bin\Release\net48
```

## বিল্ড করার নিয়ম (Phase 1)

1. **Visual Studio 2022** (17.8 বা নতুন) ইনস্টল করুন, সাথে **".NET desktop development"** workload।
2. `CPBootableUSB.sln` খুলুন → উপরে **Release** বেছে নিন → **Build ▸ Build Solution**।
   প্রথমবার NuGet প্যাকেজ (CommunityToolkit.Mvvm, System.Management, System.Text.Json) নিজে থেকেই ডাউনলোড হবে।
3. আউটপুট: `src\UsbMaker\bin\Release\net8.0-windows\win-x64\UsbMaker.exe`
   (চালালে Administrator অনুমতি চাইবে — DiskPart-এর জন্য এটা লাগবে।)

কমান্ড লাইন থেকে:

```powershell
dotnet build CPBootableUSB.sln -c Release
```

## USB বানানোর আগে

`UsbMaker.exe`-র পাশে `Payload\` ফোল্ডারে WinPE বুট ফাইল থাকতে হবে। এটা Phase 3-এর স্ক্রিপ্ট বানাবে।
Payload না থাকলে অ্যাপ খুলবে, USB শনাক্তও করবে, কিন্তু **Create Bootable USB** চাপলে "Boot payload missing" দেখাবে।
`Payload\README.txt`-এ প্রয়োজনীয় ফাইলের তালিকা আছে।

## নিরাপত্তা

- Windows system/boot ডিস্ক কখনো তালিকায় আসে না।
- ফরম্যাটের আগে ডিস্ক নম্বর টাইপ করে নিশ্চিত করতে হয়।
- ফরম্যাটের ঠিক আগে USB আবার মিলিয়ে দেখা হয় — মাঝে বদলালে কাজ বন্ধ হয়।
- প্রতিটা অপারেশনের লগ: `%LocalAppData%\CP Bootable USB\logs\` এবং USB-র `CP\logs\`।
