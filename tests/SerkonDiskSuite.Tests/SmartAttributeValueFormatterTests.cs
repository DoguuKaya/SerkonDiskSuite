using SerkonDiskSuite.Core.Formatting;
using SerkonDiskSuite.Core.Models;
using Xunit;

namespace SerkonDiskSuite.Tests;

public class SmartAttributeValueFormatterTests
{
    [Fact]
    public void FormatDisplayValue_DataUnitsRead_ConvertsToBytesThenFormats()
    {
        // 17.226.562 x (1000 x 512 bayt) = 8.819.999.744.000 bayt ~= 8,02 TB.
        var attr = new SmartAttribute("-", "data_units_read", "17226562");

        Assert.Equal("8,02 TB", SmartAttributeValueFormatter.FormatDisplayValue(attr));
    }

    [Fact]
    public void FormatDisplayValue_PowerOnHours_FormatsWithTurkishThousandsAndSuffix()
    {
        var attr = new SmartAttribute("-", "power_on_hours", "8962");

        Assert.Equal("8.962 saat", SmartAttributeValueFormatter.FormatDisplayValue(attr));
    }

    [Theory]
    [InlineData("percentage_used", "99")]
    [InlineData("available_spare", "100")]
    public void FormatDisplayValue_Percentages_FormatsWithPercentSign(string name, string raw)
    {
        var attr = new SmartAttribute("-", name, raw);

        Assert.Equal($"%{raw}", SmartAttributeValueFormatter.FormatDisplayValue(attr));
    }

    [Fact]
    public void FormatDisplayValue_UnknownAttributeName_ReturnsRawValueUnchanged()
    {
        var attr = new SmartAttribute("190", "Airflow_Temperature_Cel_Unknown_Variant", "42");

        Assert.Equal("42", SmartAttributeValueFormatter.FormatDisplayValue(attr));
    }

    [Fact]
    public void FormatDisplayValue_NonNumericCompositeRawValue_ReturnsUnchanged()
    {
        // Bazı ATA öznitelikleri (ör. Spin_Up_Time) bileşik "değer (alt değerler)" biçiminde gelir.
        var attr = new SmartAttribute("3", "Spin_Up_Time", "1725 (17 245 0)");

        Assert.Equal("1725 (17 245 0)", SmartAttributeValueFormatter.FormatDisplayValue(attr));
    }
}
