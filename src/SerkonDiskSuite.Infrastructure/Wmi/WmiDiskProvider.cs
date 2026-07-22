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
        => Task.Run<IReadOnlyList<DiskInfo>>(() =>
        {
            var disks = new List<DiskInfo>();

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

                var letters = GetDriveLetters(drive, ct);
                var busType = MapBusType(interfaceType, model);

                disks.Add(new DiskInfo
                {
                    DevicePath = deviceId,
                    ModelName = model,
                    SerialNumber = serial,
                    FirmwareVersion = firmware,
                    CapacityBytes = size,
                    BusType = busType,
                    DriveLetters = letters,
                    // RotationRate ve TransferMode WMI'da güvenilir değil;
                    // bunlar SMART katmanından zenginleştirilebilir.
                });
            }

            return disks;
        }, ct);

    private static List<string> GetDriveLetters(ManagementObject drive, CancellationToken ct)
    {
        var letters = new List<string>();
        // DeviceID (ör. \\.\PHYSICALDRIVE0) WQL sorgusuna olduğu gibi geçirilmeli;
        // ters eğik çizgileri kaçışlamak WBEM_E_NOT_FOUND ("Not found") hatasına yol açar.
        string diskId = drive["DeviceID"]?.ToString() ?? "";

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

    private static DiskBusType MapBusType(string interfaceType, string model)
    {
        // WptType bazen "SCSI" der; model adından NVMe çıkarımı destekler.
        if (model.Contains("NVMe", StringComparison.OrdinalIgnoreCase))
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
