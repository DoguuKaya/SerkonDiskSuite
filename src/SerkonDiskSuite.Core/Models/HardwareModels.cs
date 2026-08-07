namespace SerkonDiskSuite.Core.Models;

/// <summary>Bir donanım sensöründen tek bir ham okuma (ör. anakart fan hızı, CPU voltajı).
/// Sağlayıcı katmanının belirli alanlara ayrıştırmadan önceki ara temsili.</summary>
public sealed record HardwareSensorReading(string Label, double Value, string Unit, HardwareSensorCategory Category);

public enum HardwareSensorCategory
{
    Load,
    Temperature,
    Clock,
    Power,
    Voltage,
    Data,
}

/// <summary>CPU/GPU/RAM'in belirli bir andaki anlık donanım okuması. Bu makinede/donanımda
/// okunamayan alanlar null olur — tahmini değer üretilmez (ör. entegre GPU'larda ayrı bir
/// sıcaklık sensörü genelde yoktur, CPU sıcaklığı ve anakart sensörleri yönetici hakkı
/// gerektirir).</summary>
public sealed class HardwareSnapshot
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    /// <summary>CPU paket sıcaklığı (santigrat).</summary>
    public double? CpuTemperatureCelsius { get; init; }

    /// <summary>Toplam CPU kullanım yüzdesi (0-100).</summary>
    public double? CpuLoadPercent { get; init; }

    /// <summary>GPU adı (ör. "Intel(R) UHD Graphics 770"). GPU bulunamazsa null.</summary>
    public string? GpuName { get; init; }

    /// <summary>GPU sıcaklığı (santigrat). Entegre GPU'larda genelde ayrı bir sensör olmadığından null olabilir.</summary>
    public double? GpuTemperatureCelsius { get; init; }

    /// <summary>GPU 3D motor kullanım yüzdesi (0-100).</summary>
    public double? GpuLoadPercent { get; init; }

    /// <summary>Kullanılan GPU belleği (bayt). Entegre GPU'larda paylaşılan sistem belleğinden gelir.</summary>
    public long? GpuMemoryUsedBytes { get; init; }

    /// <summary>Kullanılan fiziksel RAM (bayt).</summary>
    public long? RamUsedBytes { get; init; }

    /// <summary>Toplam fiziksel RAM (bayt).</summary>
    public long? RamTotalBytes { get; init; }
}
