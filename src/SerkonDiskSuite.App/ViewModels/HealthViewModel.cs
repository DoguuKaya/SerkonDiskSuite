using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SerkonDiskSuite.Core.Interfaces;
using SerkonDiskSuite.Core.Models;
using SkiaSharp;

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
    private readonly ISmartTrendStore _trendStore;
    private readonly ObservableCollection<DateTimePoint> _temperaturePoints = [];

    private DiskInfo? _disk;
    private CancellationTokenSource? _monitorCts;
    private bool _isTabActive;

    [ObservableProperty] private SmartHealth? _health;
    [ObservableProperty] private ObservableCollection<SmartAttribute> _attributes = [];
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _error;

    /// <summary>NVMe disklerde SMART öznitelik ID'si yok (hep "-"); bu yüzden tablodaki
    /// ID kolonu NVMe'de gizlenir, SATA'da görünür kalır.</summary>
    [ObservableProperty] private bool _isIdColumnVisible = true;

    /// <summary>LiveCharts'ın arka plan iş parçacığından güvenle güncellenebilmesi için kilit nesnesi.</summary>
    public object ChartSyncObject { get; } = new();

    // Koyu tema zemininde LiveChartsCore'un varsayılan eksen/ayraç renkleri (koyu gri/siyah)
    // neredeyse görünmez oluyordu; grafik "boş" görünüyordu. Açık, okunabilir tonlar veriliyor.
    private static readonly SolidColorPaint AxisTextPaint = new(new SKColor(0xC8, 0xC8, 0xC8));
    private static readonly SolidColorPaint AxisSeparatorPaint = new(new SKColor(0x55, 0x58, 0x5E), 1);
    private static readonly SolidColorPaint SeriesStrokePaint = new(new SKColor(0x60, 0xA5, 0xFA), 2);

    public ISeries[] TemperatureSeries { get; }

    public Axis[] TemperatureXAxes { get; } =
    [
        new Axis
        {
            Labeler = value => new DateTime((long)value).ToString("HH:mm:ss"),
            UnitWidth = PollInterval.Ticks,
            MinStep = PollInterval.Ticks,
            LabelsPaint = AxisTextPaint,
            SeparatorsPaint = AxisSeparatorPaint,
        }
    ];

    public Axis[] TemperatureYAxes { get; } =
    [
        new Axis
        {
            Name = "°C",
            NamePaint = AxisTextPaint,
            LabelsPaint = AxisTextPaint,
            SeparatorsPaint = AxisSeparatorPaint,
        }
    ];

    public HealthViewModel(ISmartProvider smartProvider, ISmartTrendStore trendStore)
    {
        _smartProvider = smartProvider;
        _trendStore = trendStore;
        TemperatureSeries =
        [
            new LineSeries<DateTimePoint>
            {
                Values = _temperaturePoints,
                GeometrySize = 0,
                LineSmoothness = 0.3,
                Name = "Sıcaklık",
                Stroke = SeriesStrokePaint,
                Fill = null,
            }
        ];
    }

    public void SetDisk(DiskInfo? disk)
    {
        _disk = disk;
        Health = null;
        Attributes.Clear();
        Error = null;
        IsIdColumnVisible = disk is null || disk.BusType != DiskBusType.Nvme;
        StopMonitoring();

        lock (ChartSyncObject)
        {
            _temperaturePoints.Clear();
        }

        if (disk is not null)
        {
            _ = RefreshAsync();
            _ = LoadHistoryThenStartMonitoringAsync(disk);
        }
    }

    /// <summary>
    /// Diskin daha önce kaydedilmiş trend geçmişini yükler (yalnızca canlı grafik penceresine
    /// düşen kısmı, ör. son 15 dakika), grafiğe ekler ve ardından (sekme görünürse) canlı
    /// izlemeyi başlatır.
    /// </summary>
    private async Task LoadHistoryThenStartMonitoringAsync(DiskInfo disk)
    {
        var cutoff = DateTimeOffset.Now - TimeSpan.FromMinutes(WindowMinutes);
        IReadOnlyList<SmartTrendPoint> history;
        try
        {
            history = await _trendStore.LoadAsync(GetDiskKey(disk));
        }
        catch
        {
            history = [];
        }

        // Geçmiş yüklenirken kullanıcı başka bir disk seçmiş olabilir; artık geçerli değilse uygulama.
        if (!ReferenceEquals(_disk, disk)) return;

        lock (ChartSyncObject)
        {
            foreach (var point in history)
            {
                if (point.Timestamp < cutoff || point.TemperatureCelsius is not { } temp) continue;
                _temperaturePoints.Add(new DateTimePoint(point.Timestamp.LocalDateTime, temp));
            }
        }

        if (_isTabActive)
            StartMonitoring();
    }

    private static string GetDiskKey(DiskInfo disk)
        => !string.IsNullOrWhiteSpace(disk.SerialNumber) ? disk.SerialNumber : disk.DevicePath;

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
        var diskKey = GetDiskKey(disk);
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

                    await _trendStore.AppendAsync(diskKey, new SmartTrendPoint(health.Timestamp, temp), ct);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // Tek bir okuma/yazma hatası izlemeyi durdurmasın; bir sonraki periyotta tekrar denenir.
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
