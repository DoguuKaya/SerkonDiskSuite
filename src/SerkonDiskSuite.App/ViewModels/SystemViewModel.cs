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

    private const string GenericTemperatureUnavailableMessage = "Bu sistemde okunamıyor";
    private const string VbsTemperatureUnavailableMessage =
        "Bu sistemde okunamıyor (Bellek Bütünlüğü/VBS etkin — çekirdek sürücüsü sıcaklık sensörüne erişemez)";

    private readonly ISystemInfoProvider _systemInfoProvider;
    private readonly IHardwareMonitorProvider _hardwareMonitorProvider;
    private readonly IHardwareTrendStore _trendStore;
    private readonly IVbsStatusProvider _vbsStatusProvider;
    private readonly ObservableCollection<DateTimePoint> _cpuLoadPoints = [];
    private readonly ObservableCollection<DateTimePoint> _cpuTemperaturePoints = [];

    private CancellationTokenSource? _monitorCts;
    private bool _isPageActive;
    private bool _historyLoaded;

    [ObservableProperty] private SystemSummary? _summary;
    [ObservableProperty] private HardwareSnapshot? _hardware;

    /// <summary>CPU/GPU sıcaklık sensörü okunamadığında gösterilecek mesaj. VBS/Bellek
    /// Bütünlüğü çalışıyorsa (tespit edilebiliyorsa) bunu açıklayan özel bir mesaja döner —
    /// bu, uygulamanın kodunda düzeltemeyeceği bilinen bir Windows güvenlik sınırıdır
    /// (bkz. https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/issues/566).</summary>
    [ObservableProperty] private string _temperatureUnavailableMessage = GenericTemperatureUnavailableMessage;

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
            // MinLimit/MaxLimit AÇIKÇA veriliyor: bu makinede (ve VBS/Bellek Bütünlüğü etkin
            // birçok Windows 11 sisteminde) CPU sıcaklığı hep null kalabiliyor, bu durumda
            // _cpuTemperaturePoints asla dolmuyor. Sınır verilmezse LiveChartsCore bu eksenin
            // ölçeğini veriden otomatik hesaplamaya çalışır; veri hiç yoksa bu hesap tanımsız
            // kalıp TÜM grafiğin (diğer, verisi olan seri dahil) render edilmemesine yol
            // açabilir (SORUN 2). Sabit bir aralık (gerçekçi CPU sıcaklık üst sınırı) bu
            // eksenin veri olmasa bile her zaman geçerli bir ölçeğe sahip olmasını garanti eder.
            Name = "°C", NamePaint = AxisTextPaint, LabelsPaint = AxisTextPaint,
            SeparatorsPaint = null, Position = AxisPosition.End,
            MinLimit = 0, MaxLimit = 110,
        },
    ];

    public SystemViewModel(
        ISystemInfoProvider systemInfoProvider,
        IHardwareMonitorProvider hardwareMonitorProvider,
        IHardwareTrendStore trendStore,
        IVbsStatusProvider vbsStatusProvider)
    {
        _systemInfoProvider = systemInfoProvider;
        _hardwareMonitorProvider = hardwareMonitorProvider;
        _trendStore = trendStore;
        _vbsStatusProvider = vbsStatusProvider;

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
    {
        Summary = await _systemInfoProvider.GetSummaryAsync(ct);

        // VBS durumu çalışma zamanında değişmez (kullanıcı Windows Güvenliği'nden değiştirse
        // bile yeniden başlatma gerektirir); bu yüzden 5 sn'lik izleme döngüsünde tekrar tekrar
        // sorulmuyor, uygulama açılışında bir kez kontrol ediliyor.
        bool? isMemoryIntegrityRunning;
        try
        {
            isMemoryIntegrityRunning = await _vbsStatusProvider.IsMemoryIntegrityRunningAsync(ct);
        }
        catch
        {
            isMemoryIntegrityRunning = null;
        }

        TemperatureUnavailableMessage = isMemoryIntegrityRunning == true
            ? VbsTemperatureUnavailableMessage
            : GenericTemperatureUnavailableMessage;
    }

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
        var ct = _monitorCts.Token;
        _ = _historyLoaded ? MonitorLoopAsync(ct) : LoadHistoryThenMonitorAsync(ct);
    }

    /// <summary>Daha önce kaydedilmiş CPU trend geçmişini (yalnızca canlı grafik penceresine
    /// düşen kısmı) grafiğe önceden doldurur, ardından izleme döngüsünü başlatır —
    /// HealthViewModel'in disk sıcaklık geçmişini yüklemesiyle aynı desen. Yalnızca ilk
    /// başlatmada çalışır (sayfadan çıkıp geri dönmek geçmişi tekrar yüklemez).</summary>
    private async Task LoadHistoryThenMonitorAsync(CancellationToken ct)
    {
        _historyLoaded = true;
        var cutoff = DateTimeOffset.Now - TimeSpan.FromMinutes(WindowMinutes);
        IReadOnlyList<HardwareTrendPoint> history;
        try
        {
            history = await _trendStore.LoadAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch
        {
            history = [];
        }

        lock (ChartSyncObject)
        {
            foreach (var point in history)
            {
                if (point.Timestamp < cutoff)
                {
                    continue;
                }

                if (point.CpuLoadPercent is { } load)
                {
                    _cpuLoadPoints.Add(new DateTimePoint(point.Timestamp.LocalDateTime, load));
                }

                if (point.CpuTemperatureCelsius is { } temp)
                {
                    _cpuTemperaturePoints.Add(new DateTimePoint(point.Timestamp.LocalDateTime, temp));
                }
            }

            if (_cpuLoadPoints.Count >= 2 || _cpuTemperaturePoints.Count >= 2)
            {
                HasCpuChartData = true;
            }
        }

        await MonitorLoopAsync(ct);
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

                // Trend kaydı önce yapılır: grafik güncellemesi (aşağıda) LiveChartsCore'un
                // kendi çizim/ölçekleme mantığını tetikleyebilir; bu kalıcı kayıttan tamamen
                // ayrı bir sorumluluk olduğundan, çizim tarafında çıkabilecek bir istisnanın
                // (ör. SORUN 2) trend dosyasının güncellenmesini etkilememesi için AppendAsync
                // artık chart nokta ekleme bloğundan ÖNCE çağrılıyor (SORUN 3'ün savunması).
                if (snapshot.CpuTemperatureCelsius is not null || snapshot.CpuLoadPercent is not null
                    || snapshot.GpuTemperatureCelsius is not null || snapshot.GpuLoadPercent is not null)
                {
                    await _trendStore.AppendAsync(
                        new HardwareTrendPoint(
                            snapshot.Timestamp,
                            snapshot.CpuTemperatureCelsius,
                            snapshot.CpuLoadPercent,
                            snapshot.GpuTemperatureCelsius,
                            snapshot.GpuLoadPercent),
                        ct);
                }

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
