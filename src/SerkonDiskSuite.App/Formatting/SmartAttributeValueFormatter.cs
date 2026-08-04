using System.Globalization;
using SerkonDiskSuite.Core.Models;

namespace SerkonDiskSuite.App.Formatting;

/// <summary>
/// SMART öznitelik ham değerini, öznitelik adına göre anlamlı bir birime çevirip
/// biçimlendirir (ör. "data_units_read" -> bayt -> "8,93 TB", "power_on_hours" -> "8.962 saat").
/// Tanınmayan veya sayısal olmayan (ör. bileşik "1725 (17 245 0)" gibi) ham değerler
/// olduğu gibi bırakılır.
/// </summary>
public static class SmartAttributeValueFormatter
{
    /// <summary>NVMe data_units_* alanları 1000 x 512 baytlık birimlerdedir (bkz. NVMe spesifikasyonu).</summary>
    private const long NvmeDataUnitBytes = 1000L * 512L;

    public static string FormatDisplayValue(SmartAttribute attribute)
    {
        if (!long.TryParse(attribute.RawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var raw))
            return attribute.RawValue;

        return attribute.Name.ToLowerInvariant() switch
        {
            "data_units_read" or "data_units_written" => DisplayFormatting.FormatBytes(raw * NvmeDataUnitBytes),
            "total_lbas_written" or "total_lbas_read" => DisplayFormatting.FormatCount(raw),
            "power_on_hours" => DisplayFormatting.FormatHours(raw),
            "percentage_used" or "available_spare" or "available_spare_threshold" => $"%{DisplayFormatting.FormatCount(raw)}",
            "temperature" or "temperature_celsius" or "airflow_temperature_cel" => $"{raw} °C",
            "controller_busy_time" or "warning_temp_time" or "critical_comp_time"
                => $"{DisplayFormatting.FormatCount(raw)} dakika",
            "power_cycles" or "power_cycle_count" or "unsafe_shutdowns" or "media_errors"
                or "num_err_log_entries" or "host_reads" or "host_writes" or "start_stop_count"
                or "load_cycle_count" => DisplayFormatting.FormatCount(raw),
            _ => attribute.RawValue
        };
    }
}
