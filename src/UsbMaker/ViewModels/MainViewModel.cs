using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CP.Shared;
using CP.Shared.Models;
using UsbMaker.Models;
using UsbMaker.Services;

namespace UsbMaker.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly LogService _log = new();
    private readonly UsbDetectionService _detection = new();
    private readonly DeploymentService _deployment;
    private readonly SettingsStore _settingsStore = new();
    private readonly DispatcherTimer _refreshDebounce;
    private CancellationTokenSource? _cts;
    private CpConfig _baseConfig;            // keeps launcher/install sections not edited in this UI
    private bool _suppressThemeSync;

    /// <summary>Set by the window: shows the "type the disk number" confirmation dialog.</summary>
    public Func<UsbDiskInfo, bool>? ConfirmErase { get; set; }
    /// <summary>Set by the window: shows a message box (title, message, isError).</summary>
    public Action<string, string, bool>? ShowMessage { get; set; }
    /// <summary>Set by the window: opens a file dialog, returns the chosen path or null.</summary>
    public Func<string, string, string?>? PickFile { get; set; }
    /// <summary>Set by the window: opens a folder dialog, returns the chosen path or null.</summary>
    public Func<string, string?>? PickFolder { get; set; }

    public MainViewModel()
    {
        _deployment = new DeploymentService(_log, _detection);
        _log.EntryAdded += e => Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            Logs.Add(e);
            if (Logs.Count > 2000) Logs.RemoveAt(0);
        });

        _refreshDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _refreshDebounce.Tick += async (_, _) =>
        {
            _refreshDebounce.Stop();
            await RefreshAsync();
        };

        ThemePresetNames = new ObservableCollection<string>(ThemePresets.Names.Append(ThemePresets.Custom));

        var settings = _settingsStore.Load();
        _baseConfig = ConfigService.Normalize(settings.LastConfig, null);
        ApplyConfigToFields(_baseConfig);
        LogoSourcePath = settings.LastLogoPath != null && File.Exists(settings.LastLogoPath) ? settings.LastLogoPath : null;
        PayloadPath = !string.IsNullOrWhiteSpace(settings.PayloadPath) && Directory.Exists(settings.PayloadPath)
            ? settings.PayloadPath!
            : PayloadService.DefaultPayloadRoot;
        SelectedFileSystem = Enum.TryParse<DataFileSystem>(settings.DataFileSystem, out var fs) ? fs : DataFileSystem.NTFS;
        BootPartitionSizeMb = settings.BootPartitionSizeMb is int size && BootSizes.Contains(size) ? size : Constants.DefaultBootPartitionSizeMb;
        InspectPayload();   // re-check after size is restored (auto-bumps size if 32-bit WinPE present)

        _log.Info($"CP Bootable USB {DeploymentService.AppVersion} started. Log file: {_log.LogFilePath}");
    }

    // ------------------------------------------------------------------ disk selection

    public ObservableCollection<UsbDiskInfo> Disks { get; } = new();
    public ObservableCollection<LogEntry> Logs { get; } = new();
    public ObservableCollection<string> ThemePresetNames { get; }
    public DataFileSystem[] FileSystems { get; } = { DataFileSystem.NTFS, DataFileSystem.exFAT };
    public int[] BootSizes { get; } = { 512, 1024, 1536, 2048 };

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand), nameof(UpdateBrandingCommand))]
    private UsbDiskInfo? _selectedDisk;

    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _diskStatus = "Scanning for USB drives...";

    // ------------------------------------------------------------------ options

    [ObservableProperty] private DataFileSystem _selectedFileSystem = DataFileSystem.NTFS;
    [ObservableProperty] private int _bootPartitionSizeMb = Constants.DefaultBootPartitionSizeMb;
    [ObservableProperty] private bool _verifyAfterCopy = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _payloadPath = "";

    [ObservableProperty] private string _payloadStatus = "";
    [ObservableProperty] private bool _payloadOk;

    partial void OnPayloadPathChanged(string value) => InspectPayload();

    // ------------------------------------------------------------------ branding fields

    [ObservableProperty] private string _softwareName = "";
    [ObservableProperty] private string _brandName = "";
    [ObservableProperty] private string _tagline = "";
    [ObservableProperty] private string _developerCredit = "";
    [ObservableProperty] private string _phonesText = "";
    [ObservableProperty] private string _address = "";
    [ObservableProperty] private string _notice = "";
    [ObservableProperty] private string? _logoSourcePath;

    [ObservableProperty] private string _selectedThemePreset = BrandingDefaults.ThemePreset;
    [ObservableProperty] private string _backgroundColor = "";
    [ObservableProperty] private string _surfaceColor = "";
    [ObservableProperty] private string _accentColor = "";
    [ObservableProperty] private string _accentLightColor = "";

    partial void OnSelectedThemePresetChanged(string value)
    {
        if (_suppressThemeSync || value == ThemePresets.Custom || !ThemePresets.Exists(value)) return;
        var t = ThemePresets.Get(value);
        _suppressThemeSync = true;
        BackgroundColor = t.Background!;
        SurfaceColor = t.Surface!;
        AccentColor = t.Accent!;
        AccentLightColor = t.AccentLight!;
        _suppressThemeSync = false;
    }

    partial void OnBackgroundColorChanged(string value) => MarkThemeCustom();
    partial void OnSurfaceColorChanged(string value) => MarkThemeCustom();
    partial void OnAccentColorChanged(string value) => MarkThemeCustom();
    partial void OnAccentLightColorChanged(string value) => MarkThemeCustom();

    private void MarkThemeCustom()
    {
        if (_suppressThemeSync) return;
        if (ThemePresets.Exists(SelectedThemePreset))
        {
            var p = ThemePresets.Get(SelectedThemePreset);
            var same = Eq(p.Background, BackgroundColor) && Eq(p.Surface, SurfaceColor) &&
                       Eq(p.Accent, AccentColor) && Eq(p.AccentLight, AccentLightColor);
            if (same) return;
        }
        _suppressThemeSync = true;
        SelectedThemePreset = ThemePresets.Custom;
        _suppressThemeSync = false;

        static bool Eq(string? a, string? b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------ progress

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand), nameof(UpdateBrandingCommand), nameof(CancelCommand), nameof(RefreshCommand))]
    private bool _isBusy;

    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private string _statusText = "Ready.";

    // ------------------------------------------------------------------ commands

    /// <summary>Called by the window on WM_DEVICECHANGE; coalesces bursts of device events.</summary>
    public void RequestRefresh()
    {
        if (IsBusy) return;   // never change the disk list while writing to a disk
        _refreshDebounce.Stop();
        _refreshDebounce.Start();
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    public async Task RefreshAsync()
    {
        if (IsScanning) return;
        IsScanning = true;
        var keepId = SelectedDisk?.UniqueId;
        try
        {
            var disks = await Task.Run(() => _detection.GetUsbDisks());
            Disks.Clear();
            foreach (var d in disks) Disks.Add(d);
            SelectedDisk = Disks.FirstOrDefault(d => d.UniqueId == keepId && keepId != null)
                           ?? Disks.FirstOrDefault(d => !d.IsProtected);
            DiskStatus = Disks.Count == 0
                ? "No USB drive found. Plug in a USB flash drive (8 GB or larger)."
                : $"{Disks.Count} USB drive(s) found.";
        }
        catch (Exception ex)
        {
            DiskStatus = "Could not read disks: " + ex.Message;
            _log.Error("Disk detection failed: " + ex.Message);
        }
        finally
        {
            IsScanning = false;
        }
    }

    private bool CanRefresh() => !IsBusy;

    [RelayCommand]
    private void BrowseLogo()
    {
        var path = PickFile?.Invoke("Select logo image", "Images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg");
        if (path == null) return;
        if (new FileInfo(path).Length > 5 * 1024 * 1024)
        {
            ShowMessage?.Invoke("Logo too large", "Please choose an image smaller than 5 MB.", true);
            return;
        }
        LogoSourcePath = path;
    }

    [RelayCommand]
    private void ClearLogo() => LogoSourcePath = null;

    [RelayCommand]
    private void BrowsePayload()
    {
        var path = PickFolder?.Invoke("Select the Payload folder (contains Media\\ and Tools\\)");
        if (path != null) PayloadPath = path;
    }

    [RelayCommand]
    private void ResetBranding()
    {
        _baseConfig = BrandingDefaults.Create();
        ApplyConfigToFields(_baseConfig);
        LogoSourcePath = null;
        _log.Info("Branding reset to COMPUTER PLUS defaults.");
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_log.LogFolder}\"") { UseShellExecute = true }); }
        catch (Exception ex) { ShowMessage?.Invoke("Error", ex.Message, true); }
    }

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateAsync()
    {
        var disk = SelectedDisk!;
        InspectPayload();
        var payload = PayloadService.Inspect(PayloadPath);
        if (!payload.IsValid)
        {
            ShowMessage?.Invoke("Boot payload missing", payload.Summary +
                "\n\nBuild it with build\\Build-WinPE.ps1, or choose the Payload folder manually.", true);
            return;
        }
        if (!TryBuildConfig(out var config, out var error))
        {
            ShowMessage?.Invoke("Please check the branding fields", error, true);
            return;
        }
        if (ConfirmErase?.Invoke(disk) != true)
        {
            _log.Info("Cancelled by user at confirmation.");
            return;
        }

        var request = new DeploymentRequest
        {
            Disk = disk,
            Config = config,
            LogoSourcePath = LogoSourcePath,
            Payload = payload,
            DataFileSystem = SelectedFileSystem,
            BootPartitionSizeMb = BootPartitionSizeMb,
            VerifyAfterCopy = VerifyAfterCopy
        };

        await RunOperationAsync(async (progress, ct) =>
        {
            var result = await Task.Run(() => _deployment.CreateAsync(request, progress, ct), ct);
            ShowMessage?.Invoke("CP Bootable USB is ready",
                $"Your bootable USB was created successfully.\n\n" +
                $"Copy your Windows ISO files into:\n{result.DataLetter}:\\{Constants.IsoFolder}\\\n\n" +
                $"Free space: {UsbDiskInfo.FormatBytes(result.DataFreeBytes)}", false);
        }, config);
    }

    private bool CanCreate() => !IsBusy && SelectedDisk is { IsProtected: false };

    [RelayCommand(CanExecute = nameof(CanUpdateBranding))]
    private async Task UpdateBrandingAsync()
    {
        var disk = SelectedDisk!;
        if (!TryBuildConfig(out var config, out var error))
        {
            ShowMessage?.Invoke("Please check the branding fields", error, true);
            return;
        }

        await RunOperationAsync(async (progress, ct) =>
        {
            await Task.Run(() => _deployment.UpdateBrandingAsync(disk, config, LogoSourcePath, progress, ct), ct);
            ShowMessage?.Invoke("Branding updated", "config.json and logo were updated. Your ISO files were not changed.", false);
        }, config);
    }

    private bool CanUpdateBranding() => !IsBusy && SelectedDisk is { IsCpUsb: true };

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel()
    {
        if (_cts is { IsCancellationRequested: false })
        {
            _log.Warn("Cancel requested — stopping after the current step (DiskPart is never interrupted).");
            _cts.Cancel();
        }
    }

    // ------------------------------------------------------------------ helpers

    private async Task RunOperationAsync(Func<IProgress<DeploymentProgress>, CancellationToken, Task> work, CpConfig config)
    {
        IsBusy = true;
        ProgressValue = 0;
        _cts = new CancellationTokenSource();
        var progress = new Progress<DeploymentProgress>(p =>
        {
            ProgressValue = p.Percent;
            StatusText = p.Status;
        });

        try
        {
            await work(progress, _cts.Token);
            _baseConfig = config;
            SaveSettings();
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cancelled.";
            _log.Warn("Operation cancelled. If formatting had started, run 'Create Bootable USB' again before using the drive.");
        }
        catch (Exception ex)
        {
            StatusText = "Failed: " + ex.Message;
            _log.Error(ex.Message);
            ShowMessage?.Invoke("Operation failed", ex.Message + "\n\nSee the log for details.", true);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            IsBusy = false;
            await RefreshAsync();
        }
    }

    private void InspectPayload()
    {
        var info = PayloadService.Inspect(PayloadPath);
        PayloadOk = info.IsValid;
        PayloadStatus = info.Summary;
        PayloadHasX86 = info.HasX86;

        // 64-bit + 32-bit WinPE together need more room than the default 1 GB.
        if (info.HasX86 && BootPartitionSizeMb < Constants.BootPartitionSizeWithX86Mb)
        {
            BootPartitionSizeMb = Constants.BootPartitionSizeWithX86Mb;
            _log.Info($"32-bit WinPE found in payload — boot partition size set to {Constants.BootPartitionSizeWithX86Mb} MB.");
        }
    }

    [ObservableProperty] private bool _payloadHasX86;

    private void ApplyConfigToFields(CpConfig c)
    {
        SoftwareName = c.Branding!.SoftwareName!;
        BrandName = c.Branding.BrandName!;
        Tagline = c.Branding.Tagline!;
        DeveloperCredit = c.Branding.DeveloperCredit!;
        PhonesText = string.Join(Environment.NewLine, c.Contact!.Phones!);
        Address = c.Contact.Address!;
        Notice = c.Notice!;

        _suppressThemeSync = true;
        SelectedThemePreset = ThemePresets.Exists(c.Theme!.Preset) ? c.Theme.Preset! : ThemePresets.Custom;
        BackgroundColor = c.Theme.Background!;
        SurfaceColor = c.Theme.Surface!;
        AccentColor = c.Theme.Accent!;
        AccentLightColor = c.Theme.AccentLight!;
        _suppressThemeSync = false;
    }

    private bool TryBuildConfig(out CpConfig config, out string error)
    {
        config = null!;
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(SoftwareName)) problems.Add("Software / Brand Title is empty.");
        if (Notice.Length > 1500) problems.Add("Notice is too long (max 1500 characters).");

        var phones = PhonesText.Split(new[] { '\r', '\n', '|', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        if (phones.Count > 5) problems.Add("Enter at most 5 phone numbers.");

        foreach (var (name, value) in new[]
                 {
                     ("Background", BackgroundColor), ("Surface", SurfaceColor),
                     ("Accent", AccentColor), ("Accent Light", AccentLightColor)
                 })
        {
            if (!ConfigService.IsValidColor(value)) problems.Add($"{name} colour '{value}' must look like #0284C7.");
        }

        if (LogoSourcePath != null && !File.Exists(LogoSourcePath)) problems.Add("The selected logo file no longer exists.");

        if (problems.Count > 0)
        {
            error = string.Join(Environment.NewLine, problems);
            return false;
        }

        var theme = ThemePresets.Get(ThemePresets.Exists(SelectedThemePreset) ? SelectedThemePreset : BrandingDefaults.ThemePreset);
        theme.Preset = SelectedThemePreset;
        theme.Background = BackgroundColor.Trim();
        theme.Surface = SurfaceColor.Trim();
        theme.Accent = AccentColor.Trim();
        theme.AccentLight = AccentLightColor.Trim();

        config = new CpConfig
        {
            Branding = new BrandingSection
            {
                SoftwareName = SoftwareName,
                BrandName = BrandName,
                Tagline = Tagline,
                DeveloperCredit = DeveloperCredit
            },
            Contact = new ContactSection { Phones = phones, Address = Address },
            Notice = Notice,
            Theme = theme,
            Launcher = _baseConfig.Launcher,
            Install = _baseConfig.Install
        };
        config = ConfigService.Normalize(config, null);
        error = "";
        return true;
    }

    public void SaveSettings()
    {
        TryBuildConfig(out var cfg, out _);
        _settingsStore.Save(new UserSettings
        {
            LastConfig = cfg ?? _baseConfig,
            LastLogoPath = LogoSourcePath,
            PayloadPath = PayloadPath,
            DataFileSystem = SelectedFileSystem.ToString(),
            BootPartitionSizeMb = BootPartitionSizeMb
        });
    }
}
