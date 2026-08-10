using SerkonDiskSuite.Core.Models;
using SerkonDiskSuite.Infrastructure.Smart;
using Xunit;

namespace SerkonDiskSuite.Tests;

/// <summary>
/// SORUN 1 (v1.0.0 gerçek kullanıcı raporu): çok diskli makinelerde SMART verisi yalnızca
/// bir diskte geliyordu. Kök neden bu makinede GERÇEK donanımla (Kingston SNV2S1000G NVMe)
/// kanıtlandı: WMI `Win32_DiskDrive.SerialNumber` = "0000_0000_0000_0000_0026_B778_58A7_DF35."
/// (20 bayt, alt tire ayraçlı) iken smartctl `serial_number` = "50026B77858A7DF3" — AYNI DİSK,
/// hiçbir normalizasyonla eşleşmeyen tamamen farklı biçimler. `model_name` ise birebir
/// ("KINGSTON SNV2S1000G") ve kapasite ~%99,9997 yakın (WMI 1.000.202.273.280 bayt /
/// smartctl user_capacity.bytes 1.000.204.886.016 bayt) çıktı — bu testler o gerçek
/// senaryoyu ve normalizasyon/model+kapasite düzeltmesini birim testiyle kanıtlar.
/// </summary>
public class SmartctlDiskMatchingTests
{
    private static DiskInfo MakeDisk(string serial, string model, long capacityBytes) => new()
    {
        DevicePath = @"\\.\PhysicalDrive0",
        SerialNumber = serial,
        ModelName = model,
        CapacityBytes = capacityBytes,
    };

    private static string ScanJson(string serial, string model, long capacityBytes) =>
        "{\"device\":{\"name\":\"/dev/sda\",\"type\":\"nvme\"}," +
        $"\"model_name\":\"{model}\",\"serial_number\":\"{serial}\"," +
        $"\"user_capacity\":{{\"bytes\":{capacityBytes}}}}}";

    [Fact]
    public void MatchesDisk_ExactSerialMatch_ReturnsTrue()
    {
        var disk = MakeDisk("ABC123", "Samsung SSD 990 EVO", 2_000_000_000_000);
        string json = ScanJson("ABC123", "Samsung SSD 990 EVO", 2_000_000_000_000);

        Assert.True(SmartctlSmartProvider.MatchesDisk(json, disk));
    }

    [Fact]
    public void MatchesDisk_SerialWithWhitespaceAndSeparatorDifference_StillMatches()
    {
        // WMI tarzı boşluklu/alt tireli seri no, smartctl'in düz halinden farklı biçimlendirilmiş
        // ama normalize edilince (harf/rakam dışını at) aynı kimliği taşıyor.
        var disk = MakeDisk("0000_0000_ABC1_23.", "Samsung SSD 990 EVO", 2_000_000_000_000);
        string json = ScanJson("00000000ABC123", "Samsung SSD 990 EVO", 2_000_000_000_000);

        Assert.True(SmartctlSmartProvider.MatchesDisk(json, disk));
    }

    [Fact]
    public void MatchesDisk_SerialCaseDifference_StillMatches()
    {
        var disk = MakeDisk("abc123def", "WD Green SN350", 1_000_000_000_000);
        string json = ScanJson("ABC123DEF", "WD Green SN350", 1_000_000_000_000);

        Assert.True(SmartctlSmartProvider.MatchesDisk(json, disk));
    }

    [Fact]
    public void MatchesDisk_RealWorldSerialFormatMismatch_FallsBackToModelAndCapacity()
    {
        // Bu makinede gerçek Get-CimInstance/smartctl çıktısından alınan gerçek değerler.
        var disk = MakeDisk("0000_0000_0000_0000_0026_B778_58A7_DF35.", "KINGSTON SNV2S1000G", 1_000_202_273_280);
        string json = ScanJson("50026B77858A7DF3", "KINGSTON SNV2S1000G", 1_000_204_886_016);

        Assert.True(SmartctlSmartProvider.MatchesDisk(json, disk));
    }

    [Fact]
    public void MatchesDisk_DifferentDiskAmongThreeCandidates_NoneMatch()
    {
        // Çok diskli bir makinede --scan'in bulduğu 3 aday; hiçbiri hedef diskle eşleşmemeli.
        var disk = MakeDisk("TARGET-SERIAL-999", "Samsung SSD 990 EVO", 2_000_000_000_000);

        string[] candidates =
        [
            ScanJson("OTHER-SERIAL-111", "WD Green SN350", 1_000_000_000_000),
            ScanJson("OTHER-SERIAL-222", "SanDisk SSD PLUS", 500_000_000_000),
            ScanJson("OTHER-SERIAL-333", "Crucial MX500", 500_000_000_000),
        ];

        Assert.All(candidates, c => Assert.False(SmartctlSmartProvider.MatchesDisk(c, disk)));
    }

    [Fact]
    public void MatchesDisk_SameModelDifferentCapacity_DoesNotMatch()
    {
        // Aynı model adına sahip ama farklı kapasiteli iki disk (ör. 500 GB ve 1 TB aynı seri)
        // birbirine karışmamalı — seri no da eşleşmiyorsa yanlış pozitif üretilmemeli.
        var disk = MakeDisk("SERIAL-A", "Samsung SSD 990 EVO", 500_000_000_000);
        string json = ScanJson("SERIAL-B", "Samsung SSD 990 EVO", 2_000_000_000_000);

        Assert.False(SmartctlSmartProvider.MatchesDisk(json, disk));
    }

    [Fact]
    public void MatchesDisk_ModelSubstringWithVendorPrefixDifference_Matches()
    {
        // WMI bazen modele üretici önekini eklemeyebilir/ekleyebilir; alt dizge örtüşmesi
        // (normalize edilmiş) yeterli kabul edilir.
        var disk = MakeDisk("", "SNV2S1000G", 1_000_204_886_016);
        string json = ScanJson("50026B77858A7DF3", "KINGSTON SNV2S1000G", 1_000_204_886_016);

        Assert.True(SmartctlSmartProvider.MatchesDisk(json, disk));
    }
}
