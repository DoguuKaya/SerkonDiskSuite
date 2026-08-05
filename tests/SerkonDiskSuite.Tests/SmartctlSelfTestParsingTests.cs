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
    public void ParseSelfTestStatus_MissingField_ReturnsExplicitNotReportedMessage()
    {
        using var doc = JsonDocument.Parse("""{ "device": { "type": "nvme" } }""");

        var status = SmartctlSmartProvider.ParseSelfTestStatus(doc.RootElement);

        Assert.False(status.IsRunning);
        Assert.Null(status.PercentRemaining);
        Assert.Equal("Bu disk self-test durumu raporlamıyor.", status.StatusDescription);
        Assert.Null(status.Passed);
    }

    // ---- NVMe: gerçek bir KINGSTON SNV2S1000G üzerinde `smartctl -a --json=c` ile
    // yakalanan `nvme_self_test_log` şeması (madde 32, PROGRESS.md'de belgelendi). ----

    [Fact]
    public void ParseSelfTestStatus_Nvme_NotRunningWithHistory_ReturnsLastResult()
    {
        using var doc = JsonDocument.Parse("""
            { "nvme_self_test_log": {
                "nsid": -1,
                "current_self_test_operation": { "value": 0, "string": "No self-test in progress" },
                "table": [
                    { "self_test_code": { "value": 1, "string": "Short" },
                      "self_test_result": { "value": 0, "string": "Completed without error" },
                      "power_on_hours": 1069 }
                ]
            } }
            """);

        var status = SmartctlSmartProvider.ParseSelfTestStatus(doc.RootElement);

        Assert.False(status.IsRunning);
        Assert.Null(status.PercentRemaining);
        Assert.Equal("Short: Completed without error", status.StatusDescription);
        Assert.True(status.Passed);
    }

    [Fact]
    public void ParseSelfTestStatus_Nvme_CurrentOperationRunning_ReturnsRunningWithoutPercent()
    {
        using var doc = JsonDocument.Parse("""
            { "nvme_self_test_log": {
                "nsid": -1,
                "current_self_test_operation": { "value": 1, "string": "Short self-test in progress" },
                "table": []
            } }
            """);

        var status = SmartctlSmartProvider.ParseSelfTestStatus(doc.RootElement);

        Assert.True(status.IsRunning);
        // NVMe'de çalışırkenki kalan yüzde alan adı doğrulanamadı; tahmin edilmedi, null kalır.
        Assert.Null(status.PercentRemaining);
        Assert.Equal("Short self-test in progress", status.StatusDescription);
        Assert.Null(status.Passed);
    }

    [Fact]
    public void ParseSelfTestStatus_Nvme_NoHistoryYet_ReturnsExplicitNoRecordMessage()
    {
        using var doc = JsonDocument.Parse("""
            { "nvme_self_test_log": {
                "nsid": -1,
                "current_self_test_operation": { "value": 0, "string": "No self-test in progress" },
                "table": []
            } }
            """);

        var status = SmartctlSmartProvider.ParseSelfTestStatus(doc.RootElement);

        Assert.False(status.IsRunning);
        Assert.Equal("Bu disk için self-test kaydı yok.", status.StatusDescription);
        Assert.Null(status.Passed);
    }
}
