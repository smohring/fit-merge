using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FitMerge.Models;
using FitMerge.Services;
using Microsoft.Win32;

namespace FitMerge;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<FitFile> _files = [];

    public MainWindow()
    {
        InitializeComponent();
        FileList.ItemsSource = _files;
        DeviceCombo.ItemsSource = GarminProducts.AllNames;
        _files.CollectionChanged += (_, _) => RefreshAnalysis();
        ApplyStartupArgs();
        RefreshAnalysis();
    }

    /// <summary>Transfers --device, --serial and file paths from the command line into the UI.</summary>
    private async void ApplyStartupArgs()
    {
        if (App.DeviceOverride is ushort product)
        {
            DeviceOverrideCheck.IsChecked = true;
            DeviceCombo.Text = GarminProducts.NameOf(product) ?? product.ToString();
        }
        if (App.SerialOverride is uint serial)
        {
            DeviceOverrideCheck.IsChecked = true;
            SerialBox.Text = serial.ToString();
        }
        if (App.StartupFiles.Count > 0)
            await AddFilesAsync(App.StartupFiles);
    }

    private async void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = Loc.Get("OpenFilter"),
            Multiselect = true,
            Title = Loc.Get("OpenTitle"),
        };
        if (dialog.ShowDialog(this) == true)
            await AddFilesAsync(dialog.FileNames);
    }

    private async Task AddFilesAsync(IEnumerable<string> paths)
    {
        var errors = new List<string>();
        foreach (var path in paths)
        {
            if (_files.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)))
                continue;
            try
            {
                _files.Add(await Task.Run(() => FitReader.Read(path)));
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }
        if (errors.Count > 0)
            MessageBox.Show(this, string.Join("\n", errors), Loc.Get("ReadErrorTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        if (FileList.SelectedItem == null && _files.Count > 0)
            FileList.SelectedIndex = _files.Count - 1;
    }

    private void RemoveFile_Click(object sender, RoutedEventArgs e)
    {
        if (FileList.SelectedItem is FitFile selected)
            _files.Remove(selected);
    }

    private void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RemoveButton.IsEnabled = FileList.SelectedItem != null;
        var f = FileList.SelectedItem as FitFile;
        PreviewEmptyText.Visibility = f == null ? Visibility.Visible : Visibility.Collapsed;
        PreviewGrid.Visibility = f == null ? Visibility.Collapsed : Visibility.Visible;
        if (f == null) return;

        ValSport.Text = f.SubSport != Dynastream.Fit.SubSport.Generic ? $"{f.Sport} ({f.SubSport})" : f.Sport.ToString();
        ValStart.Text = f.Start.ToLocalTime().ToString("G");
        ValEnd.Text = f.End.ToLocalTime().ToString("G");
        ValDuration.Text = f.Elapsed.ToString(@"h\:mm\:ss");
        ValTimer.Text = TimeSpan.FromSeconds(f.TimerSeconds).ToString(@"h\:mm\:ss");
        ValDistance.Text = f.DistanceMeters > 0 ? $"{f.DistanceMeters / 1000:0.00} km" : "–";
        ValRecords.Text = f.Records.Count.ToString("N0");
        ValAvgHr.Text = f.AvgHeartRate is int avg ? $"{avg} bpm" : "–";
        ValMaxHr.Text = f.MaxHeartRate is int max ? $"{max} bpm" : "–";
        ValCalories.Text = f.Calories?.ToString("N0") ?? "–";
        ValLaps.Text = f.Laps.Count.ToString();
        ValPauses.Text = f.PauseCount.ToString();
    }

    private void RefreshAnalysis()
    {
        var options = TryBuildOptions(out var deviceError);
        DeviceHint.Text = deviceError ?? (_files.Count > 0
            ? Loc.Get("DeviceTarget", FitMerger.ResolveDevice(_files.ToList(), options).Display)
            : "");
        if (deviceError is null) DeviceHint.ClearValue(TextBlock.ForegroundProperty); // keep the theme colour
        else DeviceHint.Foreground = Brushes.IndianRed;

        var issues = _files.Count >= 2 ? FitMerger.Analyze(_files.ToList(), options) : [];
        IssuesList.ItemsSource = issues;
        IssuesEmptyText.Visibility = issues.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        MergeButton.IsEnabled = _files.Count >= 2 && deviceError is null;
        StatusText.Text = _files.Count switch
        {
            0 => Loc.Get("StatusNone"),
            1 => Loc.Get("StatusOne"),
            _ => Loc.Get("StatusMany", _files.Count),
        };
    }

    /// <summary>Builds the merge options from the UI; <paramref name="error"/> is set if the device input is invalid.</summary>
    private MergeOptions TryBuildOptions(out string? error)
    {
        error = null;
        var options = new MergeOptions
        {
            Overlap = (OverlapMode)OverlapCombo.SelectedIndex,
            MarkGapsAsPause = PauseCheck.IsChecked == true,
        };
        if (DeviceOverrideCheck.IsChecked != true)
            return options;

        var name = DeviceCombo.Text.Trim();
        if (name.Length > 0)
        {
            try { options.DeviceProduct = GarminProducts.Resolve(name); }
            catch (ArgumentException ex) { error = ex.Message; }
        }
        var serial = SerialBox.Text.Trim();
        if (serial.Length > 0)
        {
            if (uint.TryParse(serial, out var parsed)) options.DeviceSerial = parsed;
            else error ??= Loc.Get("SerialInvalid", serial);
        }
        return options;
    }

    private void DeviceOverride_Changed(object sender, RoutedEventArgs e)
    {
        bool on = DeviceOverrideCheck.IsChecked == true;
        DeviceCombo.IsEnabled = on;
        SerialBox.IsEnabled = on;
        if (!on) { DeviceCombo.Text = ""; SerialBox.Text = ""; } // empty fields keep the source file's values
        RefreshAnalysis();
    }

    private void DeviceField_Changed(object sender, RoutedEventArgs e) => RefreshAnalysis();

    private async void Merge_Click(object sender, RoutedEventArgs e)
    {
        var first = _files.OrderBy(f => f.Start).First();
        var dialog = new SaveFileDialog
        {
            Filter = Loc.Get("SaveFilter"),
            FileName = $"merged_{first.Start.ToLocalTime():yyyy-MM-dd_HHmm}.fit",
            Title = Loc.Get("SaveTitle"),
        };
        if (dialog.ShowDialog(this) != true) return;

        var options = TryBuildOptions(out var deviceError);
        if (deviceError is not null)
        {
            MessageBox.Show(this, deviceError, Loc.Get("InvalidDeviceTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        try
        {
            MergeButton.IsEnabled = false;
            StatusText.Text = Loc.Get("StatusMerging");
            var files = _files.ToList();
            var device = FitMerger.ResolveDevice(files, options);
            await Task.Run(() => FitWriter.Write(dialog.FileName, FitMerger.Merge(files, options), device));

            // Read the target back and show the metadata that was actually written.
            var merged = await Task.Run(() => FitReader.Read(dialog.FileName));
            ShowVerification(dialog.FileName, merged);
            var details = $"{merged.Elapsed:h\\:mm\\:ss}" +
                (merged.DistanceMeters > 0 ? $", {merged.DistanceMeters / 1000:0.00} km" : "");
            StatusText.Text = Loc.Get("StatusSaved", Path.GetFileName(dialog.FileName), merged.Records.Count, details);
        }
        catch (Exception ex)
        {
            StatusText.Text = Loc.Get("StatusFailed");
            MessageBox.Show(this, ex.Message, Loc.Get("MergeFailedTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            MergeButton.IsEnabled = _files.Count >= 2;
        }
    }

    private void ShowVerification(string path, FitFile merged)
    {
        VerifyText.Text = $"{Path.GetFileName(path)}\n{FitReader.DescribeMetadata(merged)}";
        VerifyHeader.Visibility = Visibility.Visible;
        VerifyCard.Visibility = Visibility.Visible;
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            await AddFilesAsync(paths.Where(p => p.EndsWith(".fit", StringComparison.OrdinalIgnoreCase)));
    }
}
