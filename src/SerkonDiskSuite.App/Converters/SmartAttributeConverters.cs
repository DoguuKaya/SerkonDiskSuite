using System.Globalization;
using System.Windows.Data;
using SerkonDiskSuite.Core.Formatting;
using SerkonDiskSuite.Core.Models;

namespace SerkonDiskSuite.App.Converters;

/// <summary>Bir SmartAttribute satırının ham adını Türkçe etikete çevirir (ham ad ayrıca tooltip'te gösterilir).</summary>
public sealed class SmartAttributeNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is SmartAttribute attr ? SmartAttributeLabels.GetDisplayName(attr.Name) : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Bir SmartAttribute satırının ham değerini öznitelik adına uygun birimde biçimlendirir.</summary>
public sealed class SmartAttributeValueConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is SmartAttribute attr ? SmartAttributeValueFormatter.FormatDisplayValue(attr) : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
