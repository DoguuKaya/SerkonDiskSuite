using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SerkonDiskSuite.Core.Interfaces;
using SerkonDiskSuite.Core.Models;

namespace SerkonDiskSuite.App.ViewModels;

/// <summary>Seçili diskin SMART sağlık verilerini gösteren sekme.</summary>
public partial class HealthViewModel : ObservableObject
{
    private readonly ISmartProvider _smartProvider;
    private DiskInfo? _disk;

    [ObservableProperty] private SmartHealth? _health;
    [ObservableProperty] private ObservableCollection<SmartAttribute> _attributes = [];
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _error;

    public HealthViewModel(ISmartProvider smartProvider) => _smartProvider = smartProvider;

    public void SetDisk(DiskInfo? disk)
    {
        _disk = disk;
        Health = null;
        Attributes.Clear();
        Error = null;
        if (disk is not null)
            _ = RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_disk is null) return;
        IsBusy = true;
        Error = null;
        try
        {
            var health = await _smartProvider.ReadHealthAsync(_disk);
            Health = health;
            Attributes = new ObservableCollection<SmartAttribute>(health.Attributes);
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
