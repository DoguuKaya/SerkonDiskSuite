namespace SerkonDiskSuite.Core.Models;

/// <summary>
/// Bir fiziksel diski (SSD/HDD/NVMe) temsil eden temel model.
/// Donanımdan bağımsızdır: hangi kaynaktan (smartctl, WMI vb.) doldurulduğu önemsizdir.
/// </summary>
public sealed class DiskInfo
{
    /// <summary>İşletim sistemi cihaz yolu (ör. \\.\PhysicalDrive1 veya /dev/nvme1).</summary>
    public required string DevicePath { get; init; }

    /// <summary>Model adı (ör. "Samsung SSD 990 EVO 2TB").</summary>
    public string ModelName { get; init; } = "Bilinmiyor";

    /// <summary>Seri numarası.</summary>
    public string SerialNumber { get; init; } = string.Empty;

    /// <summary>Firmware sürümü.</summary>
    public string FirmwareVersion { get; init; } = string.Empty;

    /// <summary>Toplam kapasite (bayt).</summary>
    public long CapacityBytes { get; init; }

    /// <summary>Arayüz tipi (NVMe, SATA, USB).</summary>
    public DiskBusType BusType { get; init; } = DiskBusType.Unknown;

    /// <summary>Protokol/aktarım modu (ör. "PCIe 4.0 x4"). NVMe için anlamlıdır.</summary>
    public string? TransferMode { get; init; }

    /// <summary>Dönme hızı (RPM). SSD ise 0 / null.</summary>
    public int? RotationRate { get; init; }

    /// <summary>Bu diskin bağlı olduğu sürücü harfleri (ör. ["C:", "Q:"]).</summary>
    public IReadOnlyList<string> DriveLetters { get; init; } = [];

    /// <summary>Kapasitenin okunabilir gösterimi (ör. "2.00 TB").</summary>
    public string CapacityDisplay => FormatBytes(CapacityBytes);

    public bool IsSolidState => RotationRate is null or 0;

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:0.##} {units[unit]}";
    }
}

public enum DiskBusType
{
    Unknown = 0,
    Nvme,
    Sata,
    Usb,
    Sas,
    Scsi
}
