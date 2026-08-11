using System.Text.Json;
using SerkonDiskSuite.Infrastructure.Smart;
using Xunit;

namespace SerkonDiskSuite.Tests;

/// <summary>
/// SmartctlSmartProvider.ParseHealth'in ATA/SATA dalını, gerçek donanım gerektirmeden sabit
/// JSON örnekleriyle doğrular. Öznitelik adları tahmin edilmedi — smartmontools'un gerçek
/// "drivedb.h" kaynağından (github.com/smartmontools/smartmontools) doğrulandı: "SanDisk SSD
/// PLUS (120|240|480|[12]000) ?GB" desenine uyan "Marvell based SanDisk SSDs" ailesi, ID 201'i
/// "Lifetime_Remaining%" (ham değer = doğrudan yüzde), ID 241/242'yi "Total_Writes_GiB"/
/// "Total_Reads_GiB" (ham değer = doğrudan GiB) olarak adlandırıyor. Bu, SORUN'u bildiren
/// kullanıcının (Berke, SanDisk SSD PLUS 480GB SATA) tam disk ailesi.
/// </summary>
public class SmartctlAtaHealthParsingTests
{
    // Kullanıcının CrystalDiskInfo ekran görüntüsünde bildirdiği gerçek değerlere yakın:
    // "İyi %92", "Okunan Toplam: 20407 GB", "Yazılan Toplam: 11378 GB".
    private const string SanDiskSsdPlusJson = """
        {
          "device": { "name": "/dev/sda", "type": "sat" },
          "model_name": "SanDisk SSD PLUS 480GB",
          "smart_status": { "passed": true },
          "temperature": { "current": 32 },
          "power_on_time": { "hours": 8760 },
          "power_cycle_count": 412,
          "ata_smart_attributes": { "table": [
            { "id": 5,   "name": "Reallocated_Sector_Ct", "value": 100, "worst": 100, "thresh": 0,
              "raw": { "value": 0, "string": "0" } },
            { "id": 9,   "name": "Power_On_Hours", "value": 96, "worst": 96, "thresh": 0,
              "raw": { "value": 8760, "string": "8760" } },
            { "id": 12,  "name": "Power_Cycle_Count", "value": 99, "worst": 99, "thresh": 0,
              "raw": { "value": 412, "string": "412" } },
            { "id": 165, "name": "Total_Write/Erase_Count", "value": 100, "worst": 100, "thresh": 0,
              "raw": { "value": 22015, "string": "22015" } },
            { "id": 199, "name": "SATA_CRC_Error", "value": 100, "worst": 100, "thresh": 0,
              "raw": { "value": 0, "string": "0" } },
            { "id": 201, "name": "Lifetime_Remaining%", "value": 92, "worst": 92, "thresh": 0,
              "raw": { "value": 92, "string": "92" } },
            { "id": 233, "name": "Total_NAND_Writes_GiB", "value": 100, "worst": 100, "thresh": 0,
              "raw": { "value": 8720, "string": "8720" } },
            { "id": 241, "name": "Total_Writes_GiB", "value": 253, "worst": 253, "thresh": 0,
              "raw": { "value": 11378, "string": "11378" } },
            { "id": 242, "name": "Total_Reads_GiB", "value": 253, "worst": 253, "thresh": 0,
              "raw": { "value": 20407, "string": "20407" } }
          ] }
        }
        """;

    [Fact]
    public void ParseHealth_SanDiskSsdPlus_ReturnsRemainingLifeFromLifetimeRemainingAttribute()
    {
        using var doc = JsonDocument.Parse(SanDiskSsdPlusJson);

        var health = SmartctlSmartProvider.ParseHealth(@"\\.\PHYSICALDRIVE0", doc.RootElement);

        Assert.Equal(92, health.RemainingLifePercent);
    }

    [Fact]
    public void ParseHealth_SanDiskSsdPlus_ReturnsTotalBytesWrittenFromGiBAttribute()
    {
        using var doc = JsonDocument.Parse(SanDiskSsdPlusJson);

        var health = SmartctlSmartProvider.ParseHealth(@"\\.\PHYSICALDRIVE0", doc.RootElement);

        Assert.Equal(11378L * 1024 * 1024 * 1024, health.TotalBytesWritten);
    }

