using System.Management;
using System.Runtime.Versioning;
using SerkonDiskSuite.Core.Interfaces;

namespace SerkonDiskSuite.Infrastructure.Hardware;

/// <summary>
/// Windows'un Sanallaştırma Tabanlı Güvenlik (VBS) / Bellek Bütünlüğü (Memory Integrity, HVCI)
/// özelliğinin çalışıp çalışmadığını WMI üzerinden tespit eder. Bu özellik etkinken,
/// LibreHardwareMonitor'ün kullandığı imzasız WinRing0 çekirdek sürücüsü MSR (sıcaklık/güç)
/// kayıtlarına erişemez — bu, uygulamanın kodunda düzeltilemeyen, Windows'un kasıtlı bir güvenlik
/// sınırıdır (bkz. https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/issues/566,
/// 2021'den beri açık, "help wanted" — bir katılımcının gözlemi: "memory integrity"yi kapatmak
/// sorunu çözüyor, imzalı olmayan sürücülerin MSR erişimi VBS/HVCI tarafından engelleniyor).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WmiVbsStatusProvider : IVbsStatusProvider
{
    /// <summary>Bellek Bütünlüğü (HVCI) şu anda ÇALIŞIYOR mu? Tespit edilemezse null döner
    /// (ör. WMI sınıfı bu Windows sürümünde yoksa) — tahmini değer üretilmez.</summary>
    public Task<bool?> IsMemoryIntegrityRunningAsync(CancellationToken ct = default)
        => Task.Run(() =>
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    @"root\Microsoft\Windows\DeviceGuard",
                    "SELECT SecurityServicesRunning FROM Win32_DeviceGuard");
                foreach (ManagementObject obj in searcher.Get())
                {
                    if (obj["SecurityServicesRunning"] is ushort[] runningServices)
                    {
                        // 2 = Hypervisor-protected Code Integrity (HVCI) / "Bellek Bütünlüğü".
                        // Bkz. Microsoft belgeleri: Win32_DeviceGuard.SecurityServicesRunning.
                        return runningServices.Contains((ushort)2);
                    }
                }
                return false;
            }
            catch
            {
                return (bool?)null;
            }
        }, ct);
}
