using System.IO;
using System.Text.RegularExpressions;
using CP.Shared;

namespace UsbMaker.Services;

/// <summary>
/// Makes the BOOT partition bootable:
///  • Legacy BIOS: bootsect /nt60 writes BOOTMGR-compatible code into the MBR and the partition boot sector.
///  • UEFI: nothing to "install" — firmware loads \EFI\Boot\bootx64.efi (Microsoft-signed, Secure Boot OK).
///  • Both BCD stores (ADK templates, already pointing to [boot]\sources\boot.wim) get our description/timeout.
///  • If the payload contains a 32-bit WinPE (sources\boot_x86.wim), a second entry is added to the
///    BIOS store so old 32-bit-only PCs can boot it. (64-bit UEFI firmware cannot run 32-bit WinPE,
///    so the UEFI store keeps only the 64-bit entry.)
/// </summary>
public sealed class BootSetupService
{
    private static readonly Regex GuidRegex = new(@"\{[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\}");

    private readonly LogService _log;

    public BootSetupService(LogService log) => _log = log;

    private static string Bcdedit => Path.Combine(Environment.SystemDirectory, "bcdedit.exe");

    public async Task WriteBootCodeAsync(string bootsectPath, char bootLetter, CancellationToken ct)
    {
        if (!File.Exists(bootsectPath))
            throw new FileNotFoundException("bootsect.exe not found. Put it in Payload\\Tools or install the Windows ADK.", bootsectPath);

        _log.Info($"Writing BIOS boot code (bootsect /nt60 {bootLetter}: /force /mbr)...");
        var result = await ProcessRunner.RunAsync(bootsectPath, $"/nt60 {bootLetter}: /force /mbr", _log.Detail, ct);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"bootsect failed with exit code {result.ExitCode}. See the log for details.");
        _log.Success("BIOS boot code written to MBR and boot sector.");
    }

    /// <param name="addX86Entry">
    /// true only on a freshly created USB whose payload includes boot_x86.wim. Never true on
    /// "Update Branding Only", so the entry is not duplicated.
    /// </param>
    public async Task CustomizeBcdAsync(char bootLetter, string description, bool addX86Entry, CancellationToken ct)
    {
        var baseDescription = Sanitize(description);

        foreach (var relative in new[] { Constants.BiosBcdRelativePath, Constants.UefiBcdRelativePath })
        {
            var store = Path.Combine($"{bootLetter}:\\", relative);
            if (!File.Exists(store))
            {
                _log.Warn($"BCD store not found: {store}");
                continue;
            }

            // Payload files may carry read-only/hidden/system attributes; bcdedit needs write access.
            try { File.SetAttributes(store, FileAttributes.Normal); } catch { /* ignore */ }

            var isBios = !relative.StartsWith("EFI", StringComparison.OrdinalIgnoreCase);
            var kind = isBios ? "BIOS" : "UEFI";
            var withX86 = isBios && addX86Entry;
            _log.Info($"Customising {kind} BCD store ({relative})...");

            var ok = true;
            var desc64 = withX86 ? $"{baseDescription} (64-bit)" : baseDescription;
            ok &= await RunBcd(store, $"/set {{default}} description \"{desc64}\"", ct);
            // Text-mode menu: instant, and works on every BIOS (the graphical menu needs a reboot to switch).
            ok &= await RunBcd(store, "/set {default} bootmenupolicy legacy", ct);

            if (withX86) ok &= await AddX86EntryAsync(store, $"{baseDescription} (32-bit - old PCs)", ct);

            // One entry → boot instantly. Two entries (BIOS + 32-bit) → show menu for 10 s, 64-bit is default.
            ok &= await RunBcd(store, withX86 ? "/timeout 10" : "/timeout 0", ct);

            if (ok) _log.Success($"{kind} BCD store updated{(withX86 ? " (64-bit + 32-bit menu)" : "")}.");
            else _log.Warn($"{kind} BCD store could not be fully customised; the default 64-bit entry will still boot.");
        }
    }

    /// <summary>Copies {default} and points the copy at sources\boot_x86.wim (same ramdisk options / boot.sdi).</summary>
    private async Task<bool> AddX86EntryAsync(string store, string description, CancellationToken ct)
    {
        _log.Info("Adding 32-bit WinPE entry to the BIOS boot menu...");
        var copy = await ProcessRunner.RunAsync(Bcdedit, $"/store \"{store}\" /copy {{default}} /d \"{description}\"", _log.Detail, ct);
        var match = GuidRegex.Match(copy.Output);
        if (copy.ExitCode != 0 || !match.Success)
        {
            _log.Warn("Could not create the 32-bit boot entry (bcdedit /copy failed).");
            return false;
        }

        var id = match.Value;
        var ramdisk = $"ramdisk=[boot]\\{Constants.BootWimX86RelativePath},{{ramdiskoptions}}";
        var ok = await RunBcd(store, $"/set {id} device {ramdisk}", ct);
        ok &= await RunBcd(store, $"/set {id} osdevice {ramdisk}", ct);
        ok &= await RunBcd(store, $"/displayorder {id} /addlast", ct);

        if (ok) _log.Success($"32-bit WinPE entry added ({id}).");
        return ok;
    }

    private async Task<bool> RunBcd(string store, string args, CancellationToken ct)
    {
        var r = await ProcessRunner.RunAsync(Bcdedit, $"/store \"{store}\" {args}", _log.Detail, ct);
        if (r.ExitCode != 0) _log.Warn($"bcdedit {args} → exit {r.ExitCode}");
        return r.ExitCode == 0;
    }

    private static string Sanitize(string description)
    {
        var s = description.Replace("\"", "'").Trim();
        return s.Length == 0 ? "CP Bootable USB" : s;
    }
}
