using System.IO;
using System.Text;
using UsbMaker.Models;

namespace UsbMaker.Services;

/// <summary>Thread-safe logger: writes to a session log file and raises an event for the UI.</summary>
public sealed class LogService
{
    private readonly object _lock = new();

    public LogService()
    {
        LogFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CP Bootable USB", "logs");
        Directory.CreateDirectory(LogFolder);
        LogFilePath = Path.Combine(LogFolder, $"usbmaker-{DateTime.Now:yyyyMMdd-HHmmss}.log");
    }

    public string LogFolder { get; }
    public string LogFilePath { get; }

    public event Action<LogEntry>? EntryAdded;

    public void Info(string msg) => Write(LogLevel.Info, msg);
    public void Success(string msg) => Write(LogLevel.Success, msg);
    public void Warn(string msg) => Write(LogLevel.Warning, msg);
    public void Error(string msg) => Write(LogLevel.Error, msg);
    /// <summary>Raw tool output (diskpart, bcdedit...). Goes to the file and the UI in a dimmer colour.</summary>
    public void Detail(string msg) => Write(LogLevel.Detail, msg);

    public void Write(LogLevel level, string msg)
    {
        var entry = new LogEntry(DateTime.Now, level, msg);
        lock (_lock)
        {
            try { File.AppendAllText(LogFilePath, entry + Environment.NewLine, Encoding.UTF8); }
            catch { /* logging must never break the operation */ }
        }
        EntryAdded?.Invoke(entry);
    }
}