    [Fact]
    public void ParseHealth_SanDiskSsdPlus_ReturnsTotalBytesReadFromGiBAttribute()
    {
        using var doc = JsonDocument.Parse(SanDiskSsdPlusJson);

        var health = SmartctlSmartProvider.ParseHealth(@"\\.\PHYSICALDRIVE0", doc.RootElement);

        Assert.Equal(20407L * 1024 * 1024 * 1024, health.TotalBytesRead);
    }

    [Fact]
    public void ParseHealth_AttributeTableMissing_ReturnsNullInsteadOfGuessing()
    {
        using var doc = JsonDocument.Parse("""{ "device": { "type": "sat" }, "smart_status": { "passed": true } }""");

        var health = SmartctlSmartProvider.ParseHealth(@"\\.\PHYSICALDRIVE0", doc.RootElement);

        Assert.Null(health.RemainingLifePercent);
        Assert.Null(health.TotalBytesWritten);
        Assert.Null(health.TotalBytesRead);
    }

    [Fact]
    public void ParseHealth_UnknownAttributeNames_ReturnsNullInsteadOfGuessingById()
    {
        // Aynı ID'ler (165-174 vb.) başka bir üreticide tamamen farklı anlama gelebilir
        // (bkz. SmartctlSmartProvider'daki yorum) — smartctl bu modeli tanımıyorsa
        // (drivedb'de eşleşme yoksa) "name" alanı "Unknown_Attribute" döner ve buradan
        // hiçbir sayı türetilmemeli.
        using var doc = JsonDocument.Parse("""
            { "ata_smart_attributes": { "table": [
                { "id": 201, "name": "Unknown_Attribute", "value": 1, "worst": 1, "thresh": 0,
                  "raw": { "value": 199, "string": "199" } }
            ] } }
            """);

        var health = SmartctlSmartProvider.ParseHealth(@"\\.\PHYSICALDRIVE0", doc.RootElement);

        Assert.Null(health.RemainingLifePercent);
    }

    // ---- Diğer üretici aileleri (aynı drivedb.h kaynağından doğrulandı) — genel isim
    // eşleştirmenin SanDisk'e özel kalmadığını gösterir. ----

    [Theory]
    [InlineData("Percent_Lifetime_Remain")]
    [InlineData("SSD_Life_Left")]
    [InlineData("SSD_Life_Left_Perc")]
    [InlineData("Wear_Leveling_Count")]
    public void ParseHealth_KnownVendorLifeAttributeNames_ReturnRemainingLifeFromNormalizedValue(string name)
    {
        using var doc = JsonDocument.Parse($$"""
            { "ata_smart_attributes": { "table": [
                { "id": 231, "name": "{{name}}", "value": 87, "worst": 87, "thresh": 0,
                  "raw": { "value": 13, "string": "13" } }
            ] } }
            """);

        var health = SmartctlSmartProvider.ParseHealth(@"\\.\PHYSICALDRIVE0", doc.RootElement);

        Assert.Equal(87, health.RemainingLifePercent);
    }

    [Fact]
    public void ParseHealth_MediaWearoutIndicator_IsDeliberatelyNotUsedForRemainingLife()
    {
        // "WD Blue / Red / Green SSDs" ailesinde bu öznitelik hex48 paketli ham veri taşıyor ve
        // gerçek bir kullanıcı raporunda normalize değerin "kalan" değil "kullanılan" yüzdeyi
        // gösterdiği (ters anlam) belgelendi (github.com/v-zhuravlev/zbx-smartctl issue #148).
        // Bu isim bilinçli olarak eşleştirme listesine alınmadı.
        using var doc = JsonDocument.Parse("""
            { "ata_smart_attributes": { "table": [
                { "id": 230, "name": "Media_Wearout_Indicator", "value": 1, "worst": 1, "thresh": 0,
                  "raw": { "value": 1099511627823, "string": "0x010f005a010f" } }
            ] } }
            """);

        var health = SmartctlSmartProvider.ParseHealth(@"\\.\PHYSICALDRIVE0", doc.RootElement);

        Assert.Null(health.RemainingLifePercent);
    }

