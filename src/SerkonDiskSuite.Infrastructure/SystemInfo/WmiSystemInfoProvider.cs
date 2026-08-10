using System.Management;
using System.Runtime.Versioning;
using SerkonDiskSuite.Core.Interfaces;
using SerkonDiskSuite.Core.Models;

namespace SerkonDiskSuite.Infrastructure.SystemInfo;

/// <summary>Sistem özetini WMI üzerinden toplar (CPU, anakart, BIOS, RAM, OS).</summary>
[SupportedOSPlatform("windows")]
public sealed class WmiSystemInfoProvider : ISystemInfoProvider
{
    public Task<SystemSummary> GetSummaryAsync(CancellationToken ct = default)
        => Task.Run(() =>
        {
            return new SystemSummary
            {
                OsName = QuerySingle("Win32_OperatingSystem", "Caption"),
                CpuName = QuerySingle("Win32_Processor", "Name"),
                MotherboardName = $"{QuerySingle("Win32_BaseBoard", "Manufacturer")} {QuerySingle("Win32_BaseBoard", "Product")}".Trim(),
                BiosVersion = QuerySingle("Win32_BIOS", "SMBIOSBIOSVersion"),
                TotalMemoryBytes = QueryMemory(),
                RamModules = QueryRamModules(),
            };
        }, ct);

    private static string QuerySingle(string wmiClass, string property)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT {property} FROM {wmiClass}");
            foreach (ManagementObject obj in searcher.Get())
                return obj[property]?.ToString()?.Trim() ?? "";
        }
        catch { /* WMI hatasında boş dön */ }
        return "";
    }

    private static long QueryMemory()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
            foreach (ManagementObject obj in searcher.Get())
                return Convert.ToInt64(obj["TotalPhysicalMemory"]);
        }
        catch { /* yut */ }
        return 0;
    }

    /// <summary>Her fiziksel RAM modülünün kapasite/hız/tip/slot bilgisini okur. "Speed"
    /// alanı WMI belgelerinde "nanoseconds" olarak işaretli olsa da gerçek makinede
    /// "ConfiguredClockSpeed" ile birebir aynı (MHz) değeri döndürdüğü doğrulandı; bu
    /// yüzden birimi belgelerde açıkça "megahertz" olan ConfiguredClockSpeed kullanılıyor.</summary>
    private static IReadOnlyList<RamModuleInfo> QueryRamModules()
    {
        var modules = new List<RamModuleInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Capacity, ConfiguredClockSpeed, SMBIOSMemoryType, DeviceLocator FROM Win32_PhysicalMemory");
            foreach (ManagementObject obj in searcher.Get())
            {
                long capacity = obj["Capacity"] is { } c ? Convert.ToInt64(c) : 0;
                int? speedMHz = obj["ConfiguredClockSpeed"] is { } s && Convert.ToInt32(s) > 0
                    ? Convert.ToInt32(s)
                    : null;
                int smbiosType = obj["SMBIOSMemoryType"] is { } t ? Convert.ToInt32(t) : 0;
                string? slot = obj["DeviceLocator"]?.ToString();

                modules.Add(new RamModuleInfo(capacity, speedMHz, MapRamType(smbiosType), slot));
            }
        }
        catch { /* WMI hatasında boş liste dön, tahmini değer üretme */ }
        return modules;
    }

    /// <summary>Ham SMBIOSMemoryType kodunu RamType'a çevirir. Kodlar SMBIOS
    /// spesifikasyonundan doğrulandı: 24=DDR3, 26=DDR4, 34=DDR5 (bkz. RamModuleInfo.cs).
    /// Not: WMI'nin kendi "MemoryType" alanı (CIM-eşlemeli, ayrı bir alan) bu makinede
    /// gerçek DDR4 donanımında bile 0 (Unknown) döndürdüğü doğrulandı — bu yüzden yalnızca
    /// ham SMBIOSMemoryType kullanılıyor.</summary>
    private static RamType MapRamType(int smbiosMemoryType) => smbiosMemoryType switch
    {
        24 => RamType.Ddr3,
        26 => RamType.Ddr4,
        34 => RamType.Ddr5,
        _ => RamType.Unknown,
    };
}
