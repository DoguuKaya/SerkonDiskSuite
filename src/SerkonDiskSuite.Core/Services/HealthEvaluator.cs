using SerkonDiskSuite.Core.Models;

namespace SerkonDiskSuite.Core.Services;

/// <summary>
/// Ham SMART metriklerini genel bir sağlık durumuna çeviren saf (pure) iş mantığı.
/// Donanıma dokunmaz; bu yüzden kolayca birim testi yazılabilir.
/// </summary>
public static class HealthEvaluator
{
    // Eşikler sektör pratiğine göre seçildi; ileride yapılandırılabilir hale getirilebilir.
    private const int CautionTemperatureC = 60;
    private const int BadTemperatureC = 70;
    private const int CautionLifePercent = 20;   // kalan ömür bunun altındaysa dikkat
    private const int BadLifePercent = 5;

    /// <summary>
    /// Sıcaklık, kalan ömür ve kritik uyarı bayraklarına bakarak bir durum döndürür.
    /// </summary>
    public static HealthStatus Evaluate(
        int? temperatureCelsius,
        int? remainingLifePercent,
        bool hasCriticalWarning)
    {
        if (hasCriticalWarning)
            return HealthStatus.Bad;

        var status = HealthStatus.Good;

        if (temperatureCelsius is { } temp)
        {
            if (temp >= BadTemperatureC) return HealthStatus.Bad;
            if (temp >= CautionTemperatureC) status = HealthStatus.Caution;
        }

        if (remainingLifePercent is { } life)
        {
            if (life <= BadLifePercent) return HealthStatus.Bad;
            if (life <= CautionLifePercent) status = HealthStatus.Caution;
        }

        return status;
    }

    /// <summary>NVMe "percentage_used" (0-100+) değerini kalan ömür yüzdesine çevirir.</summary>
    public static int PercentageUsedToRemainingLife(int percentageUsed)
        => Math.Clamp(100 - percentageUsed, 0, 100);
}
