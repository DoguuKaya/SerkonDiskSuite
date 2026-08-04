using CommunityToolkit.Mvvm.ComponentModel;
using SerkonDiskSuite.Core.Interfaces;

namespace SerkonDiskSuite.App.ViewModels;

/// <summary>Sistem/donanım genel bilgisini (işletim sistemi, CPU, anakart, BIOS) gösteren sayfa.</summary>
public partial class SystemViewModel : ObservableObject
{
    private readonly ISystemInfoProvider _systemInfoProvider;

    [ObservableProperty] private SystemSummary? _summary;

    public SystemViewModel(ISystemInfoProvider systemInfoProvider) => _systemInfoProvider = systemInfoProvider;

    public async Task LoadAsync(CancellationToken ct = default)
        => Summary = await _systemInfoProvider.GetSummaryAsync(ct);
}
