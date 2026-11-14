using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using BootLauncher.Models;
using BootLauncher.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CP.Shared.Models;

namespace BootLauncher.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly IsoMountService _mount = new IsoMountService();
        private CancellationTokenSource? _analysisCts;
        private DispatcherTimer? _rebootTimer;
        private bool _setupLaunched;

        /// <summary>Set by the window: pick target disk for Direct Apply (returns null = cancelled).</summary>
        public Func<ImageEdition, TargetDisk[], TargetDisk?>? PickTargetDisk { get; set; }
        /// <summary>Set by the window: yes/no question.</summary>
        public Func<string, string, bool>? Confirm { get; set; }

        public MainViewModel()
        {
            var cfg = BrandingService.Config;
            SoftwareName = cfg.Branding?.SoftwareName ?? BrandingDefaults.SoftwareName;
            BrandName = cfg.Branding?.BrandName ?? BrandingDefaults.BrandName;
            Tagline = cfg.Branding?.Tagline ?? BrandingDefaults.Tagline;
            DeveloperCredit = cfg.Branding?.DeveloperCredit ?? BrandingDefaults.DeveloperCredit;
            Phones = new ObservableCollection<string>(cfg.Contact?.Phones ?? BrandingDefaults.Phones.ToList());
            Address = cfg.Contact?.Address ?? BrandingDefaults.Address;
            Notice = cfg.Notice ?? BrandingDefaults.Notice;
            Logo = BrandingService.LoadLogo();
            ShowCommandPrompt = cfg.Launcher?.ShowCommandPrompt ?? true;

            var b = cfg.Launcher?.DefaultBypass ?? new BypassSection();
            BypassTpm = b.Tpm; BypassSecureBoot = b.SecureBoot; BypassRam = b.Ram; BypassCpu = b.Cpu; BypassStorage = b.Storage;
        }

        // ------------------------------------------------------------- branding
        public string SoftwareName { get; }
        public string BrandName { get; }
        public string Tagline { get; }
        public string DeveloperCredit { get; }
        public ObservableCollection<string> Phones { get; }
        public string Address { get; }
        public string Notice { get; }
        public ImageSource Logo { get; }
        public bool ShowCommandPrompt { get; }

        // ------------------------------------------------------------- hardware
        [ObservableProperty] private HardwareInfo _hardware = new HardwareInfo();
        [ObservableProperty] private bool _hardwareLoaded;
        public ObservableCollection<TargetDisk> Disks { get; } = new ObservableCollection<TargetDisk>();

        // ------------------------------------------------------------- ISOs
        public ObservableCollection<IsoItem> Isos { get; } = new ObservableCollection<IsoItem>();
        [ObservableProperty] private IsoItem? _selectedIso;
        [ObservableProperty] private string _isoStatus = "Scanning drives for ISO files...";
        [ObservableProperty] private bool _isScanning;

        partial void OnSelectedIsoChanged(IsoItem? value) => _ = AnalyzeAsync(value);

        // ------------------------------------------------------------- editions
        public ObservableCollection<ImageEdition> Editions { get; } = new ObservableCollection<ImageEdition>();

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
        private ImageEdition? _selectedEdition;

        [ObservableProperty] private bool _isAnalyzing;
        [ObservableProperty] private string _analysisText = "Select an ISO on the left to see its Windows editions.";
        [ObservableProperty] private string? _compatibilityText;
        [ObservableProperty] private bool _compatibilityIsError;
        [ObservableProperty] private bool _showBypass;

        [ObservableProperty] private bool _bypassTpm;
        [ObservableProperty] private bool _bypassSecureBoot;
        [ObservableProperty] private bool _bypassRam;
        [ObservableProperty] private bool _bypassCpu;
        [ObservableProperty] private bool _bypassStorage;

        partial void OnSelectedEditionChanged(ImageEdition? value)
        {
            if (value == null) { CompatibilityText = null; ShowBypass = false; return; }
            CompatibilityIsError = value.IsBlocked;
            CompatibilityText = value.BlockedReason ?? value.Warning;
            ShowBypass = value.Family == WindowsFamily.Win11 && value.Engine == InstallEngine.Setup;
        }

        // ------------------------------------------------------------- overlay
        [ObservableProperty] private bool _overlayVisible;
        [ObservableProperty] private string _overlayTitle = "";
        [ObservableProperty] private string _overlayMessage = "";
        [ObservableProperty] private double _overlayProgress;
        [ObservableProperty] private bool _overlayIndeterminate;
        [ObservableProperty] private bool _overlayCanClose;
        [ObservableProperty] private bool _overlayIsError;
        [ObservableProperty] private bool _overlayShowReboot;
        [ObservableProperty] private string _rebootButtonText = "Restart now";
        public ObservableCollection<string> OverlayLog { get; } = new ObservableCollection<string>();

        // ------------------------------------------------------------- startup
        public async Task InitializeAsync()
        {
            Log.LineWritten += line => Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!OverlayVisible) return;
                OverlayLog.Add(line);
                while (OverlayLog.Count > 8) OverlayLog.RemoveAt(0);
            }));

            var hwTask = Task.Run(() => SystemInfo.ReadHardware());
            await RefreshAsync();
            try
            {
                Hardware = await hwTask;
                Disks.Clear();
                foreach (var d in Hardware.Disks) Disks.Add(d);
            }
            catch (Exception ex) { Log.Error("Hardware info: " + ex.Message); }
            HardwareLoaded = true;
            Log.Info($"Hardware: {Hardware.CpuName}, RAM {Hardware.RamText}, {Hardware.BootModeText}, {Hardware.EnvironmentText}");
        }

        // ------------------------------------------------------------- commands
        [RelayCommand]
        private async Task RefreshAsync()
        {
            if (IsScanning) return;
            IsScanning = true;
            IsoStatus = "Scanning drives for ISO files...";
            try
            {
                if (BrandingService.DataRoot == null) BrandingService.Load();
                var items = await Task.Run(() => IsoScanner.Scan());
                var keep = SelectedIso?.Path;
                Isos.Clear();
                foreach (var i in items) Isos.Add(i);
                IsoStatus = Isos.Count == 0
                    ? "No ISO files found. Copy Windows ISO files into the ISO folder of the USB drive, then press Refresh."
                    : $"{Isos.Count} ISO file(s) found";
                if (keep != null) SelectedIso = Isos.FirstOrDefault(i => string.Equals(i.Path, keep, StringComparison.OrdinalIgnoreCase));
                else if (Isos.Count == 1) SelectedIso = Isos[0];
            }
            catch (Exception ex)
            {
                IsoStatus = "Scan failed: " + ex.Message;
                Log.Error(IsoStatus);
            }
            finally { IsScanning = false; }
        }

        private async Task AnalyzeAsync(IsoItem? iso)
        {
            _analysisCts?.Cancel();
            Editions.Clear();
            SelectedEdition = null;
            if (iso == null) { AnalysisText = "Select an ISO on the left to see its Windows editions."; return; }
            if (_setupLaunched)
            {
                AnalysisText = "Windows Setup is running from another ISO. Restart the PC to choose a different ISO.";
                return;
            }

            var cts = _analysisCts = new CancellationTokenSource();
            IsAnalyzing = true;
            AnalysisText = $"Reading {iso.FileName}...";
            try
            {
                var root = await _mount.MountAsync(iso.Path, cts.Token);
                var editions = await ImageInfoService.ReadEditionsAsync(root, cts.Token);
                if (cts.IsCancellationRequested) return;

                var force = BrandingService.Config.Install?.ForceDirectApply ?? false;
                foreach (var e in editions)
                {
                    Compatibility.Evaluate(e, Hardware, force);
                    Editions.Add(e);
                }
                SelectedEdition = Editions.FirstOrDefault(e => !e.IsBlocked && e.Name.IndexOf("Pro", StringComparison.OrdinalIgnoreCase) >= 0)
                                  ?? Editions.FirstOrDefault(e => !e.IsBlocked) ?? Editions.FirstOrDefault();
                var first = editions[0];
                AnalysisText = $"{first.FamilyName} · {string.Join(" + ", editions.Select(x => x.Arch).Distinct())} · {editions.Count} edition(s)";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                AnalysisText = ex.Message;
                Log.Error("Analyze: " + ex.Message);
            }
            finally
            {
                if (_analysisCts == cts) IsAnalyzing = false;
            }
        }

        private bool CanInstall() => SelectedEdition != null && !SelectedEdition.IsBlocked && !IsAnalyzing;

        partial void OnIsAnalyzingChanged(bool value) => InstallCommand.NotifyCanExecuteChanged();

        [RelayCommand(CanExecute = nameof(CanInstall))]
        private async Task InstallAsync()
        {
            var e = SelectedEdition;
            if (e == null || e.IsBlocked) return;

            if (e.Engine == InstallEngine.Setup) LaunchSetup(e);
            else await RunDirectApplyAsync(e);
        }

        private void LaunchSetup(ImageEdition e)
        {
            if ((BrandingService.Config.Launcher?.ConfirmBeforeInstall ?? true) && Confirm != null &&
                !Confirm("Start Windows Setup?",
                    $"{e.FamilyName} ({e.Arch}) — Windows Setup will start.\n\nYou will choose the disk and edition inside Setup.\nMake sure important data is backed up."))
                return;

            try
            {
                if (ShowBypass)
                    SetupEngine.ApplyBypass(new BypassSection { Tpm = BypassTpm, SecureBoot = BypassSecureBoot, Ram = BypassRam, Cpu = BypassCpu, Storage = BypassStorage });
                SetupEngine.Launch(e);
                _setupLaunched = true;
                ShowOverlay("Windows Setup is running",
                    "Follow the Windows Setup window. The PC restarts automatically when Setup has copied its files — you can then remove the USB drive.",
                    canClose: true);
                OverlayIndeterminate = true;
            }
            catch (Exception ex)
            {
                Log.Error("Setup: " + ex.Message);
                ShowOverlay("Could not start Windows Setup", ex.Message, canClose: true, isError: true);
            }
        }

        private async Task RunDirectApplyAsync(ImageEdition e)
        {
            if (!SystemInfo.IsWinPE)
            {
                ShowOverlay("Not available in test mode",
                    "Direct Apply erases a disk, so it only runs when the PC is booted from the CP Bootable USB (WinPE).",
                    canClose: true, isError: true);
                return;
            }

            // Disks that must never be erased: the USB stick itself and the disk that holds the ISO.
            var blocked = new[]
            {
                (SystemInfo.DiskIndexOfDrive(BrandingService.DataRoot), "This is the CP Bootable USB drive"),
                (SystemInfo.DiskIndexOfDrive(SelectedIso?.Path), "This disk contains the selected ISO file")
            };
            var disks = (await Task.Run(() => SystemInfo.GetDisks())).ToArray();
            foreach (var d in disks)
            {
                var hit = blocked.FirstOrDefault(b => b.Item1 == d.Index);
                if (hit.Item1 != null) d.BlockedReason = hit.Item2;
                else if (d.SizeBytes < 16UL * 1024 * 1024 * 1024) d.BlockedReason = "Too small for Windows (under 16 GB)";
            }

            var target = PickTargetDisk?.Invoke(e, disks);
            if (target == null) return;

            ShowOverlay($"Installing {e.Name}", "Preparing...", canClose: false);
            var progress = new Progress<InstallProgress>(p =>
            {
                OverlayProgress = p.Percent;
                OverlayMessage = p.Step;
            });

            try
            {
                await Task.Run(() => new DirectApplyEngine(progress, CancellationToken.None).RunAsync(target, e, Hardware.IsUefi));
                await _mount.DismountAsync();
                OverlayTitle = "Windows installed successfully";
                OverlayMessage = "Remove the USB drive. The PC will restart into Windows setup (OOBE).";
                OverlayProgress = 100;
                OverlayShowReboot = true;
                OverlayCanClose = true;
                if (BrandingService.Config.Install?.AutoReboot ?? true) StartRebootCountdown(15);
            }
            catch (Exception ex)
            {
                Log.Error("Direct Apply: " + ex.Message);
                OverlayTitle = "Installation failed";
                OverlayMessage = ex.Message + "\n\nLog: " + Log.FilePath;
                OverlayIsError = true;
                OverlayCanClose = true;
            }
        }

        private void StartRebootCountdown(int seconds)
        {
            var left = seconds;
            RebootButtonText = $"Restart now ({left})";
            _rebootTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _rebootTimer.Tick += (s, a) =>
            {
                left--;
                RebootButtonText = $"Restart now ({left})";
                if (left <= 0) { _rebootTimer?.Stop(); PowerService.Reboot(); }
            };
            _rebootTimer.Start();
        }

        [RelayCommand]
        private void CloseOverlay()
        {
            _rebootTimer?.Stop();
            _rebootTimer = null;
            RebootButtonText = "Restart now";
            OverlayVisible = false;
        }

        [RelayCommand]
        private void Reboot()
        {
            if (Confirm == null || Confirm("Restart", "Restart this PC now?")) PowerService.Reboot();
        }

        [RelayCommand]
        private void RebootNow() => PowerService.Reboot();

        [RelayCommand]
        private void Shutdown()
        {
            if (Confirm == null || Confirm("Shut down", "Shut down this PC now?")) PowerService.Shutdown();
        }

        [RelayCommand]
        private void OpenCmd()
        {
            try { PowerService.OpenCommandPrompt(); }
            catch (Exception ex) { Log.Error("cmd: " + ex.Message); }
        }

        private void ShowOverlay(string title, string message, bool canClose, bool isError = false)
        {
            OverlayLog.Clear();
            OverlayTitle = title;
            OverlayMessage = message;
            OverlayProgress = 0;
            OverlayIndeterminate = false;
            OverlayCanClose = canClose;
            OverlayIsError = isError;
            OverlayShowReboot = false;
            OverlayVisible = true;
        }
    }
}
