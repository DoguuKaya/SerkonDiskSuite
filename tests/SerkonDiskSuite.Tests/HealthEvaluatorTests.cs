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
    public void NullMetrics_DefaultsToGood()
    {
        var result = HealthEvaluator.Evaluate(null, null, false);
        Assert.Equal(HealthStatus.Good, result);
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
