namespace SerkonDiskSuite.Core.Models;

/// <summary>Hazır bir benchmark profili (ör. CrystalDiskMark'ın NVMe varsayılan satırları).</summary>
public sealed record BenchmarkProfile(string Name, bool IsRandom, int BlockSize, int QueueDepth, int ThreadCount);

/// <summary>
/// CrystalDiskMark'ın NVMe varsayılan profilleri. Bir profil seçildiğinde ilgili blok boyutu
/// (sıralı veya rastgele) ve kuyruk derinliği/iş parçacığı sayısı <see cref="BenchmarkOptions"/>'a
/// uygulanır; diğer kategorinin blok boyutu değişmez.
/// </summary>
public static class BenchmarkProfiles
{
    /// <summary>"Özel" (manuel ayarlar) seçeneği — profil ComboBox'ında varsayılan olarak
    /// seçili görünür; <see cref="Apply"/>'a hiç verilmemeli (bir no-op'tur, sadece UI'da
    /// ComboBox'ın boş başlamasını önlemek için var).</summary>
    public static readonly BenchmarkProfile Custom = new("Özel", IsRandom: false, BlockSize: 0, QueueDepth: 0, ThreadCount: 0);

    public static readonly BenchmarkProfile Seq1MQ8T1 = new("SEQ1M Q8T1", IsRandom: false, BlockSize: 1024 * 1024, QueueDepth: 8, ThreadCount: 1);
    public static readonly BenchmarkProfile Seq1MQ1T1 = new("SEQ1M Q1T1", IsRandom: false, BlockSize: 1024 * 1024, QueueDepth: 1, ThreadCount: 1);
    public static readonly BenchmarkProfile Rnd4KQ32T16 = new("RND4K Q32T16", IsRandom: true, BlockSize: 4 * 1024, QueueDepth: 32, ThreadCount: 16);
    public static readonly BenchmarkProfile Rnd4KQ1T1 = new("RND4K Q1T1", IsRandom: true, BlockSize: 4 * 1024, QueueDepth: 1, ThreadCount: 1);

    public static IReadOnlyList<BenchmarkProfile> All { get; } = [Seq1MQ8T1, Seq1MQ1T1, Rnd4KQ32T16, Rnd4KQ1T1];

    /// <summary>Profili options'a uygular: rastgele profil yalnızca rastgele blok boyutu/Q/T'sini,
    /// sıralı profil yalnızca sıralı blok boyutu/Q/T'sini değiştirir — diğer kategori (ve onun
    /// Q/T'si) hiç dokunulmadan kalır (bkz. madde C1: gerçek CrystalDiskMark'ta "SEQ1M Q8T1"
    /// yalnızca sıralı testleri, "RND4K Q32T16" yalnızca rastgele testleri etkiler).
    /// ProfileName her durumda güncellenir.</summary>
    public static BenchmarkOptions Apply(BenchmarkOptions options, BenchmarkProfile profile) => profile.IsRandom
        ? options with
        {
            RandomBlockSize = profile.BlockSize,
            RandomQueueDepth = profile.QueueDepth,
            RandomThreadCount = profile.ThreadCount,
            ProfileName = profile.Name,
        }
        : options with
        {
            SequentialBlockSize = profile.BlockSize,
            SequentialQueueDepth = profile.QueueDepth,
            SequentialThreadCount = profile.ThreadCount,
            ProfileName = profile.Name,
        };
}
