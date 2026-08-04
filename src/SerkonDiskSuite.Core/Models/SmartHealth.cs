namespace SerkonDiskSuite.Core.Models;

/// <summary>
/// Bir diskin SMART sağlık verilerinin özet + detay gösterimi.
/// </summary>
public sealed class SmartHealth
{
    public required string DevicePath { get; init; }

    /// <summary>Genel sağlık durumu (smartctl "PASSED"/"FAILED" veya hesaplanmış).</summary>
    public HealthStatus OverallStatus { get; init; } = HealthStatus.Unknown;

    /// <summary>Sıcaklık (santigrat). Okunamıyorsa null.</summary>
    public int? TemperatureCelsius { get; init; }

    /// <summary>Kalan ömür yüzdesi (0-100). NVMe "percentage_used"ten türetilir.</summary>
    public int? RemainingLifePercent { get; init; }

    /// <summary>Toplam okunan veri (bayt).</summary>
    public long? TotalBytesRead { get; init; }

    /// <summary>Toplam yazılan veri (bayt).</summary>
    public long? TotalBytesWritten { get; init; }

    /// <summary>Güç verilme süresi (saat).</summary>
    public long? PowerOnHours { get; init; }

    /// <summary>Açılış sayısı.</summary>
    public long? PowerCycleCount { get; init; }

    /// <summary>Güvensiz (ani) kapanma sayısı.</summary>
    public long? UnsafeShutdowns { get; init; }

    /// <summary>Kullanılabilir yedek yüzdesi (0-100). NVMe "available_spare" alanından gelir.</summary>
    public int? AvailableSparePercent { get; init; }

    /// <summary>Ham SMART öznitelikleri (tabloda göstermek için).</summary>
    public IReadOnlyList<SmartAttribute> Attributes { get; init; } = [];

    /// <summary>Bu okumanın alındığı an.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
}

/// <summary>Tek bir SMART özniteliği (ID + isim + ham/normalleştirilmiş değer).</summary>
public sealed record SmartAttribute(
    string Id,
    string Name,
    string RawValue,
    int? NormalizedValue = null,
    int? WorstValue = null,
    int? Threshold = null);

public enum HealthStatus
{
    Unknown = 0,
    Good,
    Caution,
    Bad
}
