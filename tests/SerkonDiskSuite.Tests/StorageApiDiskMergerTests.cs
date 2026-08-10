using SerkonDiskSuite.Core.Models;
using SerkonDiskSuite.Infrastructure.Wmi;
using Xunit;

namespace SerkonDiskSuite.Tests;

/// <summary>
/// SORUN 2 (v1.0.0 gerçek kullanıcı raporu): bazı makinelerde ana/boot disk
/// Win32_DiskDrive taramasında hiç görünmüyordu. Bu testler, Win32_DiskDrive'ın
/// eksik bıraktığı diski Storage Management API (MSFT_PhysicalDisk) üzerinden
/// tamamlayan saf birleştirme mantığını (WMI'sız) kanıtlar. Gerçek donanımda
/// (bu makinede tek disk var) çoklu disk/eksik disk senaryosu üretilemediği için
/// StorageApiDiskMerger.MapBusType değerleri gerçek WMI sorgusuyla doğrulanan
/// (BusType=17 -> Nvme) ve resmi Microsoft STORAGE_BUS_TYPE belgesinden alınan
/// sabit değerlerle test edilir.
/// </summary>
public class StorageApiDiskMergerTests
{
    private static StorageApiDiskMerger.StorageApiPhysicalDisk MakeCandidate(
        int index, string name = "Disk", long size = 1_000_000_000_000, int busType = 17) =>
        new(index, name, $"SN-{index}", size, busType);

    [Fact]
    public void FindMissingDisks_BootDiskMissingFromWin32_IsRecovered()
    {
        // SORUN 2'nin tam senaryosu: Win32_DiskDrive yalnızca ikincil (index 1) diski
        // buluyor, ana/boot disk (index 0) Storage API'de var ama Win32'de yok.
        var win32Indexes = new[] { 1 };
        var storageDisks = new[]
        {
            MakeCandidate(0, "KINGSTON SNV2S1000G (boot)", 1_000_204_886_016, busType: 17),
            MakeCandidate(1, "WD Green SN350", 1_000_000_000_000, busType: 17),
        };

        var missing = StorageApiDiskMerger.FindMissingDisks(win32Indexes, storageDisks);

        Assert.Single(missing);
        Assert.Equal(@"\\.\PHYSICALDRIVE0", missing[0].DevicePath);
        Assert.Equal("KINGSTON SNV2S1000G (boot)", missing[0].ModelName);
        Assert.Equal(DiskBusType.Nvme, missing[0].BusType);
    }

    [Fact]
    public void FindMissingDisks_AllDisksPresentInWin32_ReturnsEmpty()
    {
        var win32Indexes = new[] { 0, 1, 2 };
        var storageDisks = new[] { MakeCandidate(0), MakeCandidate(1), MakeCandidate(2) };

        var missing = StorageApiDiskMerger.FindMissingDisks(win32Indexes, storageDisks);

        Assert.Empty(missing);
    }

    [Fact]
    public void FindMissingDisks_NoStorageApiDisks_ReturnsEmpty()
    {
        var missing = StorageApiDiskMerger.FindMissingDisks(
            win32Indexes: [0],
            storageApiDisks: []);

        Assert.Empty(missing);
    }

    [Theory]
    [InlineData(17, DiskBusType.Nvme)]
    [InlineData(11, DiskBusType.Sata)]
    [InlineData(7, DiskBusType.Usb)]
    [InlineData(10, DiskBusType.Sas)]
    [InlineData(1, DiskBusType.Scsi)]
    [InlineData(999, DiskBusType.Unknown)]
    public void FindMissingDisks_MapsStorageBusTypeCorrectly(int rawBusType, DiskBusType expected)
    {
        var missing = StorageApiDiskMerger.FindMissingDisks(
            win32Indexes: [],
            storageApiDisks: [MakeCandidate(0, busType: rawBusType)]);

        Assert.Equal(expected, missing[0].BusType);
    }

    [Fact]
    public void FindMissingDisks_MissingFriendlyName_FallsBackToUnknownLabel()
    {
        var missing = StorageApiDiskMerger.FindMissingDisks(
            win32Indexes: [],
            storageApiDisks: [MakeCandidate(0, name: "")]);

        Assert.Equal("Bilinmiyor", missing[0].ModelName);
    }
}
