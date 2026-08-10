namespace SerkonDiskSuite.Core.Models;

/// <summary>CPU/GPU trend loglamasında saklanan tek bir zaman damgalı donanım örneği.
/// Disklerin aksine makinede tek bir CPU/GPU olduğundan (SmartTrendPoint'teki disk anahtarının
/// karşılığı yok) tüm noktalar tek bir dosyada saklanır.</summary>
public sealed record HardwareTrendPoint(
    DateTimeOffset Timestamp,
    double? CpuTemperatureCelsius,
    double? CpuLoadPercent,
    double? GpuTemperatureCelsius,
    double? GpuLoadPercent);
