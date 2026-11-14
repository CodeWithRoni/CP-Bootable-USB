namespace UsbMaker.Models;

public enum LogLevel { Info, Success, Warning, Error, Detail }

public sealed record LogEntry(DateTime Time, LogLevel Level, string Message)
{
    public string TimeText => Time.ToString("HH:mm:ss");
    public override string ToString() => $"[{TimeText}] {Level.ToString().ToUpperInvariant(),-7} {Message}";
}

public sealed record DeploymentProgress(double Percent, string Status);
