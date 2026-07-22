using System.Management;
using System.Runtime.Versioning;
using SerkonDiskSuite.Core.Interfaces;

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
                TotalMemoryBytes = QueryMemory()
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
}
