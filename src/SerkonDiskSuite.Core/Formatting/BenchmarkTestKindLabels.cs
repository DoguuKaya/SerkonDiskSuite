using SerkonDiskSuite.Core.Models;

namespace SerkonDiskSuite.Core.Formatting;

/// <summary>Benchmark test türü (enum) -> Türkçe görünen ad. Enum değerleri değişmez.</summary>
public static class BenchmarkTestKindLabels
{
    public static string ToTurkish(BenchmarkTestKind kind) => kind switch
    {
        BenchmarkTestKind.SequentialRead => "Sıralı Okuma",
        BenchmarkTestKind.SequentialWrite => "Sıralı Yazma",
        BenchmarkTestKind.RandomRead => "Rastgele Okuma",
        BenchmarkTestKind.RandomWrite => "Rastgele Yazma",
        _ => kind.ToString()
    };
}
