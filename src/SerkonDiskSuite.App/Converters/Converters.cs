using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using SerkonDiskSuite.Core.Formatting;
using SerkonDiskSuite.Core.Models;

namespace SerkonDiskSuite.App.Converters;

/// <summary>RAM modül listesi -> " (2x16 GB, DDR4-3200)" gibi parantezli bir ek metin
/// (RamModuleFormatter.FormatSummary'yi sarmalar). Liste boşsa/null ise boş dizge döner
/// (Run'da hiçbir şey görünmez).</summary>
public sealed class RamModulesToParenthesizedStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is IReadOnlyList<RamModuleInfo> modules && RamModuleFormatter.FormatSummary(modules) is { } summary
            ? $" ({summary})"
            : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>HealthStatus -> renkli fırça (yeşil/sarı/kırmızı).</summary>
public sealed class HealthToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            HealthStatus.Good => new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)),
            HealthStatus.Caution => new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)),
            HealthStatus.Bad => new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
            _ => new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xA8))
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Bayt (long) -> okunabilir "1.5 GB" gibi.</summary>
public sealed class BytesToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is long bytes ? DisplayFormatting.FormatBytes(bytes) : "-";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>null değilse Visible, null ise Collapsed. Singleton olarak XAML'de {x:Static} ile kullanılır.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public static readonly NullToVisibilityConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not null && !(value is string s && string.IsNullOrEmpty(s))
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>NullToVisibilityConverter'ın tersi: null (veya boş string) -> Visible,
/// değer varsa -> Collapsed. Bir sensör bu donanımda okunamadığında "Bu sistemde
/// okunamıyor" yer tutucusunu göstermek için kullanılır.</summary>
public sealed class InverseNullToVisibilityConverter : IValueConverter
{
    public static readonly InverseNullToVisibilityConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not null && !(value is string s && string.IsNullOrEmpty(s))
            ? Visibility.Collapsed
            : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Boş olmayan koleksiyon -> Visible, null veya boş koleksiyon -> Collapsed
/// (NullToVisibilityConverter'ın aksine, dolu ama sıfır elemanlı koleksiyonları da gizler).</summary>
public sealed class CollectionToVisibilityConverter : IValueConverter
{
    public static readonly CollectionToVisibilityConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is System.Collections.ICollection { Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Boş (veya null) koleksiyon -> Visible, dolu koleksiyon -> Collapsed
/// (CollectionToVisibilityConverter'ın tersi — "liste boş" mesajları için).</summary>
public sealed class InverseCollectionToVisibilityConverter : IValueConverter
{
    public static readonly InverseCollectionToVisibilityConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is System.Collections.ICollection { Count: > 0 } ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>null -> true, değer varsa -> false. Yüzdesi bilinmeyen bir işlemin ilerleme
/// çubuğunu "belirsiz" (IsIndeterminate) moda almak için kullanılır.</summary>
public sealed class IsNullConverter : IValueConverter
{
    public static readonly IsNullConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>bool'un tersini döner (IsEnabled gibi Visibility olmayan hedefler için —
/// InverseBoolToVisibilityConverter'ın aksine bir Visibility değil, doğrudan bool üretir).</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public static readonly InverseBooleanConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not true;
}

/// <summary>MultiBinding: [0]=IsRunning (bool), [1]=PercentRemaining (int?). İkisi de
/// sağlanmışsa (çalışıyor VE yüzde biliniyorsa) Visible, aksi halde Collapsed — self-test
/// "Kalan: %X" satırı için (bkz. madde B1/B2).</summary>
public sealed class RunningWithKnownPercentToVisibilityConverter : IMultiValueConverter
{
    public static readonly RunningWithKnownPercentToVisibilityConverter Instance = new();

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values is [true, int] ? Visibility.Visible : Visibility.Collapsed;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>RunningWithKnownPercentToVisibilityConverter'ın tersi: çalışıyor ama yüzde
/// bilinmiyorsa Visible ("İlerleme bilgisi bu diskte raporlanmıyor" mesajı için).</summary>
public sealed class RunningWithUnknownPercentToVisibilityConverter : IMultiValueConverter
{
    public static readonly RunningWithUnknownPercentToVisibilityConverter Instance = new();

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values is [true, null] ? Visibility.Visible : Visibility.Collapsed;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>true -> Visible, false -> Collapsed.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public static readonly BoolToVisibilityConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

/// <summary>true -> Collapsed, false -> Visible (BoolToVisibilityConverter'ın tersi).</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public static readonly InverseBoolToVisibilityConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not Visibility.Visible;
}

/// <summary>DiskBusType -> standart arayüz kısaltması (NVMe, SATA, USB, SAS, SCSI, Bilinmiyor).</summary>
public sealed class BusTypeToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value switch
        {
            DiskBusType.Nvme => "NVMe",
            DiskBusType.Sata => "SATA",
            DiskBusType.Usb => "USB",
            DiskBusType.Sas => "SAS",
            DiskBusType.Scsi => "SCSI",
            _ => "Bilinmiyor"
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>IsSolidState (bool) -> "SSD" / "HDD".</summary>
public sealed class SolidStateToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? "SSD" : "HDD";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Saat (long) -> "8.962 saat" (tr-TR binlik ayraç).</summary>
public sealed class HoursToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is long hours ? DisplayFormatting.FormatHours(hours) : "-";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Tam sayı (long) -> tr-TR binlik ayraçlı dizge (ör. "12.345").</summary>
public sealed class CountToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is long count ? DisplayFormatting.FormatCount(count) : "-";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>SelfTestType (enum) -> Türkçe görünen ad ("Kısa" / "Uzun").</summary>
public sealed class SelfTestTypeToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is SelfTestType.Long ? "Uzun" : "Kısa";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>BenchmarkTestKind (enum) -> Türkçe görünen ad (ör. "Sıralı Yazma").</summary>
public sealed class BenchmarkTestKindToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is BenchmarkTestKind kind ? BenchmarkTestKindLabels.ToTurkish(kind) : "-";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Ondalıklı sayı (double/double?) -> tr-TR binlik ayraçlı dizge (ör. "25.806").
/// XAML `StringFormat=N0` her zaman en-US kültürüne düştüğü için (ör. yanlışlıkla
/// "25,806") throughput/IOPS gibi tüm sayısal gösterimlerde bunun yerine kullanılır.
/// </summary>
public sealed class NumberToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value switch
        {
            double d => DisplayFormatting.FormatNumber(d),
            _ => "-"
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
