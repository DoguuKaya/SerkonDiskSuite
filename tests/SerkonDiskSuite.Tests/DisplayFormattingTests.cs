using SerkonDiskSuite.Core.Formatting;
using Xunit;

namespace SerkonDiskSuite.Tests;

public class DisplayFormattingTests
{
    [Theory]
    [InlineData(0L, "0,00 B")]
    [InlineData(1024L, "1,00 KB")]
    [InlineData(9_820_000_000_000L, "8,93 TB")]
    public void FormatBytes_ProducesTurkishDecimalComma(long bytes, string expected)
    {
        Assert.Equal(expected, DisplayFormatting.FormatBytes(bytes));
    }

    [Theory]
    [InlineData(0L, "0 saat")]
    [InlineData(8962L, "8.962 saat")]
    [InlineData(1000000L, "1.000.000 saat")]
    public void FormatHours_UsesTurkishThousandsSeparator(long hours, string expected)
    {
        Assert.Equal(expected, DisplayFormatting.FormatHours(hours));
    }

    [Theory]
    [InlineData(0L, "0")]
    [InlineData(12345L, "12.345")]
    public void FormatCount_UsesTurkishThousandsSeparator(long value, string expected)
    {
        Assert.Equal(expected, DisplayFormatting.FormatCount(value));
    }

    [Theory]
    [InlineData(0d, "0")]
    [InlineData(1771d, "1.771")]
    [InlineData(25806d, "25.806")]
    [InlineData(1234567d, "1.234.567")]
    public void FormatNumber_UsesTurkishThousandsSeparator_NotEnUsComma(double value, string expected)
    {
        Assert.Equal(expected, DisplayFormatting.FormatNumber(value));
    }

    [Theory]
    [InlineData(1234.5, 1, "1.234,5")]
    [InlineData(0.1234, 2, "0,12")]
    public void FormatNumber_WithDecimals_UsesTurkishDecimalComma(double value, int decimals, string expected)
    {
        Assert.Equal(expected, DisplayFormatting.FormatNumber(value, decimals));
    }
}
