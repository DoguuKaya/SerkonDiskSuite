namespace SerkonDiskSuite.Core.Models;

/// <summary>Bir diskin trend loglamasında saklanan tek bir zaman damgalı SMART örneği.</summary>
public sealed record SmartTrendPoint(DateTimeOffset Timestamp, int? TemperatureCelsius, int? RemainingLifePercent = null);
