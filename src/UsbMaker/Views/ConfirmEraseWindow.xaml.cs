using System.Windows;
using System.Windows.Controls;
using UsbMaker.Models;

namespace UsbMaker.Views;

/// <summary>Forces the technician to type the disk number before anything is erased.</summary>
public partial class ConfirmEraseWindow : Window
{
    private readonly string _expected;

    public ConfirmEraseWindow(UsbDiskInfo disk)
    {
        InitializeComponent();
        _expected = disk.Number.ToString();
        DiskText.Text = disk.DetailsText;
        LargeWarning.Visibility = disk.IsLarge ? Visibility.Visible : Visibility.Collapsed;
        PromptText.Text = $"Type the disk number  {_expected}  to confirm:";
        Loaded += (_, _) => ConfirmBox.Focus();
    }

    private void ConfirmBox_TextChanged(object sender, TextChangedEventArgs e) =>
        EraseButton.IsEnabled = ConfirmBox.Text.Trim() == _expected;

    private void Erase_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
