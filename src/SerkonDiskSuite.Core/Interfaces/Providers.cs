using SerkonDiskSuite.Core.Models;

namespace SerkonDiskSuite.Core.Interfaces;

/// <summary>Sistemdeki fiziksel diskleri keşfeder.</summary>
public interface IDiskProvider
{
    Task<IReadOnlyList<DiskInfo>> GetDisksAsync(CancellationToken ct = default);
}

/// <summary>Belirli bir diskin SMART sağlık verisini okur.</summary>
public interface ISmartProvider
{
    Task<SmartHealth> ReadHealthAsync(DiskInfo disk, CancellationToken ct = default);

    /// <summary>Alt araç (smartctl) sistemde kullanılabilir mi?</summary>
    Task<bool> IsAvailableAsync(CancellationToken ct = default);

    /// <summary>Kısa veya uzun bir SMART self-test başlatır. smartctl testi diskin kendi
    /// donanımında arka planda kuyruğa alır; bu çağrı test bitene kadar beklemez.</summary>
    Task StartSelfTestAsync(DiskInfo disk, SelfTestType type, CancellationToken ct = default);

    /// <summary>Devam eden veya son biten SMART self-test'in durumunu okur.</summary>
    Task<SelfTestStatus> GetSelfTestStatusAsync(DiskInfo disk, CancellationToken ct = default);
}

/// <summary>Disk okuma/yazma benchmark testlerini yürütür.</summary>
public interface IBenchmarkRunner
{
    Task<IReadOnlyList<BenchmarkResult>> RunAsync(
        BenchmarkOptions options,
        IProgress<BenchmarkProgress>? progress = null,
        CancellationToken ct = default);
}

/// <summary>Sistem/donanım genel bilgisi (CPU, anakart, PCIe link vb.).</summary>
public interface ISystemInfoProvider
{
    Task<SystemSummary> GetSummaryAsync(CancellationToken ct = default);
}

/// <summary>SMART okumalarının zaman içindeki trendini kalıcı olarak saklar (ör. JSON dosyası).</summary>
public interface ISmartTrendStore
{
    /// <summary>Belirli bir disk için daha önce kaydedilmiş trend noktalarını (kronolojik sırayla) yükler.</summary>
    Task<IReadOnlyList<SmartTrendPoint>> LoadAsync(string diskKey, CancellationToken ct = default);

    /// <summary>Belirli bir disk için yeni bir trend noktası ekler.</summary>
    Task AppendAsync(string diskKey, SmartTrendPoint point, CancellationToken ct = default);
}

/// <summary>CPU/GPU/RAM'in anlık donanım okumasını sağlar (HWiNFO'nun temel karşılığı).
/// Periyodik okuma/yaşam döngüsü (ne zaman başlayıp duracağı) çağıran taraf
/// (ViewModel) tarafında yönetilir — <see cref="ISmartProvider"/>'ın disk SMART
/// okumasında izlenen desenle tutarlı; ayrı bir Start/Stop metoduna gerek yok.</summary>
public interface IHardwareMonitorProvider
{
    Task<HardwareSnapshot> GetSnapshotAsync(CancellationToken ct = default);
}

/// <summary>CPU/GPU okumalarının zaman içindeki trendini kalıcı olarak saklar (ör. JSON dosyası).
/// <see cref="ISmartTrendStore"/>'un tek farkı: diskin aksine makinede tek bir CPU/GPU
/// olduğundan disk anahtarı gerekmez.</summary>
public interface IHardwareTrendStore
{
    Task<IReadOnlyList<HardwareTrendPoint>> LoadAsync(CancellationToken ct = default);

    Task AppendAsync(HardwareTrendPoint point, CancellationToken ct = default);
}

/// <summary>Windows'un Sanallaştırma Tabanlı Güvenlik (VBS) / Bellek Bütünlüğü (HVCI) durumunu
/// tespit eder. Bu özellik etkinken CPU/anakart sıcaklık sensörleri LibreHardwareMonitor'ün
/// imzasız çekirdek sürücüsüyle okunamaz (Windows'un kasıtlı güvenlik sınırı, kodda düzeltilemez —
/// bkz. https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/issues/566).</summary>
public interface IVbsStatusProvider
{
    /// <summary>Bellek Bütünlüğü (HVCI) şu anda çalışıyor mu? Tespit edilemezse null.</summary>
    Task<bool?> IsMemoryIntegrityRunningAsync(CancellationToken ct = default);
}

/// <summary>Sistem özeti modeli.</summary>
public sealed class SystemSummary
{
    public string OsName { get; init; } = string.Empty;
    public string CpuName { get; init; } = string.Empty;
    public string MotherboardName { get; init; } = string.Empty;
    public string BiosVersion { get; init; } = string.Empty;
    public long TotalMemoryBytes { get; init; }
}
