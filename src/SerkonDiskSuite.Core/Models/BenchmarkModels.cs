namespace SerkonDiskSuite.Core.Models;

/// <summary>Benchmark test parametreleri.</summary>
public sealed record BenchmarkOptions
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

    /// <summary>
    /// Sıralı testlerde (SequentialRead/Write) aynı anda kuyruğa alınacak I/O isteği sayısı
    /// (Queue Depth) — CrystalDiskMark'taki "Q" değeri. Rastgele testleri etkilemez; gerçek
    /// CrystalDiskMark'ta "SEQ1M Q8T1" gibi bir profil yalnızca sıralı testlere uygulanır,
    /// aynı anda seçili olabilecek rastgele profilin Q/T'sine dokunmaz (bkz. madde C1).
    /// Varsayılan 1 (Q1, eski tek-istekli davranış).
    /// </summary>
    public int SequentialQueueDepth { get; init; } = 1;

    /// <summary>Sıralı testlerde eşzamanlı iş parçacığı sayısı (Thread count) — "T" değeri.
    /// Varsayılan 1.</summary>
    public int SequentialThreadCount { get; init; } = 1;

    /// <summary>Rastgele testlerde (RandomRead/Write) kuyruk derinliği. Sıralı testleri etkilemez.
    /// Varsayılan 1.</summary>
    public int RandomQueueDepth { get; init; } = 1;

    /// <summary>Rastgele testlerde eşzamanlı iş parçacığı sayısı. Varsayılan 1.</summary>
    public int RandomThreadCount { get; init; } = 1;

    /// <summary>
    /// Uygulanan hazır profilin adı (ör. "SEQ1M Q8T1"); kullanıcı manuel değer girdiyse
    /// "Özel". Sonuç satırlarında hangi profille üretildiğini göstermek için taşınır.
    /// </summary>
    public string ProfileName { get; init; } = "Özel";
}

/// <summary>Tek bir testin sonucu.</summary>
public sealed record BenchmarkResult(
    BenchmarkTestKind Kind,
    double ThroughputMBps,
    double? Iops,
    TimeSpan Duration,
    int QueueDepth = 1,
    int ThreadCount = 1,
    string ProfileName = "Özel");

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
