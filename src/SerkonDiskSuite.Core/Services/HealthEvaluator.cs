using SerkonDiskSuite.Core.Models;

namespace SerkonDiskSuite.Core.Services;

/// <summary>
/// Ham SMART metriklerini genel bir sağlık durumuna çeviren saf (pure) iş mantığı.
/// Donanıma dokunmaz; bu yüzden kolayca birim testi yazılabilir.
/// </summary>
public static class HealthEvaluator
{
    // Eşikler sektör pratiğine göre seçildi; ileride yapılandırılabilir hale getirilebilir.
    //
    // Sıcaklık eşikleri kullanıcı geri bildirimiyle güncellendi (2026-08-11): Caution eskiden
    // 60°C'de başlıyordu ve Bad zaten 70°C'ydi — aralarında sadece 10°C'lik dar bir "Isınıyor!"
    // bandı vardı, kullanıcı bunun gereksiz erken tetiklendiğini bildirdi. Caution 70°C'ye
    // çekildi; Bad'i de aynı değerde bırakmak Caution bandını tamamen ortadan kaldırırdı (>=70°C
    // direkt Bad olur, Caution hiç tetiklenmezdi) — bu yüzden Bad da 80°C'ye çekilerek eski
    // 10°C'lik aralık korundu.
    private const int CautionTemperatureC = 70;
    private const int BadTemperatureC = 80;
    private const int CautionLifePercent = 20;   // kalan ömür bunun altındaysa dikkat
    private const int BadLifePercent = 5;

    /// <summary>
    /// Sıcaklık, kalan ömür ve kritik uyarı bayraklarına bakarak bir durum döndürür.
    /// Sadece durumu isteyen (nedeniyle ilgilenmeyen) çağıranlar için ince bir sarmalayıcı —
    /// bkz. EvaluateDetailed.
    /// </summary>
    public static HealthStatus Evaluate(
        int? temperatureCelsius,
        int? remainingLifePercent,
        bool hasCriticalWarning)
        => EvaluateDetailed(temperatureCelsius, remainingLifePercent, hasCriticalWarning).Status;

    /// <summary>
    /// Evaluate ile aynı mantık, ama Caution durumunun HANGİ ölçüme dayandığını da döndürür
    /// (UI'nın "Dikkat" yerine sıcaklık için "Isınıyor!" gibi daha açık bir metin göstermesi
    /// için — bkz. SmartHealth.CautionReason). Öncelik sırası mevcut değerlendirme sırasını
    /// yansıtır: sıcaklık önce kontrol edilir; sıcaklık zaten Caution'a soktuysa, kalan ömür de
    /// aynı zamanda Caution bandındaysa bile neden "Temperature" olarak kalır (ikisi birden
    /// Caution tetiklerse hangisinin gösterileceğine dair belirsizliği ortadan kaldırmak için
    /// basit, deterministik bir öncelik).
    /// </summary>
    public static (HealthStatus Status, HealthCautionReason Reason) EvaluateDetailed(
        int? temperatureCelsius,
        int? remainingLifePercent,
        bool hasCriticalWarning)
    {
        if (hasCriticalWarning)
            return (HealthStatus.Bad, HealthCautionReason.None);

        // SORUN 5 (v1.0.0 gerçek kullanıcı raporu): sıcaklık VE kalan ömür ikisi de
        // okunamadıysa (smartctl bu diski tanımadı, disk desteklemiyor, vb.) eskiden
        // buradan hiç geçmeden "Good" varsayılıyordu — ekranda "Durum: İyi" yazarken
        // altındaki tüm kartlar boş kalıyordu, kullanıcı bunu yanıltıcı/bozuk sanıyordu.
        // Hiçbir gerçek sinyal yokken sağlık iddia etmek yanlış; Unknown daha doğru.
        if (temperatureCelsius is null && remainingLifePercent is null)
            return (HealthStatus.Unknown, HealthCautionReason.None);

        var status = HealthStatus.Good;
        var reason = HealthCautionReason.None;

        if (temperatureCelsius is { } temp)
        {
            if (temp >= BadTemperatureC) return (HealthStatus.Bad, HealthCautionReason.None);
            if (temp >= CautionTemperatureC)
            {
                status = HealthStatus.Caution;
                reason = HealthCautionReason.Temperature;
            }
        }

        if (remainingLifePercent is { } life)
        {
            if (life <= BadLifePercent) return (HealthStatus.Bad, HealthCautionReason.None);
            if (life <= CautionLifePercent && status != HealthStatus.Caution)
            {
                status = HealthStatus.Caution;
                reason = HealthCautionReason.RemainingLife;
            }
        }

        return (status, reason);
    }

    /// <summary>NVMe "percentage_used" (0-100+) değerini kalan ömür yüzdesine çevirir.</summary>
    public static int PercentageUsedToRemainingLife(int percentageUsed)
        => Math.Clamp(100 - percentageUsed, 0, 100);
}
