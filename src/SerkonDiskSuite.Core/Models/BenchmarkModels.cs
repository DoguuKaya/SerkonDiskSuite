namespace SerkonDiskSuite.Core.Models;

/// <summary>Benchmark test parametreleri.</summary>
public sealed class BenchmarkOptions
{
    /// <summary>Testin çalışacağı sürücü kökü (ör. "S:\").</summary>
    public required string TargetPath { get; init; }

    /// <summary>Her test için kullanılacak dosya boyutu (bayt). Varsayılan 1 GiB.</summary>
    public long TestFileSizeBytes { get; init; } = 1L * 1024 * 1024 * 1024;

    /// <summary>Ölçüm tekrar sayısı (en iyisi raporlanır). Varsayılan 3.</summary>
    public int Passes { get; init; } = 3;

    /// <summary>Sıralı testlerde blok boyutu (bayt). Varsayılan 1 MiB.</summary>
    public int SequentialBlockSize { get; init; } = 1024 * 1024;

    /// <summary>Rastgele testlerde blok boyutu (bayt). Varsayılan 4 KiB.</summary>
    public int RandomBlockSize { get; init; } = 4 * 1024;
}

/// <summary>Tek bir testin sonucu.</summary>
public sealed record BenchmarkResult(
    BenchmarkTestKind Kind,
    double ThroughputMBps,
    double? Iops,
    TimeSpan Duration);

public enum BenchmarkTestKind
{
    SequentialRead,
    SequentialWrite,
    RandomRead,
    RandomWrite
}

/// <summary>Benchmark sırasında UI'a ilerleme bildirmek için.</summary>
public sealed record BenchmarkProgress(
    BenchmarkTestKind CurrentTest,
    int CurrentPass,
    int TotalPasses,
    double PercentComplete,
    string StatusMessage);
