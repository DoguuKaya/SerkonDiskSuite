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

/// <summary>Sistem özeti modeli.</summary>
public sealed class SystemSummary
{
    public string OsName { get; init; } = string.Empty;
    public string CpuName { get; init; } = string.Empty;
    public string MotherboardName { get; init; } = string.Empty;
    public string BiosVersion { get; init; } = string.Empty;
    public long TotalMemoryBytes { get; init; }
}
