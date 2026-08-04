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
    };

    [Fact]
    public void All_ContainsExactlyTheFourCrystalDiskMarkDefaults()
    {
        var names = BenchmarkProfiles.All.Select(p => p.Name).ToArray();
        Assert.Equal(["SEQ1M Q8T1", "SEQ1M Q1T1", "RND4K Q32T16", "RND4K Q1T1"], names);
    }

    [Fact]
    public void Apply_SequentialProfile_ChangesOnlySequentialBlockSize()
    {
        var result = BenchmarkProfiles.Apply(BaseOptions(), BenchmarkProfiles.Seq1MQ8T1);

        Assert.Equal(1024 * 1024, result.SequentialBlockSize);
        Assert.Equal(111, result.RandomBlockSize); // dokunulmadı
        Assert.Equal(8, result.QueueDepth);
        Assert.Equal(1, result.ThreadCount);
        Assert.Equal("SEQ1M Q8T1", result.ProfileName);
    }

    [Fact]
    public void Apply_RandomProfile_ChangesOnlyRandomBlockSize()
    {
        var result = BenchmarkProfiles.Apply(BaseOptions(), BenchmarkProfiles.Rnd4KQ32T16);

        Assert.Equal(4 * 1024, result.RandomBlockSize);
        Assert.Equal(999, result.SequentialBlockSize); // dokunulmadı
        Assert.Equal(32, result.QueueDepth);
        Assert.Equal(16, result.ThreadCount);
        Assert.Equal("RND4K Q32T16", result.ProfileName);
    }

    [Fact]
    public void Apply_DoesNotMutateOriginalOptions()
    {
        var original = BaseOptions();
        _ = BenchmarkProfiles.Apply(original, BenchmarkProfiles.Seq1MQ1T1);

        Assert.Equal(999, original.SequentialBlockSize);
        Assert.Equal("Özel", original.ProfileName);
    }
}
