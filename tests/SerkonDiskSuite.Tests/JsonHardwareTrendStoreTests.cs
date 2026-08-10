using SerkonDiskSuite.Core.Models;
using SerkonDiskSuite.Infrastructure.Trend;
using Xunit;

namespace SerkonDiskSuite.Tests;

/// <summary>
/// Gerçek dosya I/O'su üzerinden çalışan entegrasyon testleri (temp dizinde) —
/// JsonSmartTrendStoreTests'in aynı süreçler-arası eşzamanlılık senaryosunu,
/// tek dosyalı JsonHardwareTrendStore için doğrular.
/// </summary>
public class JsonHardwareTrendStoreTests : IDisposable
{
    private readonly string _dir;

    public JsonHardwareTrendStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"serkonhwtrend_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { /* test temizliği başarısız olsa da testi etkilemesin */ }
    }

    [Fact]
    public async Task AppendAsync_ConcurrentFromMultipleStoreInstances_LosesNoPoints()
    {
        var storeA = new JsonHardwareTrendStore(_dir);
        var storeB = new JsonHardwareTrendStore(_dir);
        const int countPerStore = 40;

        var tasks = new List<Task>();
        for (int i = 0; i < countPerStore; i++)
        {
            var timestamp = DateTimeOffset.Now.AddSeconds(i);
            tasks.Add(storeA.AppendAsync(new HardwareTrendPoint(timestamp, i, i, null, null)));
            tasks.Add(storeB.AppendAsync(new HardwareTrendPoint(timestamp, 1000 + i, 1000 + i, null, null)));
        }

        await Task.WhenAll(tasks);

        var result = await storeA.LoadAsync();
        Assert.Equal(countPerStore * 2, result.Count);
    }

    [Fact]
    public async Task AppendAsync_ThenLoadAsync_ReturnsAppendedPoint()
    {
        var store = new JsonHardwareTrendStore(_dir);
        var point = new HardwareTrendPoint(DateTimeOffset.Now, 62.5, 34.2, null, null);

        await store.AppendAsync(point);
        var result = await store.LoadAsync();

        var loaded = Assert.Single(result);
        Assert.Equal(62.5, loaded.CpuTemperatureCelsius);
        Assert.Equal(34.2, loaded.CpuLoadPercent);
        Assert.Null(loaded.GpuTemperatureCelsius);
    }

    [Fact]
    public async Task LoadAsync_WhenFileDoesNotExist_ReturnsEmpty()
    {
        var store = new JsonHardwareTrendStore(_dir);

        var result = await store.LoadAsync();

        Assert.Empty(result);
    }
}
