using SerkonDiskSuite.Core.Models;
using Xunit;

namespace SerkonDiskSuite.Tests;

public class BenchmarkProfilesTests
{
    private static BenchmarkOptions BaseOptions() => new()
    {
        TargetPath = "S:\\",
        SequentialBlockSize = 999,
        RandomBlockSize = 111,
        SequentialQueueDepth = 2,
        SequentialThreadCount = 3,
        RandomQueueDepth = 5,
        RandomThreadCount = 7,
    };

    [Fact]
    public void All_ContainsExactlyTheFourCrystalDiskMarkDefaults()
    {
        var names = BenchmarkProfiles.All.Select(p => p.Name).ToArray();
        Assert.Equal(["SEQ1M Q8T1", "SEQ1M Q1T1", "RND4K Q32T16", "RND4K Q1T1"], names);
    }

    [Fact]
    public void Apply_SequentialProfile_ChangesOnlySequentialBlockSizeAndQueueDepth()
    {
        var result = BenchmarkProfiles.Apply(BaseOptions(), BenchmarkProfiles.Seq1MQ8T1);

        Assert.Equal(1024 * 1024, result.SequentialBlockSize);
        Assert.Equal(8, result.SequentialQueueDepth);
        Assert.Equal(1, result.SequentialThreadCount);
        Assert.Equal("SEQ1M Q8T1", result.ProfileName);

        // Madde C1: rastgele kategoriye hiç dokunulmamalı.
        Assert.Equal(111, result.RandomBlockSize);
        Assert.Equal(5, result.RandomQueueDepth);
        Assert.Equal(7, result.RandomThreadCount);
    }

    [Fact]
    public void Apply_RandomProfile_ChangesOnlyRandomBlockSizeAndQueueDepth()
    {
        var result = BenchmarkProfiles.Apply(BaseOptions(), BenchmarkProfiles.Rnd4KQ32T16);

        Assert.Equal(4 * 1024, result.RandomBlockSize);
        Assert.Equal(32, result.RandomQueueDepth);
        Assert.Equal(16, result.RandomThreadCount);
        Assert.Equal("RND4K Q32T16", result.ProfileName);

        // Madde C1: sıralı kategoriye hiç dokunulmamalı.
        Assert.Equal(999, result.SequentialBlockSize);
        Assert.Equal(2, result.SequentialQueueDepth);
        Assert.Equal(3, result.SequentialThreadCount);
    }

    [Fact]
    public void Apply_DoesNotMutateOriginalOptions()
    {
        var original = BaseOptions();
        _ = BenchmarkProfiles.Apply(original, BenchmarkProfiles.Seq1MQ1T1);

        Assert.Equal(999, original.SequentialBlockSize);
        Assert.Equal(2, original.SequentialQueueDepth);
        Assert.Equal("Özel", original.ProfileName);
    }
}
