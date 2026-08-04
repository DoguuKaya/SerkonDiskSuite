using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using SerkonDiskSuite.App.Formatting;
using SerkonDiskSuite.Core.Models;

namespace SerkonDiskSuite.App.Converters;

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

/// <summary>true -> Visible, false -> Collapsed.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public static readonly BoolToVisibilityConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Visible;
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
