using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SerkonDiskSuite.Core.Interfaces;
using SerkonDiskSuite.Core.Models;

namespace SerkonDiskSuite.App.ViewModels;

/// <summary>
/// "Teşhis" sekmesi: SMART self-test başlatma/sonuçları, firmware sürümü ve NVMe kritik uyarı
/// bayraklarını bir arada toplar. Self-test çalışırken durumu periyodik olarak (15 sn) yoklar
/// (smartctl'in kendisi arka planda çalışıp bitene kadar bloklamıyor).
/// </summary>
public partial class DiagnosticsViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    private readonly ISmartProvider _smartProvider;
    private DiskInfo? _disk;
    private CancellationTokenSource? _pollCts;

    [ObservableProperty] private string? _firmwareVersion;
    [ObservableProperty] private ObservableCollection<string> _criticalWarningFlags = [];
    [ObservableProperty] private SelfTestStatus? _selfTestStatus;
    [ObservableProperty] private SelfTestType _selectedSelfTestType = SelfTestType.Short;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartSelfTestCommand))]
    private bool _isBusy;

    public IReadOnlyList<SelfTestType> SelfTestTypes { get; } = [SelfTestType.Short, SelfTestType.Long];

    public DiagnosticsViewModel(ISmartProvider smartProvider) => _smartProvider = smartProvider;

    public void SetDisk(DiskInfo? disk)
    {
        _disk = disk;
        FirmwareVersion = disk?.FirmwareVersion;
        CriticalWarningFlags.Clear();
        SelfTestStatus = null;
        StatusMessage = null;
        StopPolling();

        if (disk is not null)
        {
            _ = RefreshAsync();
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_disk is null) return;
        IsBusy = true;
        try
        {
            var health = await _smartProvider.ReadHealthAsync(_disk);
            CriticalWarningFlags = new ObservableCollection<string>(health.CriticalWarningFlags);
            SelfTestStatus = await _smartProvider.GetSelfTestStatusAsync(_disk);
            if (SelfTestStatus.IsRunning)
                StartPolling();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Hata: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanStartSelfTest => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanStartSelfTest))]
    private async Task StartSelfTestAsync()
    {
        if (_disk is null) return;
        IsBusy = true;
        try
        {
            await _smartProvider.StartSelfTestAsync(_disk, SelectedSelfTestType);
            StatusMessage = "Self-test başlatıldı.";
            SelfTestStatus = await _smartProvider.GetSelfTestStatusAsync(_disk);
            StartPolling();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Hata: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void StartPolling()
    {
        if (_pollCts is not null || _disk is null) return;
        var disk = _disk;
        _pollCts = new CancellationTokenSource();
        _ = PollLoopAsync(disk, _pollCts.Token);
    }

    private void StopPolling()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = null;
    }

    private async Task PollLoopAsync(DiskInfo disk, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollInterval, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                var status = await _smartProvider.GetSelfTestStatusAsync(disk, ct);
                SelfTestStatus = status;
                if (!status.IsRunning)
                {
                    StopPolling();
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // Tek bir okuma hatası döngüyü durdurmasın; bir sonraki periyotta tekrar denenir.
            }
        }
    }

    public void Dispose() => StopPolling();
}
