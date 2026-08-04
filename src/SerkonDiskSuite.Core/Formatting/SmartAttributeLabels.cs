using System.Globalization;

namespace SerkonDiskSuite.Core.Formatting;

/// <summary>
/// Ham SMART öznitelik adlarını (ör. "data_units_read", "Power_On_Hours") okunabilir
/// Türkçe etiketlere çevirir. Eşleşme yoksa alt çizgileri boşluğa çevirip baş harfleri
/// büyüterek makul bir varsayılan üretir; ham ad her durumda tooltip'te ayrıca gösterilir.
/// </summary>
public static class SmartAttributeLabels
{
    private static readonly IReadOnlyDictionary<string, string> Map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        // NVMe sağlık log alanları (nvme_smart_health_information_log)
        ["critical_warning"] = "Kritik Uyarı",
        ["temperature"] = "Sıcaklık",
        ["available_spare"] = "Kullanılabilir Yedek",
        ["available_spare_threshold"] = "Yedek Eşiği",
        ["percentage_used"] = "Kullanım Yüzdesi",
        ["data_units_read"] = "Okunan Veri Birimi",
        ["data_units_written"] = "Yazılan Veri Birimi",
        ["host_reads"] = "Ana Bilgisayar Okuma Komutu",
        ["host_writes"] = "Ana Bilgisayar Yazma Komutu",
        ["controller_busy_time"] = "Denetleyici Meşgul Süresi",
        ["power_cycles"] = "Açılma Sayısı",
        ["power_on_hours"] = "Çalışma Süresi",
        ["unsafe_shutdowns"] = "Güvensiz Kapanma Sayısı",
        ["media_errors"] = "Ortam Hatası Sayısı",
        ["num_err_log_entries"] = "Hata Günlüğü Kaydı",
        ["warning_temp_time"] = "Uyarı Sıcaklığı Süresi",
        ["critical_comp_time"] = "Kritik Sıcaklık Süresi",
        ["nsid"] = "Ad Alanı No",

        // Yaygın ATA/SATA öznitelikleri (smartctl "name" alanı)
        ["raw_read_error_rate"] = "Ham Okuma Hata Oranı",
        ["reallocated_sector_ct"] = "Yeniden Tahsis Edilen Sektör",
        ["reallocated_event_count"] = "Yeniden Tahsis Olayı",
        ["spin_up_time"] = "Dönüş Başlatma Süresi",
        ["start_stop_count"] = "Başlat/Durdur Sayısı",
        ["seek_error_rate"] = "Arama Hata Oranı",
        ["power_cycle_count"] = "Açılma Sayısı",
        ["current_pending_sector"] = "Bekleyen Sektör",
        ["offline_uncorrectable"] = "Çevrimdışı Düzeltilemeyen Sektör",
        ["udma_crc_error_count"] = "UDMA CRC Hatası",
        ["load_cycle_count"] = "Yük Döngüsü Sayısı",
        ["temperature_celsius"] = "Sıcaklık (°C)",
        ["airflow_temperature_cel"] = "Hava Akışı Sıcaklığı (°C)",
        ["program_fail_cnt_total"] = "Programlama Hatası Sayısı",
        ["erase_fail_count_total"] = "Silme Hatası Sayısı",
        ["wear_leveling_count"] = "Aşınma Dengeleme Sayısı",
        ["total_lbas_written"] = "Yazılan Toplam LBA",
        ["total_lbas_read"] = "Okunan Toplam LBA",
    };

    /// <summary>Ham öznitelik adı için Türkçe etiket döndürür; eşleşme yoksa okunabilir bir varsayılan üretir.</summary>
    public static string GetDisplayName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
            return rawName;

        return Map.TryGetValue(rawName, out var label) ? label : Prettify(rawName);
    }

    private static string Prettify(string rawName)
    {
        var culture = CultureInfo.GetCultureInfo("tr-TR");
        var words = rawName.Replace('_', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", words.Select(w => culture.TextInfo.ToTitleCase(w.ToLower(culture))));
    }
}
