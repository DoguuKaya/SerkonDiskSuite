using SerkonDiskSuite.Core.Models;
using SerkonDiskSuite.Infrastructure.SystemInfo;
using Xunit;

namespace SerkonDiskSuite.Tests;

/// <summary>Gerçek WMI üzerinden çalışan entegrasyon testi (mock değil) — bu makinede
/// (32 GB, 2x16 GB DDR4-3200 olduğu PowerShell Get-CimInstance ile elle doğrulanmıştı)
/// RamModules'ın gerçekten dolduğunu doğrular.</summary>
public class WmiSystemInfoProviderTests
{
    [Fact]
    public async Task GetSummaryAsync_ReturnsPlausibleRamModules()
    {
        var provider = new WmiSystemInfoProvider();

        var summary = await provider.GetSummaryAsync();

        // Not: Bu test CI'da (GitHub-hosted runner, farklı/sanal donanım) da çalışacağından
        // makineye özgü sabit değerler (ör. bu geliştirme makinesindeki "2x16 GB DDR4-3200")
        // İDDİA EDİLMİYOR — LibreHardwareMonitorProviderTests'teki desenle tutarlı olarak
        // yalnızca yapısal/mantıklı aralık kontrolleri yapılıyor.
        Assert.NotEmpty(summary.RamModules);
        foreach (RamModuleInfo module in summary.RamModules)
        {
            Assert.True(module.CapacityBytes > 0, "Her modülün kapasitesi pozitif olmalı.");
            Assert.True(module.SpeedMHz is null or > 0, "Hız biliniyorsa pozitif olmalı.");
        }

        // Win32_ComputerSystem.TotalPhysicalMemory (OS'e görünen) ile Win32_PhysicalMemory
        // modüllerinin ham kapasite toplamı BİREBİR EŞİT OLMAK ZORUNDA DEĞİL — donanım/UEFI/GPU
        // için ayrılan bellek bölgeleri nedeniyle OS'e görünen değer birkaç yüz MB daha az
        // olabilir (bu makinede gerçek fark ~326 MB, 32 GB kurulu / 31,7 GB kullanılabilir).
        // Bu yüzden yalnızca "aynı büyüklük mertebesinde" olduğu doğrulanıyor.
        long totalFromModules = summary.RamModules.Sum(m => m.CapacityBytes);
        double ratio = (double)summary.TotalMemoryBytes / totalFromModules;
        Assert.InRange(ratio, 0.9, 1.0);
    }
}
