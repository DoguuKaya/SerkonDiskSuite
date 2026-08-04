using SerkonDiskSuite.Core.Models;
using SerkonDiskSuite.Infrastructure.Benchmark;
using Xunit;

namespace SerkonDiskSuite.Tests;

/// <summary>
/// Gerçek dosya I/O'su üzerinden çalışan entegrasyon testleri (temp dizinde küçük dosyalarla).
/// Gerçek disk hızına bağlı olmadıkları için verim/IOPS değerlerinin BÜYÜKLÜĞÜNÜ değil,
/// motorun her yapılandırmada (Q1T1 eski davranış + yeni yüksek Q/T eşzamanlılığı) hatasız
/// tamamlandığını ve sonuçları doğru etiketlediğini doğrular.
/// </summary>
public class DiskBenchmarkRunnerTests : IDisposable
{
    private readonly string _targetDir;

    public DiskBenchmarkRunnerTests()
    {
        _targetDir = Path.Combine(Path.GetTempPath(), $"serkonbench_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_targetDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_targetDir, recursive: true); }
        catch { /* test temizliği başarısız olsa da testi etkilemesin */ }
    }

    private BenchmarkOptions SmallOptions(int queueDepth = 1, int threadCount = 1) => new()
    {
        TargetPath = _targetDir,
        TestFileSizeBytes = 256 * 1024,
        Passes = 1,
        SequentialBlockSize = 64 * 1024,
        RandomBlockSize = 4 * 1024,
        QueueDepth = queueDepth,
        ThreadCount = threadCount,
    };

    [Fact]
    public async Task RunAsync_DefaultQ1T1_ProducesResultForEachTestKind()
    {
        var runner = new DiskBenchmarkRunner();

        var results = await runner.RunAsync(SmallOptions());

        Assert.Equal(4, results.Count);
        Assert.All(results, r => Assert.True(r.ThroughputMBps > 0));
        Assert.All(results, r => Assert.Equal(1, r.QueueDepth));
        Assert.All(results, r => Assert.Equal(1, r.ThreadCount));

        var sequentialWrite = Assert.Single(results, r => r.Kind == BenchmarkTestKind.SequentialWrite);
        var randomRead = Assert.Single(results, r => r.Kind == BenchmarkTestKind.RandomRead);
        Assert.Null(sequentialWrite.Iops);
        Assert.NotNull(randomRead.Iops);
    }

    [Theory]
    [InlineData(4, 1)]
    [InlineData(1, 4)]
    [InlineData(4, 2)]
    public async Task RunAsync_HigherQueueDepthOrThreadCount_CompletesAndTagsResults(int queueDepth, int threadCount)
    {
        var runner = new DiskBenchmarkRunner();

        var results = await runner.RunAsync(SmallOptions(queueDepth, threadCount));

        Assert.Equal(4, results.Count);
        Assert.All(results, r => Assert.True(r.ThroughputMBps > 0));
        Assert.All(results, r => Assert.Equal(queueDepth, r.QueueDepth));
        Assert.All(results, r => Assert.Equal(threadCount, r.ThreadCount));
    }

    [Fact]
    public async Task RunAsync_CanBeCancelled()
    {
        var runner = new DiskBenchmarkRunner();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runner.RunAsync(SmallOptions(), progress: null, cts.Token));
    }
}