    [Theory]
    [InlineData("Total_LBAs_Written", 1000000, 1000000L * 512)]
    [InlineData("Host_Writes_32MiB", 500, 500L * 32 * 1024 * 1024)]
    [InlineData("Host_Writes_GiB", 900, 900L * 1024 * 1024 * 1024)]
    [InlineData("Lifetime_Writes_GiB", 1014, 1014L * 1024 * 1024 * 1024)]
    [InlineData("Total_Writes_GiB", 11378, 11378L * 1024 * 1024 * 1024)]
    public void ParseHealth_KnownVendorWrittenAttributeNames_ConvertRawToBytesWithCorrectUnit(
        string name, long raw, long expectedBytes)
    {
        using var doc = JsonDocument.Parse($$"""
            { "ata_smart_attributes": { "table": [
                { "id": 241, "name": "{{name}}", "value": 253, "worst": 253, "thresh": 0,
                  "raw": { "value": {{raw}}, "string": "{{raw}}" } }
            ] } }
            """);

        var health = SmartctlSmartProvider.ParseHealth(@"\\.\PHYSICALDRIVE0", doc.RootElement);

        Assert.Equal(expectedBytes, health.TotalBytesWritten);
    }

    [Theory]
    [InlineData("Total_LBAs_Read", 2000000, 2000000L * 512)]
    [InlineData("Host_Reads_32MiB", 300, 300L * 32 * 1024 * 1024)]
    [InlineData("Host_Reads_GiB", 700, 700L * 1024 * 1024 * 1024)]
    [InlineData("Lifetime_Reads_GiB", 556, 556L * 1024 * 1024 * 1024)]
    [InlineData("Total_Reads_GiB", 20407, 20407L * 1024 * 1024 * 1024)]
    public void ParseHealth_KnownVendorReadAttributeNames_ConvertRawToBytesWithCorrectUnit(
        string name, long raw, long expectedBytes)
    {
        using var doc = JsonDocument.Parse($$"""
            { "ata_smart_attributes": { "table": [
                { "id": 242, "name": "{{name}}", "value": 253, "worst": 253, "thresh": 0,
                  "raw": { "value": {{raw}}, "string": "{{raw}}" } }
            ] } }
            """);

        var health = SmartctlSmartProvider.ParseHealth(@"\\.\PHYSICALDRIVE0", doc.RootElement);

        Assert.Equal(expectedBytes, health.TotalBytesRead);
    }

    [Fact]
    public void ParseHealth_NegativeRawValue_ReturnsNullInsteadOfNonsenseBytes()
    {
        // Bozuk/arızalı bir diskin sahte ham değeri sessizce yanlış (negatif) bir bayt sayısına
        // dönüşmemeli.
        using var doc = JsonDocument.Parse("""
            { "ata_smart_attributes": { "table": [
                { "id": 241, "name": "Total_Writes_GiB", "value": 253, "worst": 253, "thresh": 0,
                  "raw": { "value": -1, "string": "-1" } }
            ] } }
            """);

        var health = SmartctlSmartProvider.ParseHealth(@"\\.\PHYSICALDRIVE0", doc.RootElement);

        Assert.Null(health.TotalBytesWritten);
    }

    [Fact]
    public void ParseHealth_RemainingLifeOutOfNormalizedRange_IsClampedTo100()
    {
        // Bazı firmware'ler normalize değeri 0-100 dışında (ör. 0-253) raporlayabilir; UI'ya
        // anlamsız bir yüzde sızmamalı.
        using var doc = JsonDocument.Parse("""
            { "ata_smart_attributes": { "table": [
                { "id": 231, "name": "SSD_Life_Left", "value": 253, "worst": 253, "thresh": 0,
                  "raw": { "value": 0, "string": "0" } }
            ] } }
            """);

        var health = SmartctlSmartProvider.ParseHealth(@"\\.\PHYSICALDRIVE0", doc.RootElement);

        Assert.Equal(100, health.RemainingLifePercent);
    }

    [Fact]
    public void ParseHealth_PriorityOrderPrefersEarlierCandidateOverLaterTableEntry()
    {
        // Bir diskte HEM yaygın bir ad HEM daha az kesin bir ad varsa (gerçekte olası, bazı
        // firmware'ler hem genel hem üreticiye özel bir öznitelik gösterebilir), öncelik
        // listesindeki önce gelen ad kazanmalı — tablodaki fiziksel sıralama değil.
        using var doc = JsonDocument.Parse("""
            { "ata_smart_attributes": { "table": [
                { "id": 173, "name": "Wear_Leveling_Count", "value": 40, "worst": 40, "thresh": 0,
                  "raw": { "value": 1, "string": "1" } },
                { "id": 202, "name": "Percent_Lifetime_Remain", "value": 85, "worst": 85, "thresh": 0,
                  "raw": { "value": 85, "string": "85" } }
            ] } }
            """);

        var health = SmartctlSmartProvider.ParseHealth(@"\\.\PHYSICALDRIVE0", doc.RootElement);

        Assert.Equal(85, health.RemainingLifePercent);
    }

