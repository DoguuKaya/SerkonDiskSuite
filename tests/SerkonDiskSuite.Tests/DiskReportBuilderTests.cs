using SerkonDiskSuite.Core.Models;
using SerkonDiskSuite.Core.Reporting;
using Xunit;

namespace SerkonDiskSuite.Tests;

public class DiskReportBuilderTests
{
    private static DiskInfo SampleDisk() => new()
    {
        DevicePath = "\\\\.\\PHYSICALDRIVE0",
        ModelName = "Samsung SSD 990 EVO 2TB",
        SerialNumber = "S6Z1NF0R123456",
        FirmwareVersion = "1B2QJXD7",
        CapacityBytes = 2_000_000_000_000L,
        BusType = DiskBusType.Nvme,
        TransferMode = "PCIe 4.0 x4",
        DriveLetters = ["C:"],
    };

    private static SmartHealth SampleHealth() => new()
    {
        DevicePath = "\\\\.\\PHYSICALDRIVE0",
        OverallStatus = HealthStatus.Good,
        TemperatureCelsius = 42,
        RemainingLifePercent = 97,
        CriticalWarningFlags = ["Sıcaklık eşik dışında"],
        Attributes = [new SmartAttribute("-", "power_on_hours", "8962")],
    };

    [Fact]
    public void BuildPlainText_IncludesDiskModelAndSerial()
    {
        string text = DiskReportBuilder.BuildPlainText(SampleDisk(), null, []);

        Assert.Contains("Samsung SSD 990 EVO 2TB", text);
        Assert.Contains("S6Z1NF0R123456", text);
    }

    [Fact]
    public void BuildPlainText_WithHealth_IncludesCriticalWarningAndAttributeLabel()
    {
        string text = DiskReportBuilder.BuildPlainText(SampleDisk(), SampleHealth(), []);

        Assert.Contains("Sıcaklık eşik dışında", text);
        Assert.Contains("Çalışma Süresi", text); // power_on_hours -> Türkçe etiket
    }

    [Fact]
    public void BuildPlainText_WithBenchmarkResults_IncludesTurkishKindAndProfileName()
    {
        BenchmarkResult[] results = [new(BenchmarkTestKind.SequentialRead, 3500.0, null, TimeSpan.FromSeconds(1), 1, 1, "SEQ1M Q1T1")];

        string text = DiskReportBuilder.BuildPlainText(SampleDisk(), null, results);

        Assert.Contains("Sıralı Okuma", text);
        Assert.Contains("SEQ1M Q1T1", text);
    }

    [Fact]
    public void BuildPlainText_NullHealthAndEmptyResults_DoesNotThrow()
    {
        string text = DiskReportBuilder.BuildPlainText(SampleDisk(), null, []);

        Assert.DoesNotContain("SMART Sağlık Bilgisi", text);
        Assert.DoesNotContain("Son Benchmark Sonuçları", text);
    }

    [Fact]
    public void BuildJson_ProducesValidJsonWithDiskAndHealth()
    {
        string json = DiskReportBuilder.BuildJson(SampleDisk(), SampleHealth(), []);

        using var doc = System.Text.Json.JsonDocument.Parse(json); // atarsa test zaten başarısız olur
        Assert.True(doc.RootElement.TryGetProperty("Disk", out _));
        Assert.True(doc.RootElement.TryGetProperty("Health", out _));
        Assert.True(doc.RootElement.TryGetProperty("BenchmarkResults", out _));
    }
}
