using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SerkonDiskSuite.Core.Interfaces;
using SerkonDiskSuite.Core.Models;
using SkiaSharp;

namespace SerkonDiskSuite.App.ViewModels;

/// <summary>
/// Sistem/donanım genel bilgisini (statik: OS/CPU/RAM/anakart/BIOS) ve CPU/GPU/RAM'in gerçek
/// zamanlı donanım izlemesini (HWiNFO'nun temel karşılığı) gösteren sayfa. Donanım okuma
/// döngüsü, Sağlık sekmesindeki sıcaklık izlemesiyle aynı desende (5 sn, sayfa görünürken
/// çalışır) <see cref="SetMonitoringActive"/> ile yönetilir.
/// </summary>
public partial class SystemViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private const int WindowMinutes = 15;
    private static readonly int MaxPoints = (int)(WindowMinutes * 60 / PollInterval.TotalSeconds);

    private readonly ISystemInfoProvider _systemInfoProvider;
    private readonly IHardwareMonitorProvider _hardwareMonitorProvider;
    private readonly ObservableCollection<DateTimePoint> _cpuLoadPoints = [];
    private readonly ObservableCollection<DateTimePoint> _cpuTemperaturePoints = [];

    private CancellationTokenSource? _monitorCts;
    private bool _isPageActive;

    [ObservableProperty] private SystemSummary? _summary;
    [ObservableProperty] private HardwareSnapshot? _hardware;

    /// <summary>RAM kullanım yüzdesi (0-100), <see cref="Hardware"/> her değiştiğinde
    /// yeniden hesaplanır. Toplam bilinmiyorsa null (ProgressBar boş kalır).</summary>
    [ObservableProperty] private double? _ramUsedPercent;

    partial void OnHardwareChanged(HardwareSnapshot? value)
    {
        RamUsedPercent = value is { RamUsedBytes: { } used, RamTotalBytes: > 0 and { } total }
            ? used / (double)total * 100
            : null;
    }

    /// <summary>Canlı CPU grafiğinde en az bir çizgi çizilebilir mi (2+ nokta)? Aksi hâlde
    /// "Veri bekleniyor..." yer tutucusu gösterilir — HealthViewModel'in madde A1'de bulduğu
    /// desenle tutarlı (GeometrySize=0 tek noktada hiçbir şey çizmez).</summary>
    [ObservableProperty] private bool _hasCpuChartData;

    /// <summary>LiveCharts'ın arka plan iş parçacığından güvenle güncellenebilmesi için kilit nesnesi.</summary>
    public object ChartSyncObject { get; } = new();

    private static readonly SolidColorPaint AxisTextPaint = new(new SKColor(0xC8, 0xC8, 0xC8));
    private static readonly SolidColorPaint AxisSeparatorPaint = new(new SKColor(0x55, 0x58, 0x5E), 1);
    private static readonly SolidColorPaint LoadSeriesPaint = new(new SKColor(0x60, 0xA5, 0xFA), 2);
    private static readonly SolidColorPaint TemperatureSeriesPaint = new(new SKColor(0xFB, 0x92, 0x3C), 2);

    public ISeries[] CpuSeries { get; }

    public Axis[] CpuXAxes { get; } =
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

    public Axis[] CpuYAxes { get; } =
    [
        new Axis
        {
            Name = "Yük %", NamePaint = AxisTextPaint, LabelsPaint = AxisTextPaint,
            SeparatorsPaint = AxisSeparatorPaint, MinLimit = 0, MaxLimit = 100,
        },
        new Axis
        {
            Name = "°C", NamePaint = AxisTextPaint, LabelsPaint = AxisTextPaint,
            SeparatorsPaint = null, Position = AxisPosition.End,
        },
    ];

    public SystemViewModel(ISystemInfoProvider systemInfoProvider, IHardwareMonitorProvider hardwareMonitorProvider)
    {
        _systemInfoProvider = systemInfoProvider;
        _hardwareMonitorProvider = hardwareMonitorProvider;

        CpuSeries =
        [
            new LineSeries<DateTimePoint>
            {
                Values = _cpuLoadPoints,
                GeometrySize = 0,
                LineSmoothness = 0.3,
                Name = "CPU Yük (%)",
                Stroke = LoadSeriesPaint,
                Fill = null,
                ScalesYAt = 0,
            },
            new LineSeries<DateTimePoint>
            {
                Values = _cpuTemperaturePoints,
                GeometrySize = 0,
                LineSmoothness = 0.3,
                Name = "CPU Sıcaklık (°C)",
                Stroke = TemperatureSeriesPaint,
                Fill = null,
                ScalesYAt = 1,
            },
        ];
    }

    public async Task LoadAsync(CancellationToken ct = default)
        => Summary = await _systemInfoProvider.GetSummaryAsync(ct);

    /// <summary>Sistem sekmesi görünür/görünmez olduğunda çağrılır; sekme kapandığında izleme durur.</summary>
    public void SetMonitoringActive(bool active)
    {
        _isPageActive = active;
        if (active)
        {
            StartMonitoring();
        }
        else
        {
            StopMonitoring();
        }
    }

    private void StartMonitoring()
    {
        if (_monitorCts is not null || !_isPageActive)
        {
            return;
        }

        _monitorCts = new CancellationTokenSource();
        _ = MonitorLoopAsync(_monitorCts.Token);
    }

    private void StopMonitoring()
    {
        _monitorCts?.Cancel();
        _monitorCts?.Dispose();
        _monitorCts = null;
    }

    private async Task MonitorLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var snapshot = await _hardwareMonitorProvider.GetSnapshotAsync(ct);
                Hardware = snapshot;

                lock (ChartSyncObject)
                {
                    if (snapshot.CpuLoadPercent is { } load)
                    {
                        _cpuLoadPoints.Add(new DateTimePoint(snapshot.Timestamp.LocalDateTime, load));
                        while (_cpuLoadPoints.Count > MaxPoints)
                        {
                            _cpuLoadPoints.RemoveAt(0);
                        }
                    }

                    if (snapshot.CpuTemperatureCelsius is { } temp)
                    {
                        _cpuTemperaturePoints.Add(new DateTimePoint(snapshot.Timestamp.LocalDateTime, temp));
                        while (_cpuTemperaturePoints.Count > MaxPoints)
                        {
                            _cpuTemperaturePoints.RemoveAt(0);
                        }
                    }

                    if (_cpuLoadPoints.Count >= 2 || _cpuTemperaturePoints.Count >= 2)
                    {
                        HasCpuChartData = true;
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
