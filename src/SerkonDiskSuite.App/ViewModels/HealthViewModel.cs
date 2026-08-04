using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using SerkonDiskSuite.Core.Interfaces;
using SerkonDiskSuite.Core.Models;

namespace SerkonDiskSuite.App.ViewModels;

/// <summary>
/// Seçili diskin SMART sağlık verilerini ve gerçek zamanlı sıcaklık grafiğini gösteren sekme.
/// Sıcaklık grafiği, sekme görünürken periyodik olarak SMART okuyan iptal edilebilir bir arka
/// plan döngüsüyle beslenir (bkz. <see cref="SetMonitoringActive"/>).
/// </summary>
public partial class HealthViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private const int WindowMinutes = 15;
    private static readonly int MaxPoints = (int)(WindowMinutes * 60 / PollInterval.TotalSeconds);

    private readonly ISmartProvider _smartProvider;
    private readonly ObservableCollection<DateTimePoint> _temperaturePoints = [];

    private DiskInfo? _disk;
    private CancellationTokenSource? _monitorCts;
    private bool _isTabActive;

    [ObservableProperty] private SmartHealth? _health;
    [ObservableProperty] private ObservableCollection<SmartAttribute> _attributes = [];
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _error;

    /// <summary>LiveCharts'ın arka plan iş parçacığından güvenle güncellenebilmesi için kilit nesnesi.</summary>
    public object ChartSyncObject { get; } = new();

    public ISeries[] TemperatureSeries { get; }

    public Axis[] TemperatureXAxes { get; } =
    [
        new Axis
        {
            Labeler = value => new DateTime((long)value).ToString("HH:mm:ss"),
            UnitWidth = PollInterval.Ticks,
            MinStep = PollInterval.Ticks,
        }
    ];

    public Axis[] TemperatureYAxes { get; } = [new Axis { Name = "°C" }];

    public HealthViewModel(ISmartProvider smartProvider)
    {
        _smartProvider = smartProvider;
        TemperatureSeries =
        [
            new LineSeries<DateTimePoint>
            {
                Values = _temperaturePoints,
                GeometrySize = 0,
                LineSmoothness = 0.3,
                Name = "Sıcaklık"
            }
        ];
    }

    public void SetDisk(DiskInfo? disk)
    {
        _disk = disk;
        Health = null;
        Attributes.Clear();
        Error = null;

        lock (ChartSyncObject)
        {
            _temperaturePoints.Clear();
        }

        StopMonitoring();
        if (disk is not null)
        {
            _ = RefreshAsync();
            if (_isTabActive)
                StartMonitoring();
        }
    }

    /// <summary>Sağlık sekmesi görünür/görünmez olduğunda çağrılır; sekme kapandığında izleme durur.</summary>
    public void SetMonitoringActive(bool active)
    {
        _isTabActive = active;
        if (active && _disk is not null)
            StartMonitoring();
        else
            StopMonitoring();
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

    private void StartMonitoring()
    {
        if (_monitorCts is not null || _disk is null) return;
        var disk = _disk;
        _monitorCts = new CancellationTokenSource();
        _ = MonitorLoopAsync(disk, _monitorCts.Token);
    }

    private void StopMonitoring()
    {
        _monitorCts?.Cancel();
        _monitorCts?.Dispose();
        _monitorCts = null;
    }

    private async Task MonitorLoopAsync(DiskInfo disk, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var health = await _smartProvider.ReadHealthAsync(disk, ct);
                if (health.TemperatureCelsius is { } temp)
                {
                    lock (ChartSyncObject)
                    {
                        _temperaturePoints.Add(new DateTimePoint(health.Timestamp.LocalDateTime, temp));
                        while (_temperaturePoints.Count > MaxPoints)
                            _temperaturePoints.RemoveAt(0);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // Tek bir okuma hatası izlemeyi durdurmasın; bir sonraki periyotta tekrar denenir.
            }

            try
            {
                await Task.Delay(PollInterval, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public void Dispose() => StopMonitoring();
}
