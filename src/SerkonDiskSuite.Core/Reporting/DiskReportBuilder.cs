using System.Text;
using System.Text.Json;
using SerkonDiskSuite.Core.Formatting;
using SerkonDiskSuite.Core.Models;

namespace SerkonDiskSuite.Core.Reporting;

/// <summary>
/// Seçili diskin SMART verisi + son benchmark sonuçlarından CrystalDiskInfo tarzı düz metin
/// (panoya kopyalanabilir/dosyaya kaydedilebilir) ve JSON rapor üretir.
/// </summary>
public static class DiskReportBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string BuildPlainText(
        DiskInfo disk,
        SmartHealth? health,
        IReadOnlyList<BenchmarkResult> benchmarkResults,
        HardwareSnapshot? hardware = null,
        IReadOnlyList<RamModuleInfo>? ramModules = null)
    {
        var sb = new StringBuilder();
        const string separator = "----------------------------------------------------------";

        sb.AppendLine("Serkon Disk Suite - Disk Raporu");
        sb.AppendLine(separator);
        sb.AppendLine($"Model            : {disk.ModelName}");
        sb.AppendLine($"Seri No          : {disk.SerialNumber}");
        sb.AppendLine($"Firmware         : {disk.FirmwareVersion}");
        sb.AppendLine($"Tür / Arayüz     : {(disk.IsSolidState ? "SSD" : "HDD")} / {disk.BusType}");
        sb.AppendLine($"Bağlantı         : {disk.TransferMode ?? "-"}");
        sb.AppendLine($"Kapasite         : {disk.CapacityDisplay}");
        sb.AppendLine($"Sürücü Harfleri  : {(disk.DriveLetters.Count > 0 ? string.Join(", ", disk.DriveLetters) : "-")}");
        sb.AppendLine();

        if (health is not null)
        {
            sb.AppendLine("SMART Sağlık Bilgisi");
            sb.AppendLine(separator);
            sb.AppendLine($"Genel Durum          : {health.OverallStatus}");
            sb.AppendLine($"Sıcaklık             : {health.TemperatureCelsius?.ToString() ?? "-"} °C");
            sb.AppendLine($"Kalan Ömür           : {health.RemainingLifePercent?.ToString() ?? "-"} %");
            sb.AppendLine($"Kullanılabilir Yedek : {health.AvailableSparePercent?.ToString() ?? "-"} %");
            sb.AppendLine($"Açılma Sayısı        : {health.PowerCycleCount?.ToString() ?? "-"}");
            sb.AppendLine($"Güvensiz Kapanma     : {health.UnsafeShutdowns?.ToString() ?? "-"}");
            sb.AppendLine($"Çalışma Süresi       : {health.PowerOnHours?.ToString() ?? "-"} saat");
            sb.AppendLine($"Toplam Okunan        : {(health.TotalBytesRead is { } r ? DisplayFormatting.FormatBytes(r) : "-")}");
            sb.AppendLine($"Toplam Yazılan       : {(health.TotalBytesWritten is { } w ? DisplayFormatting.FormatBytes(w) : "-")}");

            if (health.CriticalWarningFlags.Count > 0)
            {
                sb.AppendLine("Kritik Uyarılar      :");
                foreach (var flag in health.CriticalWarningFlags)
                    sb.AppendLine($"  - {flag}");
            }
            sb.AppendLine();

            if (health.Attributes.Count > 0)
            {
                sb.AppendLine("SMART Öznitelikleri");
                sb.AppendLine(separator);
                foreach (var attr in health.Attributes)
                {
                    string name = SmartAttributeLabels.GetDisplayName(attr.Name);
                    string value = SmartAttributeValueFormatter.FormatDisplayValue(attr);
                    sb.AppendLine($"{name,-32} {value}");
                }
                sb.AppendLine();
            }
        }

        if (benchmarkResults.Count > 0)
        {
            sb.AppendLine("Son Benchmark Sonuçları");
            sb.AppendLine(separator);
            foreach (var result in benchmarkResults)
            {
                string kind = BenchmarkTestKindLabels.ToTurkish(result.Kind);
                string throughput = $"{DisplayFormatting.FormatNumber(result.ThroughputMBps)} MB/s";
                string iops = result.Iops is { } i ? $", {DisplayFormatting.FormatNumber(i)} IOPS" : "";
                sb.AppendLine($"{kind,-18} {throughput}{iops}  ({result.ProfileName})");
            }
            sb.AppendLine();
        }

        if (hardware is not null)
        {
            sb.AppendLine("Sistem Anlık Durumu (CPU/GPU/RAM)");
            sb.AppendLine(separator);
            sb.AppendLine($"CPU Yük              : {(hardware.CpuLoadPercent is { } cl ? DisplayFormatting.FormatNumber(cl) : "-")} %");
            sb.AppendLine($"CPU Sıcaklık         : {(hardware.CpuTemperatureCelsius is { } ct ? DisplayFormatting.FormatNumber(ct) : "-")} °C");
            if (hardware.GpuName is not null)
            {
                sb.AppendLine($"GPU                  : {hardware.GpuName}");
                sb.AppendLine($"GPU Yük              : {(hardware.GpuLoadPercent is { } gl ? DisplayFormatting.FormatNumber(gl) : "-")} %");
                sb.AppendLine($"GPU Sıcaklık         : {(hardware.GpuTemperatureCelsius is { } gt ? DisplayFormatting.FormatNumber(gt) : "-")} °C");
                sb.AppendLine($"GPU Bellek Kullanımı : {(hardware.GpuMemoryUsedBytes is { } gm ? DisplayFormatting.FormatBytes(gm) : "-")}");
            }
            sb.AppendLine($"RAM Kullanımı        : {(hardware.RamUsedBytes is { } ru ? DisplayFormatting.FormatBytes(ru) : "-")} / {(hardware.RamTotalBytes is { } rt ? DisplayFormatting.FormatBytes(rt) : "-")}");
            if (ramModules is { Count: > 0 } && RamModuleFormatter.FormatSummary(ramModules) is { } ramSummary)
            {
                sb.AppendLine($"RAM Modülleri        : {ramSummary}");
            }
            sb.AppendLine();
        }

        sb.AppendLine($"Rapor oluşturulma zamanı: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}");
        return sb.ToString();
    }

    public static string BuildJson(
        DiskInfo disk,
        SmartHealth? health,
        IReadOnlyList<BenchmarkResult> benchmarkResults,
        HardwareSnapshot? hardware = null,
        IReadOnlyList<RamModuleInfo>? ramModules = null)
    {
        var report = new
        {
            GeneratedAt = DateTimeOffset.Now,
            Disk = disk,
            Health = health,
            BenchmarkResults = benchmarkResults,
            Hardware = hardware,
            RamModules = ramModules,
        };
        return JsonSerializer.Serialize(report, JsonOptions);
    }
}
