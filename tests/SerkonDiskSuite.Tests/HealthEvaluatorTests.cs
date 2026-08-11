using SerkonDiskSuite.Core.Models;
using SerkonDiskSuite.Core.Services;
using Xunit;

namespace SerkonDiskSuite.Tests;

public class HealthEvaluatorTests
{
    [Fact]
    public void CriticalWarning_AlwaysBad()
    {
        var result = HealthEvaluator.Evaluate(temperatureCelsius: 30, remainingLifePercent: 100, hasCriticalWarning: true);
        Assert.Equal(HealthStatus.Bad, result);
    }

    [Theory]
    [InlineData(30, HealthStatus.Good)]
    [InlineData(60, HealthStatus.Caution)]
    [InlineData(65, HealthStatus.Caution)]
    [InlineData(70, HealthStatus.Bad)]
    [InlineData(85, HealthStatus.Bad)]
    public void Temperature_MapsToExpectedStatus(int temp, HealthStatus expected)
    {
        var result = HealthEvaluator.Evaluate(temp, remainingLifePercent: 100, hasCriticalWarning: false);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(100, HealthStatus.Good)]
    [InlineData(20, HealthStatus.Caution)]
    [InlineData(10, HealthStatus.Caution)]
    [InlineData(5, HealthStatus.Bad)]
    [InlineData(1, HealthStatus.Bad)]
    public void RemainingLife_MapsToExpectedStatus(int life, HealthStatus expected)
    {
        var result = HealthEvaluator.Evaluate(temperatureCelsius: 30, life, hasCriticalWarning: false);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void NullMetrics_ReturnsUnknown_NotGood()
    {
        // SORUN 5 (v1.0.0 gerçek kullanıcı raporu): veri hiç yokken "Good" iddia etmek
        // yanıltıcıydı ("Durum: İyi" yazarken tüm kartlar boştu). Artık Unknown döner.
        var result = HealthEvaluator.Evaluate(null, null, false);
        Assert.Equal(HealthStatus.Unknown, result);
    }

    [Fact]
    public void OnlyTemperatureKnown_StillEvaluatesNormally()
    {
        // Kalan ömür bilinmiyor olsa da sıcaklık tek başına yeterli bir sinyaldir —
        // Unknown'a düşülmemeli (yalnızca İKİSİ DE null olduğunda düşülür).
        var result = HealthEvaluator.Evaluate(temperatureCelsius: 30, remainingLifePercent: null, hasCriticalWarning: false);
        Assert.Equal(HealthStatus.Good, result);
    }

    [Fact]
    public void OnlyRemainingLifeKnown_StillEvaluatesNormally()
    {
        var result = HealthEvaluator.Evaluate(temperatureCelsius: null, remainingLifePercent: 100, hasCriticalWarning: false);
        Assert.Equal(HealthStatus.Good, result);
    }

    [Fact]
    public void NullMetrics_WithCriticalWarning_StillBad()
    {
        // Kritik uyarı bayrağı en yüksek önceliğe sahip — veri eksikliği bunu geçersiz kılmaz.
        var result = HealthEvaluator.Evaluate(null, null, hasCriticalWarning: true);
        Assert.Equal(HealthStatus.Bad, result);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(15, 85)]
    [InlineData(100, 0)]
    [InlineData(120, 0)] // %100'ü aşan kullanım 0'a sabitlenir
    public void PercentageUsed_ConvertsToRemainingLife(int used, int expectedRemaining)
    {
        Assert.Equal(expectedRemaining, HealthEvaluator.PercentageUsedToRemainingLife(used));
    }
}
