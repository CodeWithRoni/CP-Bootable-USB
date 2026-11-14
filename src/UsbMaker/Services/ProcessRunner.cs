using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace UsbMaker.Services;

public sealed record ProcessResult(int ExitCode, string Output);

/// <summary>Runs console tools (diskpart, bootsect, bcdedit) hidden, streaming their output line by line.</summary>
public static class ProcessRunner
{
    /// <summary>Console tools write in the OEM code page (e.g. 437), not UTF-8.</summary>
    private static Encoding OemEncoding
    {
        get
        {
            try { return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
            catch { return Encoding.Default; }
        }
    }

    /// <param name="killOnCancel">
    /// false for tools that must never be interrupted half-way (diskpart): cancellation is ignored
    /// until the process exits on its own.
    /// </param>
    public static async Task<ProcessResult> RunAsync(string fileName, string arguments, Action<string>? onLine,
        CancellationToken ct, bool killOnCancel = true)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = OemEncoding,
            StandardErrorEncoding = OemEncoding,
            WorkingDirectory = Environment.SystemDirectory
        };

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var output = new StringBuilder();

        void Handle(string? line)
        {
            if (line == null) return;
            lock (output) output.AppendLine(line);
            if (!string.IsNullOrWhiteSpace(line)) onLine?.Invoke(line.TrimEnd());
        }

        process.OutputDataReceived += (_, e) => Handle(e.Data);
        process.ErrorDataReceived += (_, e) => Handle(e.Data);

        if (!process.Start()) throw new InvalidOperationException($"Could not start {fileName}.");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(killOnCancel ? ct : CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            throw;
        }

        // Make sure the async readers have drained.
        process.WaitForExit();
        lock (output) return new ProcessResult(process.ExitCode, output.ToString());
    }
}
