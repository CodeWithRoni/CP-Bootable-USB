using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BootLauncher.Services
{
    public sealed class ProcessResult
    {
        public ProcessResult(int exitCode, string output) { ExitCode = exitCode; Output = output; }
        public int ExitCode { get; }
        public string Output { get; }
    }

    /// <summary>Runs console tools (diskpart, dism, bcdboot, powershell) hidden and streams their output.</summary>
    public static class ProcessRunner
    {
        private static Encoding Oem
        {
            get
            {
                try { return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
                catch { return Encoding.Default; }
            }
        }

        public static string System32(string exe) => Path.Combine(Environment.SystemDirectory, exe);

        /// <param name="killOnCancel">false for diskpart/dism/bcdboot: never interrupt them half-way.</param>
        public static Task<ProcessResult> RunAsync(string fileName, string arguments, Action<string>? onLine = null,
            CancellationToken ct = default(CancellationToken), bool killOnCancel = true, Encoding? encoding = null)
        {
            var tcs = new TaskCompletionSource<ProcessResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var output = new StringBuilder();
            var enc = encoding ?? Oem;

            var p = new Process
            {
                StartInfo = new ProcessStartInfo(fileName, arguments)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = enc,
                    StandardErrorEncoding = enc,
                    WorkingDirectory = Environment.SystemDirectory
                },
                EnableRaisingEvents = true
            };

            DataReceivedEventHandler handler = (s, e) =>
            {
                if (e.Data == null) return;
                lock (output) output.AppendLine(e.Data);
                var line = e.Data.TrimEnd();
                if (line.Length > 0) onLine?.Invoke(line);
            };
            p.OutputDataReceived += handler;
            p.ErrorDataReceived += handler;
            p.Exited += (s, e) =>
            {
                try
                {
                    p.WaitForExit(); // drain async readers
                    string text;
                    lock (output) text = output.ToString();
                    tcs.TrySetResult(new ProcessResult(p.ExitCode, text));
                }
                catch (Exception ex) { tcs.TrySetException(ex); }
                finally { p.Dispose(); }
            };

            try
            {
                if (!p.Start()) throw new InvalidOperationException("Could not start " + fileName);
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                p.Dispose();
                tcs.TrySetException(new InvalidOperationException($"Could not start {Path.GetFileName(fileName)}: {ex.Message}", ex));
                return tcs.Task;
            }

            if (killOnCancel && ct.CanBeCanceled)
            {
                ct.Register(() =>
                {
                    try { if (!p.HasExited) p.Kill(); } catch { }
                    tcs.TrySetCanceled();
                });
            }

            return tcs.Task;
        }

        /// <summary>Runs a PowerShell command and returns its output (UTF-8).</summary>
        public static Task<ProcessResult> PowerShellAsync(string command, CancellationToken ct = default(CancellationToken))
        {
            var ps = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(
                "[Console]::OutputEncoding=[Text.Encoding]::UTF8; $ErrorActionPreference='Stop'; " + command));
            return RunAsync(ps, "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded,
                Log.Detail, ct, true, Encoding.UTF8);
        }
    }
}
