using SerkonDiskSuite.Core.Models;
using SerkonDiskSuite.Infrastructure.Trend;
using Xunit;

namespace SerkonDiskSuite.Tests;

/// <summary>
/// Gerçek dosya I/O'su üzerinden çalışan entegrasyon testleri (temp dizinde).
/// Asıl amaç madde A2'de bulunan hatayı yeniden üretmek ve düzeltmeyi doğrulamak: birden
/// fazla <see cref="JsonSmartTrendStore"/> örneği (gerçek uygulamada birden fazla
/// SerkonDiskSuite.exe süreci gibi) AYNI dosyaya eşzamanlı <c>AppendAsync</c> çağırdığında,
/// eski in-process kilit süreçler arası korumayı sağlamadığından "lost update" oluşuyor ve
/// geçmişin büyük kısmı sessizce siliniyordu.
/// </summary>
public class JsonSmartTrendStoreTests : IDisposable
{
    private readonly string _dir;

    public JsonSmartTrendStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"serkontrend_test_{Guid.NewGuid():N}");
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
        // Arrange: gerçek uygulamada iki ayrı süreç aynı dosyaya yazar; burada aynı klasörü
        // paylaşan iki ayrı JsonSmartTrendStore örneğiyle bu taklit ediliyor.
        var storeA = new JsonSmartTrendStore(_dir);
        var storeB = new JsonSmartTrendStore(_dir);
        const string diskKey = "TESTSERIAL123";
        const int countPerStore = 40;

        var tasks = new List<Task>();
        for (int i = 0; i < countPerStore; i++)
        {
            var timestamp = DateTimeOffset.Now.AddSeconds(i);
            tasks.Add(storeA.AppendAsync(diskKey, new SmartTrendPoint(timestamp, TemperatureCelsius: i)));
            tasks.Add(storeB.AppendAsync(diskKey, new SmartTrendPoint(timestamp, TemperatureCelsius: 1000 + i)));
        }

        // Act
        await Task.WhenAll(tasks);

        // Assert: iki taraftan gelen TÜM noktalar (toplam 80) korunmalı, hiçbiri diğerinin
        // yazımı tarafından sessizce ezilmemeli.
        var result = await storeA.LoadAsync(diskKey);
        Assert.Equal(countPerStore * 2, result.Count);
    }

    [Fact]
    public async Task AppendAsync_ThenLoadAsync_ReturnsAppendedPoint()
    {
        var store = new JsonSmartTrendStore(_dir);
        var point = new SmartTrendPoint(DateTimeOffset.Now, TemperatureCelsius: 42, RemainingLifePercent: 90);

        await store.AppendAsync("DISK1", point);
        var result = await store.LoadAsync("DISK1");

        var loaded = Assert.Single(result);
        Assert.Equal(42, loaded.TemperatureCelsius);
        Assert.Equal(90, loaded.RemainingLifePercent);
    }

    [Fact]
    public async Task LoadAsync_WhenFileDoesNotExist_ReturnsEmpty()
    {
        var store = new JsonSmartTrendStore(_dir);

        var result = await store.LoadAsync("NEVER_WRITTEN");

        Assert.Empty(result);
    }
}
