using System.Globalization;

namespace SerkonDiskSuite.Core.Formatting;

/// <summary>Türkçe (tr-TR) yerelleştirilmiş sayı/bayt/süre biçimlendirme yardımcıları.</summary>
public static class DisplayFormatting
{
    public static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private static readonly string[] ByteUnits = ["B", "KB", "MB", "GB", "TB", "PB"];

    /// <summary>Bayt değerini "8,93 TB" gibi okunabilir bir dizgeye çevirir.</summary>
    public static string FormatBytes(long bytes)
    {
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < ByteUnits.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size.ToString("N2", Turkish)} {ByteUnits[unit]}";
    }

    /// <summary>Saat değerini "8.962 saat" gibi biçimlendirir.</summary>
    public static string FormatHours(long hours) => $"{hours.ToString("N0", Turkish)} saat";

    /// <summary>Tam sayıyı binlik ayraçla biçimlendirir (ör. "12.345").</summary>
    public static string FormatCount(long value) => value.ToString("N0", Turkish);

    /// <summary>
    /// Ondalıklı bir değeri tr-TR binlik/ondalık ayraçlarıyla biçimlendirir (ör. "1.771",
    /// "25.806"). WPF'in XAML `StringFormat=N0` gibi kısayolları her zaman en-US kültürüne
    /// düştüğü için (ör. "1,771"), throughput/IOPS gibi tüm sayısal gösterimler bu metot
    /// üzerinden geçmelidir.
    /// </summary>
    public static string FormatNumber(double value, int decimals = 0)
        => value.ToString("N" + decimals, Turkish);
}
