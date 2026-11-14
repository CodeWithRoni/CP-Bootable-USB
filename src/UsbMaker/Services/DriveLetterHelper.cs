using System.IO;
using Microsoft.Win32;

namespace UsbMaker.Services;

public static class DriveLetterHelper
{
    /// <summary>
    /// Returns free drive letters, searching from Z downwards. Letters used by mounted volumes and by
    /// remembered (possibly disconnected) network drives are skipped.
    /// </summary>
    public static IReadOnlyList<char> GetFreeLetters(int count)
    {
        var used = new HashSet<char>(Environment.GetLogicalDrives().Select(d => char.ToUpperInvariant(d[0])));

        try
        {
            using var network = Registry.CurrentUser.OpenSubKey("Network");
            if (network != null)
                foreach (var name in network.GetSubKeyNames())
                    if (name.Length == 1) used.Add(char.ToUpperInvariant(name[0]));
        }
        catch { /* best effort */ }

        var free = new List<char>();
        for (var c = 'Z'; c >= 'D' && free.Count < count; c--)
            if (!used.Contains(c)) free.Add(c);

        if (free.Count < count) throw new InvalidOperationException("Not enough free drive letters (need 2).");
        return free;
    }

    /// <summary>Waits until a newly assigned volume is mounted and readable.</summary>
    public static async Task WaitForVolumeAsync(char letter, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var di = new DriveInfo(letter.ToString());
                if (di.IsReady && Directory.Exists(di.RootDirectory.FullName)) return;
            }
            catch { /* not yet */ }
            await Task.Delay(500, ct);
        }
        throw new TimeoutException($"Volume {letter}: did not become ready within {timeout.TotalSeconds:0} seconds.");
    }
}
