using System.Diagnostics;
using System.Text.Json;
using SerkonDiskSuite.Core.Interfaces;
using SerkonDiskSuite.Core.Models;
using SerkonDiskSuite.Core.Services;

namespace SerkonDiskSuite.Infrastructure.Smart;

/// <summary>
/// SMART verisini endüstri standardı "smartctl" (smartmontools) aracını çalıştırarak okur.
/// smartctl --json=c çıktısı parse edilir. Kendi ham DeviceIoControl kodumuzu yazmak yerine
/// olgun, binlerce diski destekleyen bu aracı sarmalıyoruz (wrap).
///
/// Not: smartctl'in uygulama ile birlikte tools/ klasöründe dağıtılması beklenir.
/// </summary>
public sealed class SmartctlSmartProvider : ISmartProvider
{
    private readonly string _smartctlPath;

    /// <param name="smartctlPath">smartctl.exe tam yolu. Verilmezse "smartctl" (PATH) kullanılır.</param>
    public SmartctlSmartProvider(string? smartctlPath = null)
    {
        _smartctlPath = smartctlPath ?? "smartctl";
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var (exitCode, _, _) = await RunAsync(["--version"], ct);
            return exitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<SmartHealth> ReadHealthAsync(DiskInfo disk, CancellationToken ct = default)
    {
        // -a: tüm bilgiler, --json=c: sıkıştırılmış JSON
        var (exitCode, stdout, stderr) = await RunAsync(["-a", "--json=c", disk.DevicePath], ct);

        // smartctl exit kodu bir bit maskesidir; 0 = tamamen temiz.
        // 2. bit (değer 4) "device open failed" demektir, bunu hata sayarız.
        if ((exitCode & 0x02) != 0)
            throw new InvalidOperationException(
                $"smartctl diski açamadı ({disk.DevicePath}). Yönetici olarak çalıştırdığınızdan emin olun. {stderr}");

        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;

        int? temperature = TryGetTemperature(root);
        int? remainingLife = TryGetRemainingLife(root);
        bool criticalWarning = TryGetCriticalWarning(root);

        var status = HealthEvaluator.Evaluate(temperature, remainingLife, criticalWarning);

        return new SmartHealth
        {
            DevicePath = disk.DevicePath,
            OverallStatus = status,
            TemperatureCelsius = temperature,
            RemainingLifePercent = remainingLife,
            PowerOnHours = TryGetLong(root, "power_on_time", "hours"),
            PowerCycleCount = TryGetLongDirect(root, "power_cycle_count"),
            UnsafeShutdowns = TryGetNvmeLong(root, "unsafe_shutdowns"),
            TotalBytesRead = TryGetNvmeDataUnits(root, "data_units_read"),
            TotalBytesWritten = TryGetNvmeDataUnits(root, "data_units_written"),
            Attributes = ExtractAttributes(root),
            Timestamp = DateTimeOffset.Now
        };
    }

    // ---- JSON ayrıştırma yardımcıları (smartctl şeması) ----

    private static int? TryGetTemperature(JsonElement root)
        => root.TryGetProperty("temperature", out var t) && t.TryGetProperty("current", out var c)
            ? c.GetInt32()
            : null;

    private static int? TryGetRemainingLife(JsonElement root)
    {
        // NVMe: nvme_smart_health_information_log.percentage_used
        if (root.TryGetProperty("nvme_smart_health_information_log", out var log)
            && log.TryGetProperty("percentage_used", out var used))
        {
            return HealthEvaluator.PercentageUsedToRemainingLife(used.GetInt32());
        }
        return null;
    }

    private static bool TryGetCriticalWarning(JsonElement root)
    {
        if (root.TryGetProperty("nvme_smart_health_information_log", out var log)
            && log.TryGetProperty("critical_warning", out var warn))
        {
            return warn.GetInt32() != 0;
        }
        // SATA: smart_status.passed false ise kritik
        if (root.TryGetProperty("smart_status", out var st)
            && st.TryGetProperty("passed", out var passed))
        {
            return !passed.GetBoolean();
        }
        return false;
    }

    private static long? TryGetLong(JsonElement root, string obj, string prop)
        => root.TryGetProperty(obj, out var o) && o.TryGetProperty(prop, out var p)
            ? p.GetInt64()
            : null;

    private static long? TryGetLongDirect(JsonElement root, string prop)
        => root.TryGetProperty(prop, out var p) ? p.GetInt64() : null;

    private static long? TryGetNvmeLong(JsonElement root, string prop)
        => root.TryGetProperty("nvme_smart_health_information_log", out var log)
           && log.TryGetProperty(prop, out var p)
            ? p.GetInt64()
            : null;

    /// <summary>NVMe data_units_* değeri 1000 x 512 bayt birimindedir.</summary>
    private static long? TryGetNvmeDataUnits(JsonElement root, string prop)
    {
        var units = TryGetNvmeLong(root, prop);
        return units is { } u ? u * 1000L * 512L : null;
    }

    private static IReadOnlyList<SmartAttribute> ExtractAttributes(JsonElement root)
    {
        var list = new List<SmartAttribute>();

        // SATA ATA öznitelikleri
        if (root.TryGetProperty("ata_smart_attributes", out var ata)
            && ata.TryGetProperty("table", out var table)
            && table.ValueKind == JsonValueKind.Array)
        {
            foreach (var attr in table.EnumerateArray())
            {
                list.Add(new SmartAttribute(
                    Id: attr.GetProperty("id").GetInt32().ToString(),
                    Name: attr.GetProperty("name").GetString() ?? "",
                    RawValue: attr.TryGetProperty("raw", out var raw) && raw.TryGetProperty("string", out var rs)
                        ? rs.GetString() ?? "" : "",
                    NormalizedValue: attr.TryGetProperty("value", out var v) ? v.GetInt32() : null,
                    WorstValue: attr.TryGetProperty("worst", out var w) ? w.GetInt32() : null,
                    Threshold: attr.TryGetProperty("thresh", out var th) ? th.GetInt32() : null));
            }
        }

        // NVMe sağlık log'unu da öznitelik olarak düzleştir
        if (root.TryGetProperty("nvme_smart_health_information_log", out var nvme))
        {
            foreach (var prop in nvme.EnumerateObject())
            {
                if (prop.Value.ValueKind is JsonValueKind.Number)
                {
                    list.Add(new SmartAttribute(
                        Id: "-",
                        Name: prop.Name,
                        RawValue: prop.Value.GetRawText()));
                }
            }
        }

        return list;
    }

    // ---- Süreç çalıştırma ----

    private async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(
        string[] args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _smartctlPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi };
        proc.Start();

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        return (proc.ExitCode, await stdoutTask, await stderrTask);
    }
}
