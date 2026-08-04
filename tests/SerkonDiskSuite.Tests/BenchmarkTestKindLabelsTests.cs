using SerkonDiskSuite.Core.Formatting;
using SerkonDiskSuite.Core.Models;
using Xunit;

namespace SerkonDiskSuite.Tests;

public class BenchmarkTestKindLabelsTests
{
    [Theory]
    [InlineData(BenchmarkTestKind.SequentialRead, "Sıralı Okuma")]
    [InlineData(BenchmarkTestKind.SequentialWrite, "Sıralı Yazma")]
    [InlineData(BenchmarkTestKind.RandomRead, "Rastgele Okuma")]
    [InlineData(BenchmarkTestKind.RandomWrite, "Rastgele Yazma")]
    public void ToTurkish_MapsEveryKnownKind(BenchmarkTestKind kind, string expected)
    {
        Assert.Equal(expected, BenchmarkTestKindLabels.ToTurkish(kind));
    }
}