    // ---- SORUN 7 (v1.0.1 gerçek kullanıcı raporu): Berke'nin SanDisk SSD PLUS 480GB diskinde
    // gerçek smartctl -a --json=c çıktısı yakalandı; ID 173 "Avg_Write_Erase_Ct" normalize
    // değeri (92), CrystalDiskInfo'nun aynı diskte gösterdiği "İyi %92" ile birebir eşleşiyor. ----

    [Fact]
    public void ParseHealth_RealCapturedAvgWriteEraseCt_ReturnsRemainingLifeMatchingCrystalDiskInfo()
    {
        using var doc = JsonDocument.Parse("""
            { "ata_smart_attributes": { "table": [
                { "id": 173, "name": "Avg_Write_Erase_Ct", "value": 92, "worst": 92, "thresh": 0,
                  "raw": { "value": 2933, "string": "2933" } }
            ] } }
            """);

        var health = SmartctlSmartProvider.ParseHealth(@"\\.\PHYSICALDRIVE0", doc.RootElement);

        Assert.Equal(92, health.RemainingLifePercent);
    }

    [Theory]
    [InlineData("Disk_Health_Percentage")]
    [InlineData("Wear_Range_Delta")]
    [InlineData("Life_Used")]
    public void ParseHealth_UnknownFamilyWithLifeWearHealthKeyword_FallsBackToKeywordMatch(string name)
    {
        // Bilinen isim listesi tükendiğinde (tamamen yeni/görülmemiş bir üretici ailesi),
        // "wear"/"life"/"health" kelime parçası geçen bir öznitelik ikincil sinyal olarak
        // kullanılmalı — tahmin döngüsüne yeniden girmemek için.
        using var doc = JsonDocument.Parse($$"""
            { "ata_smart_attributes": { "table": [
                { "id": 250, "name": "{{name}}", "value": 77, "worst": 77, "thresh": 0,
                  "raw": { "value": 1, "string": "1" } }
            ] } }
            """);

        var health = SmartctlSmartProvider.ParseHealth(@"\\.\PHYSICALDRIVE0", doc.RootElement);

        Assert.Equal(77, health.RemainingLifePercent);
    }

    [Fact]
    public void ParseHealth_LifetimeWritesGiB_DoesNotMatchLifeKeywordFallback()
    {
        // Ham "Contains" olsaydı "Lifetime_Writes_GiB" ("life" segmenti değil "Lifetime")
        // yanlışlıkla eşleşirdi — bu bir YÜZDE değil, toplam yazılan veri (GiB). Tam kelime
        // parçası eşleşmesi bunu engelliyor.
        using var doc = JsonDocument.Parse("""
            { "ata_smart_attributes": { "table": [
                { "id": 241, "name": "Lifetime_Writes_GiB", "value": 100, "worst": 100, "thresh": 0,
                  "raw": { "value": 1014, "string": "1014" } }
            ] } }
            """);

        var health = SmartctlSmartProvider.ParseHealth(@"\\.\PHYSICALDRIVE0", doc.RootElement);

        Assert.Null(health.RemainingLifePercent);
    }

    [Fact]
    public void ParseHealth_MediaWearoutIndicator_DoesNotMatchWearKeywordFallbackEither()
    {
        // "Wearout" segmenti tam olarak "Wear" değil — bilinçli dışlama (bkz. ana test dosyasının
        // üstündeki Media_Wearout_Indicator testi) fallback'te de bozulmamalı.
        using var doc = JsonDocument.Parse("""
            { "ata_smart_attributes": { "table": [
                { "id": 230, "name": "Media_Wearout_Indicator", "value": 1, "worst": 1, "thresh": 0,
                  "raw": { "value": 1099511627823, "string": "0x010f005a010f" } }
            ] } }
            """);

        var health = SmartctlSmartProvider.ParseHealth(@"\\.\PHYSICALDRIVE0", doc.RootElement);

        Assert.Null(health.RemainingLifePercent);
    }

    // ---- ID tabanlı son çare (231 -> 232 -> 230): isim listesi tükendiğinde (smartctl bu modeli
    // drivedb.h'da tanımıyor) devreye giren en son yol. ----

