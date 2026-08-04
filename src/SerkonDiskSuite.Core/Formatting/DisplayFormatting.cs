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
}
