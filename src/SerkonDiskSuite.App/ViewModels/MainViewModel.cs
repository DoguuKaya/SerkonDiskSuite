using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SerkonDiskSuite.Core.Interfaces;
using SerkonDiskSuite.Core.Models;

namespace SerkonDiskSuite.App.ViewModels;

/// <summary>
/// Ana pencere ViewModel'i. Disk listesini yükler ve seçili diske göre
/// sağlık/benchmark alt-ViewModel'lerini besler.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly IDiskProvider _diskProvider;
    private readonly ISmartProvider _smartProvider;
    private readonly ISystemInfoProvider _systemInfoProvider;

    public HealthViewModel Health { get; }
    public BenchmarkViewModel Benchmark { get; }

    [ObservableProperty]
    private ObservableCollection<DiskInfo> _disks = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedDisk))]
    private DiskInfo? _selectedDisk;

    [ObservableProperty]
    private SystemSummary? _system;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _statusMessage;

    public bool HasSelectedDisk => SelectedDisk is not null;

    public MainViewModel(
        IDiskProvider diskProvider,
        ISmartProvider smartProvider,
        ISystemInfoProvider systemInfoProvider,
        HealthViewModel health,
        BenchmarkViewModel benchmark)
    {
        _diskProvider = diskProvider;
        _smartProvider = smartProvider;
        _systemInfoProvider = systemInfoProvider;
        Health = health;
        Benchmark = benchmark;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        StatusMessage = "Diskler taranıyor...";
        try
        {
            System = await _systemInfoProvider.GetSummaryAsync();

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
    }
}
