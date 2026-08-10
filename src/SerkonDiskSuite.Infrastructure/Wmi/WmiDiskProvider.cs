using System.Management;
using System.Runtime.Versioning;
using SerkonDiskSuite.Core.Interfaces;
using SerkonDiskSuite.Core.Models;

namespace SerkonDiskSuite.Infrastructure.Wmi;

/// <summary>
/// Fiziksel diskleri Windows WMI (MSFT_PhysicalDisk + Win32_DiskDrive) üzerinden keşfeder.
/// Sürücü harflerini Win32_DiskDrive -> Win32_DiskPartition -> Win32_LogicalDisk
/// ilişki zinciriyle bulur.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WmiDiskProvider : IDiskProvider
{
    public Task<IReadOnlyList<DiskInfo>> GetDisksAsync(CancellationToken ct = default)
        => Task.Run<IReadOnlyList<DiskInfo>>(async () =>
        {
            var disks = new List<DiskInfo>();
            var win32Indexes = new List<int>();

            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_DiskDrive");

            foreach (ManagementObject drive in searcher.Get())
            {
                ct.ThrowIfCancellationRequested();

                string deviceId = drive["DeviceID"]?.ToString() ?? "";  // \\.\PHYSICALDRIVE1
                string model = drive["Model"]?.ToString()?.Trim() ?? "Bilinmiyor";
                string serial = drive["SerialNumber"]?.ToString()?.Trim() ?? "";
                string firmware = drive["FirmwareRevision"]?.ToString()?.Trim() ?? "";
                long size = drive["Size"] is not null ? Convert.ToInt64(drive["Size"]) : 0;
                string interfaceType = drive["InterfaceType"]?.ToString() ?? "";
                string pnpDeviceId = drive["PNPDeviceID"]?.ToString() ?? "";
                if (drive["Index"] is not null)
                    win32Indexes.Add(Convert.ToInt32(drive["Index"]));

                var letters = GetDriveLetters(deviceId, ct);
                var busType = MapBusType(interfaceType, model, pnpDeviceId);

                // PCIe link bilgisi yalnızca NVMe disklerinde anlamlıdır; SATA/USB için
                // ayrı bir aktarım hızı kavramı WMI'dan güvenilir okunamaz.
                string? transferMode = busType == DiskBusType.Nvme
                    ? await PcieLinkInfoReader.TryReadAsync(pnpDeviceId, ct)
                    : null;

                disks.Add(new DiskInfo
                {
                    DevicePath = deviceId,
                    ModelName = model,
                    SerialNumber = serial,
                    FirmwareVersion = firmware,
                    CapacityBytes = size,
                    BusType = busType,
                    DriveLetters = letters,
                    TransferMode = transferMode,
                    // RotationRate WMI'da güvenilir değil; SMART katmanından zenginleştirilebilir.
                });
            }

            // SORUN 2 (v1.0.0 gerçek kullanıcı raporu): Win32_DiskDrive bazı makinelerde
            // ana/boot diski hiç döndürmüyor (bkz. StorageApiDiskMerger belgesi). Storage
            // Management API (MSFT_PhysicalDisk) ikincil kaynak olarak kontrol edilip
            // eksik kalan diskler tamamlanıyor. Bu ad alanı çok eski/özel sistemlerde
            // bulunmayabilir; sorgu başarısız olursa mevcut Win32_DiskDrive sonucu
            // olduğu gibi döner (yeni kaynak zaten var olan davranışı bozmaz).
            try
            {
                foreach (var (index, recovered) in FindDisksMissingFromWin32(win32Indexes, ct))
                {
                    // GetDriveLetters (ASSOCIATORS OF Win32_DiskDrive...) burada işe yaramaz:
                    // bu disk tanım gereği Win32_DiskDrive'da yok. Storage Management API'nin
                    // kendi MSFT_Partition.DriveLetter alanı (DiskNumber ile anahtarlanır)
                    // kullanılıyor — bu makinede doğrulandı (partition 3 -> "C").
                    var letters = GetDriveLettersFromStorageApi(index, ct);
                    disks.Add(letters.Count == 0 ? recovered : new DiskInfo
                    {
                        DevicePath = recovered.DevicePath,
                        ModelName = recovered.ModelName,
                        SerialNumber = recovered.SerialNumber,
                        FirmwareVersion = recovered.FirmwareVersion,
                        CapacityBytes = recovered.CapacityBytes,
                        BusType = recovered.BusType,
                        DriveLetters = letters,
                        TransferMode = recovered.TransferMode,
                    });
                }
            }
            catch (ManagementException)
            {
                // Storage Management API ad alanı bu sistemde yok/erişilemiyor —
                // Win32_DiskDrive sonucu zaten döndürülecek, sessizce yutulur.
            }

            return disks;
        }, ct);

    private static IReadOnlyList<(int Index, DiskInfo Disk)> FindDisksMissingFromWin32(
        IReadOnlyCollection<int> win32Indexes, CancellationToken ct)
    {
        var storageDisks = new List<StorageApiDiskMerger.StorageApiPhysicalDisk>();

        using var storageSearcher = new ManagementObjectSearcher(
            @"root\Microsoft\Windows\Storage", "SELECT * FROM MSFT_PhysicalDisk");

        foreach (ManagementObject disk in storageSearcher.Get())
        {
            ct.ThrowIfCancellationRequested();

            if (disk["DeviceId"] is not { } rawId || !int.TryParse(rawId.ToString(), out int index))
                continue;

            storageDisks.Add(new StorageApiDiskMerger.StorageApiPhysicalDisk(
                Index: index,
                FriendlyName: disk["FriendlyName"]?.ToString()?.Trim() ?? "",
                SerialNumber: disk["SerialNumber"]?.ToString()?.Trim() ?? "",
                SizeBytes: disk["Size"] is not null ? Convert.ToInt64(disk["Size"]) : 0,
                BusType: disk["BusType"] is not null ? Convert.ToInt32(disk["BusType"]) : 0));
        }

        return StorageApiDiskMerger.FindMissingDisks(win32Indexes, storageDisks)
            .Select(d => (int.Parse(d.DevicePath[@"\\.\PHYSICALDRIVE".Length..]), d))
            .ToList();
    }

    /// <summary>
    /// MSFT_Partition.DriveLetter, DiskNumber ile anahtarlanır ve Win32_DiskDrive'a hiç
    /// ihtiyaç duymaz — bu makinede doğrulandı (`MSFT_Partition` DiskNumber=0,
    /// PartitionNumber=3 -> DriveLetter='C'). Win32_DiskDrive'da bulunamayan (bu yüzden
    /// StorageApiDiskMerger ile eklenen) diskler için sürücü harfi bulmanın tek yolu bu —
    /// `GetDriveLetters`'daki ASSOCIATORS OF Win32_DiskDrive... sorgusu, tanım gereği
    /// Win32_DiskDrive'da eksik olan bir disk için hiçbir sonuç döndürmez.
    /// </summary>
    private static List<string> GetDriveLettersFromStorageApi(int diskIndex, CancellationToken ct)
    {
        var letters = new List<string>();

        using var partitionSearcher = new ManagementObjectSearcher(
            @"root\Microsoft\Windows\Storage",
            $"SELECT * FROM MSFT_Partition WHERE DiskNumber={diskIndex}");

        foreach (ManagementObject partition in partitionSearcher.Get())
        {
            ct.ThrowIfCancellationRequested();
            // DriveLetter, System.Management üzerinden CLR System.Char olarak gelir; harf
            // atanmamış partisyonlarda ' ' (boşluk) döner — canlı WMI sorgusuyla doğrulandı.
            string? letter = partition["DriveLetter"]?.ToString();
            if (!string.IsNullOrWhiteSpace(letter))
                letters.Add(letter + ":");
        }

        return letters;
    }

    private static List<string> GetDriveLetters(string diskId, CancellationToken ct)
    {
        var letters = new List<string>();
        // DeviceID (ör. \\.\PHYSICALDRIVE0) WQL sorgusuna olduğu gibi geçirilmeli;
        // ters eğik çizgileri kaçışlamak WBEM_E_NOT_FOUND ("Not found") hatasına yol açar.

        // DiskDrive -> Partition
        using var partSearcher = new ManagementObjectSearcher(
            $"ASSOCIATORS OF {{Win32_DiskDrive.DeviceID='{diskId}'}} " +
            "WHERE AssocClass=Win32_DiskDriveToDiskPartition");

        foreach (ManagementObject partition in partSearcher.Get())
        {
            ct.ThrowIfCancellationRequested();
            string partId = partition["DeviceID"]?.ToString() ?? "";

            // Partition -> LogicalDisk
            using var logicalSearcher = new ManagementObjectSearcher(
                $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partId}'}} " +
                "WHERE AssocClass=Win32_LogicalDiskToPartition");

            foreach (ManagementObject logical in logicalSearcher.Get())
            {
                string? letter = logical["DeviceID"]?.ToString(); // "C:"
                if (!string.IsNullOrEmpty(letter))
                    letters.Add(letter);
            }
        }

        return letters;
    }

    private static DiskBusType MapBusType(string interfaceType, string model, string pnpDeviceId)
    {
        // InterfaceType NVMe disklerde genelde "SCSI" döner (Windows depolama yığını NVMe'yi
        // bir SCSI miniport soyutlamasıyla sunar); model adı da çoğu gerçek üründe "NVMe"
        // geçmez (ör. "KINGSTON SNV2S1000G"). Güvenilir işaret, PNPDeviceID içindeki
        // "VEN_NVME" belirtecidir (ör. "SCSI\DISK&VEN_NVME&PROD_...").
        if (model.Contains("NVMe", StringComparison.OrdinalIgnoreCase)
            || pnpDeviceId.Contains("VEN_NVME", StringComparison.OrdinalIgnoreCase))
            return DiskBusType.Nvme;

        return interfaceType.ToUpperInvariant() switch
        {
            "USB" => DiskBusType.Usb,
            "SATA" or "IDE" => DiskBusType.Sata,
            "SAS" => DiskBusType.Sas,
            "SCSI" => DiskBusType.Scsi,
            _ => DiskBusType.Unknown
        };
    }
}
