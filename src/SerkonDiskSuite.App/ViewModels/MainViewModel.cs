using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SerkonDiskSuite.Core.Interfaces;
using SerkonDiskSuite.Core.Models;
using SerkonDiskSuite.Core.Reporting;

namespace SerkonDiskSuite.App.ViewModels;

/// <summary>
/// Ana pencere ViewModel'i. Disk listesini yükler ve seçili diske göre
/// sağlık/benchmark alt-ViewModel'lerini besler.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly IDiskProvider _diskProvider;
    private readonly ISmartProvider _smartProvider;

    public HealthViewModel Health { get; }
    public BenchmarkViewModel Benchmark { get; }
    public SystemViewModel System { get; }
    public DiagnosticsViewModel Diagnostics { get; }

    [ObservableProperty]
    private ObservableCollection<DiskInfo> _disks = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedDisk))]
    [NotifyCanExecuteChangedFor(nameof(ExportReportCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyReportToClipboardCommand))]
    private DiskInfo? _selectedDisk;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _statusMessage;

    public bool HasSelectedDisk => SelectedDisk is not null;

    public MainViewModel(
        IDiskProvider diskProvider,
        ISmartProvider smartProvider,
        HealthViewModel health,
        BenchmarkViewModel benchmark,
        SystemViewModel system,
        DiagnosticsViewModel diagnostics)
    {
        _diskProvider = diskProvider;
        _smartProvider = smartProvider;
        Health = health;
        Benchmark = benchmark;
        System = system;
        Diagnostics = diagnostics;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        StatusMessage = "Diskler taranıyor...";
        try
        {
            await System.LoadAsync();

            var disks = await _diskProvider.GetDisksAsync();
            Disks = new ObservableCollection<DiskInfo>(disks);
            SelectedDisk = Disks.FirstOrDefault();

            if (!await _smartProvider.IsAvailableAsync())
                StatusMessage = "Uyarı: smartctl bulunamadı. SMART verileri okunamayabilir.";
            else
                StatusMessage = $"{Disks.Count} disk bulundu.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Hata: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSelectedDiskChanged(DiskInfo? value)
    {
        Health.SetDisk(value);
        Benchmark.SetDisk(value);
        Diagnostics.SetDisk(value);
    }

    private bool CanExportOrCopyReport => SelectedDisk is not null;

    /// <summary>Seçili diskin SMART verisi + son benchmark sonuçlarını hem düz metin (.txt)
    /// hem JSON (.json) olarak, kullanıcının seçtiği konuma kaydeder.</summary>
    [RelayCommand(CanExecute = nameof(CanExportOrCopyReport))]
    private void ExportReport()
    {
        if (SelectedDisk is not { } disk) return;

        var dialog = new SaveFileDialog
        {
            Title = "Rapor Dışa Aktar",
            Filter = "Metin dosyası (*.txt)|*.txt",
            FileName = $"{disk.ModelName}_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            string txtPath = dialog.FileName;
            string jsonPath = Path.ChangeExtension(txtPath, ".json");

            var results = Benchmark.Results.ToList();
            File.WriteAllText(txtPath, DiskReportBuilder.BuildPlainText(disk, Health.Health, results, System.Hardware, System.Summary?.RamModules));
            File.WriteAllText(jsonPath, DiskReportBuilder.BuildJson(disk, Health.Health, results, System.Hardware, System.Summary?.RamModules));

            StatusMessage = $"Rapor kaydedildi: {txtPath} / {Path.GetFileName(jsonPath)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Rapor kaydedilemedi: {ex.Message}";
        }
    }

    /// <summary>CrystalDiskInfo tarzı düz metin özetini panoya kopyalar.</summary>
    [RelayCommand(CanExecute = nameof(CanExportOrCopyReport))]
    private void CopyReportToClipboard()
    {
        if (SelectedDisk is not { } disk) return;

        try
        {
            string text = DiskReportBuilder.BuildPlainText(disk, Health.Health, Benchmark.Results.ToList(), System.Hardware, System.Summary?.RamModules);
            Clipboard.SetText(text);
            StatusMessage = "Rapor panoya kopyalandı.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Panoya kopyalanamadı: {ex.Message}";
        }
    }
}
