using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace UsbMaker.Services;

public sealed class PayloadInfo
{
    public string Root { get; init; } = "";
    public string MediaPath => Path.Combine(Root, "Media");
    public string? BootsectPath { get; init; }
    public long TotalBytes { get; init; }
    public int FileCount { get; init; }
    public string? Version { get; init; }
    public List<string> Missing { get; } = new();
    public bool IsValid => Missing.Count == 0 && BootsectPath != null;
    /// <summary>Payload also contains a 32-bit WinPE (Media\sources\boot_x86.wim) for old 32-bit-only PCs.</summary>
    public bool HasX86 { get; init; }

    public string Summary
    {
        get
        {
            if (!Directory.Exists(Root)) return "Payload folder not found — run build\\Build-WinPE.ps1 (Phase 3) first.";
            if (Missing.Count > 0) return "Payload incomplete. Missing: " + string.Join(", ", Missing);
            if (BootsectPath == null) return "bootsect.exe not found (Payload\\Tools or Windows ADK).";
            var v = Version != null ? $"v{Version}, " : "";
            var arch = HasX86 ? "64-bit + 32-bit WinPE" : "64-bit WinPE only";
            return $"Payload OK — {arch} ({v}{FileCount} files, {Models.UsbDiskInfo.FormatBytes((ulong)TotalBytes)})";
        }
    }
}

/// <summary>Validates and copies the WinPE boot payload produced by Phase 3.</summary>
public sealed class PayloadService
{
    public static readonly string[] RequiredFiles =
    {
        "bootmgr",
        "bootmgr.efi",
        @"Boot\BCD",
        @"Boot\boot.sdi",
        @"EFI\Boot\bootx64.efi",
        @"EFI\Microsoft\Boot\BCD",
        @"sources\boot.wim"
    };

    private const string AdkBootsect =
        @"Windows Kits\10\Assessment and Deployment Kit\Deployment Tools\amd64\BCDBoot\bootsect.exe";

    private readonly LogService _log;

    public PayloadService(LogService log) => _log = log;

    public static string DefaultPayloadRoot => Path.Combine(AppContext.BaseDirectory, "Payload");

    public static PayloadInfo Inspect(string root)
    {
        long total = 0;
        var count = 0;
        string? version = null;
        var media = Path.Combine(root, "Media");

        if (Directory.Exists(media))
        {
            foreach (var f in Directory.EnumerateFiles(media, "*", SearchOption.AllDirectories))
            {
                total += new FileInfo(f).Length;
                count++;
            }
        }

        try
        {
            var meta = Path.Combine(root, "payload.json");
            if (File.Exists(meta))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(meta));
                if (doc.RootElement.TryGetProperty("version", out var v)) version = v.GetString();
            }
        }
        catch { /* optional file */ }

        var info = new PayloadInfo
        {
            Root = root,
            BootsectPath = FindBootsect(root),
            TotalBytes = total,
            FileCount = count,
            Version = version,
            HasX86 = File.Exists(Path.Combine(media, CP.Shared.Constants.BootWimX86RelativePath))
        };

        foreach (var rel in RequiredFiles)
            if (!File.Exists(Path.Combine(media, rel))) info.Missing.Add(rel);

        return info;
    }

    private static string? FindBootsect(string root)
    {
        var local = Path.Combine(root, "Tools", "bootsect.exe");
        if (File.Exists(local)) return local;

        foreach (var pf in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
                 })
        {
            if (string.IsNullOrEmpty(pf)) continue;
            var adk = Path.Combine(pf, AdkBootsect);
            if (File.Exists(adk)) return adk;
        }
        return null;
    }

    /// <summary>Copies Payload\Media\* to the BOOT partition, reporting byte-level progress (0..1).</summary>
    public async Task CopyMediaAsync(PayloadInfo payload, string destinationRoot, IProgress<double> progress, CancellationToken ct)
    {
        const int bufferSize = 4 * 1024 * 1024;
        var buffer = new byte[bufferSize];
        long copied = 0;
        var total = Math.Max(1, payload.TotalBytes);

        foreach (var src in Directory.EnumerateFiles(payload.MediaPath, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var rel = Path.GetRelativePath(payload.MediaPath, src);
            var dst = Path.Combine(destinationRoot, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);

            var size = new FileInfo(src).Length;
            if (size > 50 * 1024 * 1024) _log.Info($"Copying {rel} ({Models.UsbDiskInfo.FormatBytes((ulong)size)})...");

            await using (var input = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize,
                             FileOptions.Asynchronous))
            {
                int read;
                while ((read = await input.ReadAsync(buffer.AsMemory(0, bufferSize), ct)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                    copied += read;
                    progress.Report((double)copied / total);
                }
                // Push the data to the stick itself, not just the Windows cache.
                output.Flush(flushToDisk: true);
            }

            File.SetAttributes(dst, FileAttributes.Normal);
        }

        _log.Success($"Boot files copied ({Models.UsbDiskInfo.FormatBytes((ulong)copied)}).");
    }

    /// <summary>Checks every copied file's size, and the SHA-256 of boot.wim.</summary>
    public async Task VerifyAsync(PayloadInfo payload, string destinationRoot, CancellationToken ct)
    {
        _log.Info("Verifying copied boot files...");
        foreach (var src in Directory.EnumerateFiles(payload.MediaPath, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(payload.MediaPath, src);
            var dst = Path.Combine(destinationRoot, rel);
            if (!File.Exists(dst)) throw new IOException($"Verification failed: {rel} is missing on the USB.");
            if (new FileInfo(dst).Length != new FileInfo(src).Length)
                throw new IOException($"Verification failed: {rel} has the wrong size on the USB.");
        }

        var wims = new List<string> { CP.Shared.Constants.BootWimRelativePath };
        if (payload.HasX86) wims.Add(CP.Shared.Constants.BootWimX86RelativePath);

        foreach (var wim in wims)
        {
            var h1 = await HashAsync(Path.Combine(payload.MediaPath, wim), ct);
            var h2 = await HashAsync(Path.Combine(destinationRoot, wim), ct);
            if (!h1.SequenceEqual(h2))
                throw new IOException($"Verification failed: {wim} checksum mismatch. The USB may be faulty.");
        }

        _log.Success($"Verification passed (sizes + SHA-256 of {string.Join(", ", wims)}).");
    }

    private static async Task<byte[]> HashAsync(string path, CancellationToken ct)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4 * 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var sha = SHA256.Create();
        return await sha.ComputeHashAsync(fs, ct);
    }
}
