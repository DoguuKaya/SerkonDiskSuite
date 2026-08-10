using SerkonDiskSuite.Core.Models;

namespace SerkonDiskSuite.Infrastructure.Wmi;

/// <summary>
/// SORUN 2 (v1.0.0 gerçek kullanıcı raporu): bazı makinelerde ana/boot disk
/// `Win32_DiskDrive` taramasında hiç görünmüyordu (NVMe/RAID/Intel RST gibi
/// yapılandırmalarda bu WMI sınıfının bazı diskleri atladığı, resmi Microsoft
/// belgelerinde doğrudan belgelenmiyor ama depolama topluluğunda bilinen bir
/// güvenilirlik sınırı). Microsoft'un daha yeni Storage Management API'si
/// (`root\Microsoft\Windows\Storage` ad alanındaki `MSFT_PhysicalDisk`) ikincil/
/// tamamlayıcı bir kaynak olarak kullanılıp `Win32_DiskDrive`'ın eksik bıraktığı
/// diskler bulunuyor. `Win32_DiskDrive.Index` ile `MSFT_PhysicalDisk.DeviceId`
/// aynı disk numarasını taşır (bu makinede doğrulandı: ikisi de tek diskte "0").
///
/// Bu sınıf WMI'dan tamamen bağımsız, saf birleştirme mantığını içerir —
/// çoklu disk/eksik disk senaryosu bu makinede gerçek donanımla üretilemediği
/// için (tek fiziksel disk var) doğrulama birim testiyle yapılır
/// (<see cref="StorageApiDiskMergerTests"/>).
/// </summary>
public static class StorageApiDiskMerger
{
    /// <summary>MSFT_PhysicalDisk'ten okunan ham alanlar (WMI tipine bağımlı değil).</summary>
    public readonly record struct StorageApiPhysicalDisk(
        int Index,
        string FriendlyName,
        string SerialNumber,
        long SizeBytes,
        int BusType);

    /// <summary>
    /// `win32Indexes` (Win32_DiskDrive'dan bulunan disk numaraları) içinde olmayan ama
    /// `storageApiDisks` (MSFT_PhysicalDisk) içinde bulunan diskleri <see cref="DiskInfo"/>
    /// olarak döndürür. Her iki kaynakta da bulunan diskler için boş liste döner
    /// (o diskler zaten Win32_DiskDrive'dan doğru şekilde oluşturulmuştur).
    /// </summary>
    public static IReadOnlyList<DiskInfo> FindMissingDisks(
        IReadOnlyCollection<int> win32Indexes,
        IReadOnlyList<StorageApiPhysicalDisk> storageApiDisks)
    {
        var missing = new List<DiskInfo>();
        foreach (var candidate in storageApiDisks)
        {
            if (win32Indexes.Contains(candidate.Index))
                continue;

            missing.Add(new DiskInfo
            {
                DevicePath = $@"\\.\PHYSICALDRIVE{candidate.Index}",
                ModelName = string.IsNullOrWhiteSpace(candidate.FriendlyName) ? "Bilinmiyor" : candidate.FriendlyName,
                SerialNumber = candidate.SerialNumber ?? "",
                CapacityBytes = candidate.SizeBytes,
                BusType = MapBusType(candidate.BusType),
            });
        }
        return missing;
    }

    /// <summary>
    /// MSFT_PhysicalDisk.BusType, Windows sürücü belgelerindeki STORAGE_BUS_TYPE enum'ının
    /// (ntddstor.h) ham sayısal değeridir (learn.microsoft.com/.../ne-ntddstor-storage_bus_type
    /// ile doğrulandı, 0-tabanlı: Unknown=0, Scsi=1, ..., Sas=10, Sata=11, ..., Nvme=17).
    /// Bu makinede gerçek NVMe diskte BusType=17 döndüğü ayrıca canlı WMI sorgusuyla
    /// doğrulandı.
    /// </summary>
    private static DiskBusType MapBusType(int storageBusType) => storageBusType switch
    {
        1 => DiskBusType.Scsi,
        7 => DiskBusType.Usb,
        10 => DiskBusType.Sas,
        11 => DiskBusType.Sata,
        17 => DiskBusType.Nvme,
        _ => DiskBusType.Unknown
    };
}
