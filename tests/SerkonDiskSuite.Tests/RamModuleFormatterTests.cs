using SerkonDiskSuite.Core.Formatting;
using SerkonDiskSuite.Core.Models;
using Xunit;

namespace SerkonDiskSuite.Tests;

public class RamModuleFormatterTests
{
    private const long SixteenGb = 16L * 1024 * 1024 * 1024;
    private const long EightGb = 8L * 1024 * 1024 * 1024;

    [Fact]
    public void FormatSummary_EmptyList_ReturnsNull()
    {
        Assert.Null(RamModuleFormatter.FormatSummary([]));
    }

    [Fact]
    public void FormatSummary_TwoIdenticalModules_ReturnsCombinedSummary()
    {
        var modules = new[]
        {
            new RamModuleInfo(SixteenGb, 3200, RamType.Ddr4, "DIMM1"),
            new RamModuleInfo(SixteenGb, 3200, RamType.Ddr4, "DIMM2"),
        };

        var result = RamModuleFormatter.FormatSummary(modules);

        Assert.Equal("2x16 GB, DDR4-3200", result);
    }

    [Fact]
    public void FormatSummary_MixedCapacityAndType_ListsEachModuleWithSlot()
    {
        var modules = new[]
        {
            new RamModuleInfo(SixteenGb, 3200, RamType.Ddr4, "DIMM1"),
            new RamModuleInfo(EightGb, 2666, RamType.Ddr4, "DIMM2"),
        };

        var result = RamModuleFormatter.FormatSummary(modules);

        Assert.Equal(
            $"DIMM1: 16 GB, DDR4-3200{Environment.NewLine}DIMM2: 8 GB, DDR4-2666",
            result);
    }

    [Fact]
    public void FormatSummary_UnknownTypeWithSpeed_FallsBackToMHz()
    {
        var modules = new[] { new RamModuleInfo(SixteenGb, 2400, RamType.Unknown, "DIMM1") };

        var result = RamModuleFormatter.FormatSummary(modules);

        Assert.Equal("1x16 GB, 2400 MHz", result);
    }

    [Fact]
    public void FormatSummary_UnknownTypeAndSpeed_OnlyShowsCapacity()
    {
        var modules = new[] { new RamModuleInfo(SixteenGb, null, RamType.Unknown, null) };

        var result = RamModuleFormatter.FormatSummary(modules);

        Assert.Equal("1x16 GB", result);
    }

    [Fact]
    public void FormatSummary_SingleModuleWithoutSlot_NoSlotPrefixInMixedCase()
    {
        var modules = new[]
        {
            new RamModuleInfo(SixteenGb, 3200, RamType.Ddr4, null),
            new RamModuleInfo(EightGb, 3200, RamType.Ddr4, null),
        };

        var result = RamModuleFormatter.FormatSummary(modules);

        Assert.Equal($"16 GB, DDR4-3200{Environment.NewLine}8 GB, DDR4-3200", result);
    }
}
