using SerkonDiskSuite.Core.Formatting;
using Xunit;

namespace SerkonDiskSuite.Tests;

public class SmartAttributeLabelsTests
{
    [Theory]
    [InlineData("percentage_used", "Kullanım Yüzdesi")]
    [InlineData("data_units_read", "Okunan Veri Birimi")]
    [InlineData("nsid", "Ad Alanı No")]
    [InlineData("PERCENTAGE_USED", "Kullanım Yüzdesi")] // eşleşme büyük/küçük harf duyarsız
    [InlineData("Power_On_Hours", "Çalışma Süresi")] // ATA adı, NVMe anahtarıyla aynı etikete düşer
    public void GetDisplayName_KnownRawName_ReturnsTurkishLabel(string rawName, string expected)
    {
        Assert.Equal(expected, SmartAttributeLabels.GetDisplayName(rawName));
    }

    [Theory]
    [InlineData("some_unknown_field", "Some Unknown Field")]
    [InlineData("Unmapped_Attribute", "Unmapped Attribute")]
    public void GetDisplayName_UnknownRawName_FallsBackToPrettifiedVersion(string rawName, string expected)
    {
        Assert.Equal(expected, SmartAttributeLabels.GetDisplayName(rawName));
    }

    [Fact]
    public void GetDisplayName_NullOrWhitespace_ReturnsInputUnchanged()
    {
        Assert.Equal("", SmartAttributeLabels.GetDisplayName(""));
        Assert.Equal("   ", SmartAttributeLabels.GetDisplayName("   "));
    }
}