    [Theory]
    [InlineData(231)]
    [InlineData(232)]
    [InlineData(230)]
    public void ParseHealth_UnknownNameWithKnownStandardId_FallsBackToIdMatch(int id)
    {
        using var doc = JsonDocument.Parse($$"""
            { "ata_smart_attributes": { "table": [
                { "id": {{id}}, "name": "Unknown_Attribute", "value": 61, "worst": 61, "thresh": 0,
                  "raw": { "value": 39, "string": "39" } }
            ] } }
            """);

        var health = SmartctlSmartProvider.ParseHealth(@"\\.\PHYSICALDRIVE0", doc.RootElement);

        Assert.Equal(61, health.RemainingLifePercent);
    }

    [Fact]
    public void ParseHealth_IdFallbackPrefersEarlierIdOverLaterTableEntry()
    {
        // 231 (SSD_Life_Left) 232'den (Perc_Avail_Resrvd_Space) önce denenmeli — tablodaki fiziksel
        // sıralamadan bağımsız olarak.
        using var doc = JsonDocument.Parse("""
            { "ata_smart_attributes": { "table": [
                { "id": 232, "name": "Unknown_Attribute", "value": 40, "worst": 40, "thresh": 0,
                  "raw": { "value": 60, "string": "60" } },
                { "id": 231, "name": "Unknown_Attribute", "value": 70, "worst": 70, "thresh": 0,
                  "raw": { "value": 30, "string": "30" } }
            ] } }
            """);

        var health = SmartctlSmartProvider.ParseHealth(@"\\.\PHYSICALDRIVE0", doc.RootElement);

        Assert.Equal(70, health.RemainingLifePercent);
    }

    [Fact]
    public void ParseHealth_KnownNameStillWinsOverIdFallback()
    {
        // İsim eşleşmesi bulunduğunda ID tabanlı son çareye hiç düşülmemeli.
        using var doc = JsonDocument.Parse("""
            { "ata_smart_attributes": { "table": [
                { "id": 231, "name": "SSD_Life_Left", "value": 88, "worst": 88, "thresh": 0,
                  "raw": { "value": 12, "string": "12" } }
            ] } }
            """);

        var health = SmartctlSmartProvider.ParseHealth(@"\\.\PHYSICALDRIVE0", doc.RootElement);

        Assert.Equal(88, health.RemainingLifePercent);
    }

    [Fact]
    public void ParseHealth_MediaWearoutIndicatorAtId230_StillExcludedFromIdFallback()
    {
        // ID tabanlı son çare, ID 230 için "Media_Wearout_Indicator" adını da dışlamalı — aksi
        // halde yukarıdaki isim tabanlı dışlama ID yoluyla arkadan dolanılıp bozulurdu (WD Blue/
        // Red/Green SSDs ailesinde bu isim ters anlam taşıyor, bkz. TryGetAtaRemainingLifeById).
        using var doc = JsonDocument.Parse("""
            { "ata_smart_attributes": { "table": [
                { "id": 230, "name": "Media_Wearout_Indicator", "value": 1, "worst": 1, "thresh": 0,
                  "raw": { "value": 1099511627823, "string": "0x010f005a010f" } }
            ] } }
            """);

        var health = SmartctlSmartProvider.ParseHealth(@"\\.\PHYSICALDRIVE0", doc.RootElement);

        Assert.Null(health.RemainingLifePercent);
    }

    [Fact]
    public void ParseHealth_NvmeDiskUnaffected_StillUsesPercentageUsedNotAtaPath()
    {
        // NVMe yolunu bozmama gereksinimi: nvme_smart_health_information_log varken ATA
        // öznitelik tablosu yoksa/karışmışsa bile NVMe hesaplaması önceliğini korumalı.
        using var doc = JsonDocument.Parse("""
            { "nvme_smart_health_information_log": {
                "percentage_used": 8, "data_units_read": 100, "data_units_written": 50
            } }
            """);

        var health = SmartctlSmartProvider.ParseHealth(@"\\.\PHYSICALDRIVE0", doc.RootElement);

        Assert.Equal(92, health.RemainingLifePercent);
        Assert.Equal(100L * 1000 * 512, health.TotalBytesRead);
        Assert.Equal(50L * 1000 * 512, health.TotalBytesWritten);
    }
}
