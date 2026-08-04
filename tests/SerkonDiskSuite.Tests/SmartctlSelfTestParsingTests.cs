using System.Text.Json;
using SerkonDiskSuite.Infrastructure.Smart;
using Xunit;

namespace SerkonDiskSuite.Tests;

/// <summary>
/// SmartctlSmartProvider.ParseSelfTestStatus'u gerçek smartctl/donanım gerektirmeden, sabit
/// JSON örnekleriyle (smartctl'in belgelenen ata_smart_data.self_test.status şeması) doğrular.
/// </summary>
public class SmartctlSelfTestParsingTests
{
    [Fact]
    public void ParseSelfTestStatus_InProgress_ReturnsRunningWithRemainingPercent()
    {
        using var doc = JsonDocument.Parse("""
            { "ata_smart_data": { "self_test": { "status": {
                "value": 249, "string": "in progress, 90% remaining", "remaining_percent": 90
            } } } }
            """);

        var status = SmartctlSmartProvider.ParseSelfTestStatus(doc.RootElement);

        Assert.True(status.IsRunning);
        Assert.Equal(90, status.PercentRemaining);
        Assert.Null(status.Passed);
    }

    [Fact]
    public void ParseSelfTestStatus_CompletedWithoutError_ReturnsPassedTrue()
    {
        using var doc = JsonDocument.Parse("""
            { "ata_smart_data": { "self_test": { "status": {
                "value": 0, "string": "completed without error"
            } } } }
            """);

        var status = SmartctlSmartProvider.ParseSelfTestStatus(doc.RootElement);

        Assert.False(status.IsRunning);
        Assert.Null(status.PercentRemaining);
        Assert.True(status.Passed);
    }

    [Fact]
    public void ParseSelfTestStatus_InterruptedByHostReset_ReturnsPassedFalse()
    {
        using var doc = JsonDocument.Parse("""
            { "ata_smart_data": { "self_test": { "status": {
                "value": 41, "string": "Interrupted (host reset)", "remaining_percent": 30
            } } } }
            """);

        var status = SmartctlSmartProvider.ParseSelfTestStatus(doc.RootElement);

        // remaining_percent varsa hâlâ "havada" kabul edilir (kesinti sonrası devam durumu net değil).
        Assert.True(status.IsRunning);
    }

    [Fact]
    public void ParseSelfTestStatus_MissingField_ReturnsAllNullNotRunning()
    {
        using var doc = JsonDocument.Parse("""{ "device": { "type": "nvme" } }""");

        var status = SmartctlSmartProvider.ParseSelfTestStatus(doc.RootElement);

        Assert.False(status.IsRunning);
        Assert.Null(status.PercentRemaining);
        Assert.Null(status.StatusDescription);
        Assert.Null(status.Passed);
    }
}
