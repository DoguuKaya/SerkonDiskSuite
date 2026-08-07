using SerkonDiskSuite.Infrastructure.Hardware;
using Xunit;

namespace SerkonDiskSuite.Tests;

/// <summary>
/// Gerçek donanım üzerinden çalışan entegrasyon testleri (mock değil) — CPU yükü ve RAM
/// bu geliştirme makinesinde yönetici hakkı olmadan bile her zaman okunabildiğinden
/// (ADIM 1'in probu, PROGRESS.md madde 41) bunları doğrular. CPU/GPU sıcaklığı gibi
/// yönetici hakkı gerektiren alanlar makineye/izne göre değişebileceğinden yalnızca
/// "aralık içinde veya null" şeklinde gevşek doğrulanır.
/// </summary>
public class LibreHardwareMonitorProviderTests
{
    [Fact]
    public async Task GetSnapshotAsync_ReturnsCpuLoadAndRam_WithoutAdminRights()
    {
        using var provider = new LibreHardwareMonitorProvider();

        var snapshot = await provider.GetSnapshotAsync();

        Assert.True(snapshot.CpuLoadPercent is >= 0 and <= 100, "CPU Total yük sensörü bu makinede yönetici hakkı olmadan da veri döndürmeli.");
        Assert.True(snapshot.RamUsedBytes > 0, "Total Memory 'Memory Used' sensörü pozitif bir değer döndürmeli.");
        Assert.True(snapshot.RamTotalBytes > 0, "Total Memory 'Memory Available' + 'Memory Used' toplamı pozitif olmalı.");
        Assert.True(snapshot.RamUsedBytes <= snapshot.RamTotalBytes, "Kullanılan RAM, toplam RAM'i aşamaz.");
    }

    [Fact]
    public async Task GetSnapshotAsync_OptionalSensors_AreNullOrWithinPhysicalRange()
    {
        using var provider = new LibreHardwareMonitorProvider();

        var snapshot = await provider.GetSnapshotAsync();

        Assert.True(snapshot.CpuTemperatureCelsius is null or (>= 0 and <= 130));
        Assert.True(snapshot.GpuTemperatureCelsius is null or (>= 0 and <= 130));
        Assert.True(snapshot.GpuLoadPercent is null or (>= 0 and <= 100));
        Assert.True(snapshot.GpuMemoryUsedBytes is null or > 0);
    }

    [Fact]
    public async Task GetSnapshotAsync_CalledTwice_DoesNotThrow()
    {
        using var provider = new LibreHardwareMonitorProvider();

        await provider.GetSnapshotAsync();
        var second = await provider.GetSnapshotAsync();

        Assert.NotNull(second);
    }

    [Fact]
    public async Task GetSnapshotAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var provider = new LibreHardwareMonitorProvider();
        provider.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => provider.GetSnapshotAsync());
    }
}
