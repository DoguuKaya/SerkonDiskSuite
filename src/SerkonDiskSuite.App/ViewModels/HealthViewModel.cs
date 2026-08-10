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
    private readonly ObservableCollection<DateTimePoint> _historyTemperaturePoints = [];
    private readonly ObservableCollection<DateTimePoint> _historyRemainingLifePoints = [];

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

    /// <summary>Canlı sıcaklık grafiğinde en az bir nokta var mı? Yoksa "Veri bekleniyor..."
    /// yer tutucusu gösterilir (aksi hâlde grafik tamamen boş bir alan gibi görünür).</summary>
    [ObservableProperty] private bool _hasTemperatureData;

    /// <summary>LiveCharts'ın arka plan iş parçacığından güvenle güncellenebilmesi için kilit nesnesi.</summary>
    public object ChartSyncObject { get; } = new();

    // Koyu tema zemininde LiveChartsCore'un varsayılan eksen/ayraç renkleri (koyu gri/siyah)
    // neredeyse görünmez oluyordu; grafik "boş" görünüyordu. Açık, okunabilir tonlar veriliyor.
    private static readonly SolidColorPaint AxisTextPaint = new(new SKColor(0xC8, 0xC8, 0xC8));
    private static readonly SolidColorPaint AxisSeparatorPaint = new(new SKColor(0x55, 0x58, 0x5E), 1);
    private static readonly SolidColorPaint SeriesStrokePaint = new(new SKColor(0x60, 0xA5, 0xFA), 2);
    private static readonly SolidColorPaint LifeSeriesStrokePaint = new(new SKColor(0x4A, 0xDE, 0x80), 2);

    public ISeries[] TemperatureSeries { get; }

    /// <summary>Diskin tüm kaydedilmiş geçmişi (canlı grafiğin 15 dakikalık penceresinden
    /// bağımsız — %LOCALAPPDATA%\SerkonDiskSuite\trend\ altındaki dosyanın tamamı).</summary>
    public ISeries[] HistoryTemperatureSeries { get; }
    public ISeries[] HistoryRemainingLifeSeries { get; }

    public Axis[] HistoryXAxes { get; } =
    [
        new Axis
        {
            Labeler = value => new DateTime((long)value).ToString("dd MMM HH:mm"),
            LabelsPaint = AxisTextPaint,
            SeparatorsPaint = AxisSeparatorPaint,
        }
    ];

    public Axis[] HistoryTemperatureYAxes { get; } =
    [
        new Axis { Name = "°C", NamePaint = AxisTextPaint, LabelsPaint = AxisTextPaint, SeparatorsPaint = AxisSeparatorPaint }
    ];

    public Axis[] HistoryRemainingLifeYAxes { get; } =
    [
        new Axis
        {
            Name = "%", NamePaint = AxisTextPaint, LabelsPaint = AxisTextPaint, SeparatorsPaint = AxisSeparatorPaint,
            MinLimit = 0, MaxLimit = 100,
        }
    ];

    public Axis[] TemperatureXAxes { get; } =
    [
        new Axis
        {
            // UnitWidth/MinStep = PollInterval.Ticks BİLEREK KALDIRILDI (madde 47): bu, canlı
            // grafiğin (Trend Geçmişi'nin aksine) hiçbir eksen/çizgi olmadan TAMAMEN boş
            // görünmesinin gerçek kök nedeniydi — kullanıcının kendi gözlemiyle (Trend
            // Geçmişi eksenleri görünüyor, bu eksen hiç yoktu) doğrulandı: bu ikisi arasındaki
            // TEK yapısal fark buydu. LiveChartsCore'un ölçek hesabını veriden otomatik
            // yapmasına bırakılıyor (History eksenlerinde zaten çalışan aynı yaklaşım).
            Labeler = value => new DateTime((long)value).ToString("HH:mm:ss"),
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
        HistoryTemperatureSeries =
        [
            new LineSeries<DateTimePoint>
            {
                Values = _historyTemperaturePoints,
                GeometrySize = 0,
                LineSmoothness = 0.3,
                Name = "Sıcaklık (°C)",
                Stroke = SeriesStrokePaint,
                Fill = null,
            }
        ];
        HistoryRemainingLifeSeries =
        [
            new LineSeries<DateTimePoint>
            {
                Values = _historyRemainingLifePoints,
                GeometrySize = 0,
                LineSmoothness = 0.3,
                Name = "Kalan Ömür (%)",
                Stroke = LifeSeriesStrokePaint,
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
            _historyTemperaturePoints.Clear();
            _historyRemainingLifePoints.Clear();
        }
        HasTemperatureData = false;

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
                if (point.TemperatureCelsius is { } temp)
                {
                    _historyTemperaturePoints.Add(new DateTimePoint(point.Timestamp.LocalDateTime, temp));
                    if (point.Timestamp >= cutoff)
                    {
                        _temperaturePoints.Add(new DateTimePoint(point.Timestamp.LocalDateTime, temp));
                        // LineSeries GeometrySize=0 (madde 15) olduğundan tek nokta hiçbir şey
                        // çizmez (çizgi için en az 2 nokta gerekir); yer tutucu bu yüzden 2.
                        // noktaya kadar görünür kalır (bkz. madde A1).
                        if (_temperaturePoints.Count >= 2)
                            HasTemperatureData = true;
                    }
                }
                if (point.RemainingLifePercent is { } life)
                {
                    _historyRemainingLifePoints.Add(new DateTimePoint(point.Timestamp.LocalDateTime, life));
                }
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
                if (health.TemperatureCelsius is not null || health.RemainingLifePercent is not null)
                {
                    lock (ChartSyncObject)
                    {
                        if (health.TemperatureCelsius is { } temp)
                        {
                            _temperaturePoints.Add(new DateTimePoint(health.Timestamp.LocalDateTime, temp));
                            while (_temperaturePoints.Count > MaxPoints)
                                _temperaturePoints.RemoveAt(0);
                            _historyTemperaturePoints.Add(new DateTimePoint(health.Timestamp.LocalDateTime, temp));
                            if (_temperaturePoints.Count >= 2)
                                HasTemperatureData = true;
                        }
                        if (health.RemainingLifePercent is { } life)
                        {
                            _historyRemainingLifePoints.Add(new DateTimePoint(health.Timestamp.LocalDateTime, life));
                        }
                    }

                    await _trendStore.AppendAsync(
                        diskKey,
                        new SmartTrendPoint(health.Timestamp, health.TemperatureCelsius, health.RemainingLifePercent),
                        ct);
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
