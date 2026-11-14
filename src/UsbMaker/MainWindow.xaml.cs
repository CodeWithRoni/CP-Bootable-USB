using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using UsbMaker.ViewModels;
using UsbMaker.Views;

namespace UsbMaker;

public partial class MainWindow : Window
{
    private const int WM_DEVICECHANGE = 0x0219;
    private const int DBT_DEVICEARRIVAL = 0x8000;
    private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
    private const int DBT_DEVNODES_CHANGED = 0x0007;

    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel
        {
            ConfirmErase = disk => new ConfirmEraseWindow(disk) { Owner = this }.ShowDialog() == true,
            ShowMessage = (title, message, isError) =>
                MessageBox.Show(this, message, title, MessageBoxButton.OK,
                    isError ? MessageBoxImage.Warning : MessageBoxImage.Information),
            PickFile = (title, filter) =>
            {
                var dlg = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
                return dlg.ShowDialog(this) == true ? dlg.FileName : null;
            },
            PickFolder = title =>
            {
                var dlg = new OpenFolderDialog { Title = title };
                return dlg.ShowDialog(this) == true ? dlg.FolderName : null;
            }
        };
        DataContext = _vm;

        // Auto-scroll the log to the newest line.
        ((INotifyCollectionChanged)LogList.Items).CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add && LogList.Items.Count > 0)
                LogList.ScrollIntoView(LogList.Items[^1]);
        };

        Loaded += async (_, _) => await _vm.RefreshAsync();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        EnableDarkTitleBar(hwnd);
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
    }

    /// <summary>USB plug/unplug → debounced refresh of the drive list.</summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_DEVICECHANGE)
        {
            var evt = wParam.ToInt32();
            if (evt is DBT_DEVICEARRIVAL or DBT_DEVICEREMOVECOMPLETE or DBT_DEVNODES_CHANGED)
                _vm.RequestRefresh();
        }
        return IntPtr.Zero;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_vm.IsBusy)
        {
            MessageBox.Show(this, "Please wait until the current operation finishes. Closing now could leave the USB drive unusable.",
                "CP Bootable USB", MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Cancel = true;
            return;
        }
        _vm.SaveSettings();
        base.OnClosing(e);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private static void EnableDarkTitleBar(IntPtr hwnd)
    {
        try
        {
            var on = 1;
            // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE (Windows 10 20H1+); 19 on older builds.
            if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
        }
        catch { /* cosmetic only */ }
    }
}
