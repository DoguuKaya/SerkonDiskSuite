using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
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

    private static readonly Regex ScanLineRegex = new(@"^(\S+)\s+-d\s+(\S+)", RegexOptions.Compiled);

    public async Task<SmartHealth> ReadHealthAsync(DiskInfo disk, CancellationToken ct = default)
    {
        var (_, exitCode, stdout, stderr) = await ResolveDeviceAsync(disk, ct);

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
            AvailableSparePercent = TryGetNvmeInt(root, "available_spare"),
            TotalBytesRead = TryGetNvmeDataUnits(root, "data_units_read"),
            TotalBytesWritten = TryGetNvmeDataUnits(root, "data_units_written"),
            Attributes = ExtractAttributes(root),
            CriticalWarningFlags = ExtractCriticalWarningFlags(root),
            Timestamp = DateTimeOffset.Now
        };
    }

    public async Task StartSelfTestAsync(DiskInfo disk, SelfTestType type, CancellationToken ct = default)
    {
        var (deviceArgs, _, _, _) = await ResolveDeviceAsync(disk, ct);

        string testArg = type == SelfTestType.Long ? "long" : "short";
        var (exitCode, _, stderr) = await RunAsync(["-t", testArg, ..deviceArgs], ct);

        if ((exitCode & 0x02) != 0)
            throw new InvalidOperationException(
                $"smartctl self-test başlatamadı ({disk.DevicePath}). Yönetici olarak çalıştırdığınızdan emin olun. {stderr}");
    }

    public async Task<SelfTestStatus> GetSelfTestStatusAsync(DiskInfo disk, CancellationToken ct = default)
    {
        var (_, exitCode, stdout, stderr) = await ResolveDeviceAsync(disk, ct);

        if ((exitCode & 0x02) != 0)
            throw new InvalidOperationException(
                $"smartctl diski açamadı ({disk.DevicePath}). {stderr}");

        using var doc = JsonDocument.Parse(stdout);
        return ParseSelfTestStatus(doc.RootElement);
    }

    /// <summary>
    /// smartctl'in self-test durumunu ayrıştırır. ATA/SATA'da `ata_smart_data.self_test.status`
    /// (value/string/remaining_percent). NVMe'de gerçek bir KINGSTON SNV2S1000G üzerinde
    /// `smartctl -a --json=c` ile doğrulanan şema kullanılır:
    /// `nvme_self_test_log.current_self_test_operation.{value,string}` (value=0 -> test
    /// çalışmıyor) ve geçmiş sonuçlar için `nvme_self_test_log.table[]` (en yeni kayıt ilk
    /// sırada) - her kayıtta `self_test_code.{value,string}` (ör. "Short"),
    /// `self_test_result.{value,string}` (value=0 -> "Completed without error"),
    /// `power_on_hours`. NVMe'de test ÇALIŞIRKEN kalan yüzdeyi taşıyan alan adı bu makinede
    /// hiç doğrulanamadı (test tetiklenmedi, saatler sürebilir) — bu yüzden NVMe için
    /// PercentRemaining her zaman null döner; tahmini bir alan adı kullanılmadı.
    /// Ne ATA ne NVMe self-test verisi bulunamazsa (disk gerçekten desteklemiyor olabilir),
    /// arayüzde boş bırakmak yerine bunu açıkça belirten bir mesaj döner.
    /// </summary>
    public static SelfTestStatus ParseSelfTestStatus(JsonElement root)
    {
        if (root.TryGetProperty("ata_smart_data", out var data)
            && data.TryGetProperty("self_test", out var ataSelfTest)
            && ataSelfTest.TryGetProperty("status", out var ataStatus))
        {
            string? description = ataStatus.TryGetProperty("string", out var s) ? s.GetString() : null;
            int? remaining = ataStatus.TryGetProperty("remaining_percent", out var r) ? r.GetInt32() : null;
            bool isRunning = remaining is not null
                || (description?.Contains("in progress", StringComparison.OrdinalIgnoreCase) ?? false);
            bool? passed = !isRunning && description is not null
                ? description.Contains("without error", StringComparison.OrdinalIgnoreCase)
                : null;

            return new SelfTestStatus(isRunning, remaining, description, passed);
        }

        if (root.TryGetProperty("nvme_self_test_log", out var nvmeLog))
        {
            bool isRunning = false;
            string? runningDescription = null;
            if (nvmeLog.TryGetProperty("current_self_test_operation", out var current)
                && current.TryGetProperty("value", out var opValue))
            {
                isRunning = opValue.GetInt32() != 0;
                runningDescription = current.TryGetProperty("string", out var opString) ? opString.GetString() : null;
            }

            if (isRunning)
                return new SelfTestStatus(true, PercentRemaining: null, runningDescription, Passed: null);

            if (nvmeLog.TryGetProperty("table", out var table)
                && table.ValueKind == JsonValueKind.Array
                && table.GetArrayLength() > 0)
            {
                var last = table[0];
                string? testType = last.TryGetProperty("self_test_code", out var code)
                    && code.TryGetProperty("string", out var codeStr) ? codeStr.GetString() : null;
                string? result = last.TryGetProperty("self_test_result", out var res)
                    && res.TryGetProperty("string", out var resStr) ? resStr.GetString() : null;
                bool? passed = last.TryGetProperty("self_test_result", out var res2)
                    && res2.TryGetProperty("value", out var resVal) ? resVal.GetInt32() == 0 : null;

                string? description = testType is not null && result is not null
                    ? $"{testType}: {result}"
                    : result ?? runningDescription;

                return new SelfTestStatus(false, PercentRemaining: null, description, passed);
            }

            return new SelfTestStatus(false, null, "Bu disk için self-test kaydı yok.", null);
        }

        return new SelfTestStatus(IsRunning: false, PercentRemaining: null,
            StatusDescription: "Bu disk self-test durumu raporlamıyor.", Passed: null);
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

    private static int? TryGetNvmeInt(JsonElement root, string prop)
        => root.TryGetProperty("nvme_smart_health_information_log", out var log)
           && log.TryGetProperty(prop, out var p)
            ? p.GetInt32()
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
                    // nsid (ad alanı no) -1 ise bu diskte/ortamda anlamsızdır; satırı hiç ekleme.
                    if (prop.Name.Equals("nsid", StringComparison.OrdinalIgnoreCase)
                        && prop.Value.TryGetInt64(out long nsid) && nsid == -1)
                    {
                        continue;
                    }

                    // critical_warning=0 uyarı yok demektir; bu bilgi zaten Teşhis sayfasında
                    // (CriticalWarningFlags) ayrıntılı gösteriliyor, tabloda gürültü yapmasın.
                    if (prop.Name.Equals("critical_warning", StringComparison.OrdinalIgnoreCase)
                        && prop.Value.TryGetInt64(out long warning) && warning == 0)
                    {
                        continue;
                    }

                    list.Add(new SmartAttribute(
                        Id: "-",
                        Name: prop.Name,
                        RawValue: prop.Value.GetRawText()));
                }
            }
        }

        return list;
    }

    /// <summary>
    /// NVMe SMART/Health log'undaki "critical_warning" bit alanını (NVMe spesifikasyonunun
    /// standart 5 bitlik kritik uyarı bayrağı) Türkçe açıklamalara çözümler.
    /// </summary>
    private static IReadOnlyList<string> ExtractCriticalWarningFlags(JsonElement root)
    {
        if (!root.TryGetProperty("nvme_smart_health_information_log", out var log)
            || !log.TryGetProperty("critical_warning", out var warn))
        {
            return [];
        }

        int bits = warn.GetInt32();
        var flags = new List<string>();
        if ((bits & 0x01) != 0) flags.Add("Kullanılabilir yedek alanı eşiğin altına düştü");
        if ((bits & 0x02) != 0) flags.Add("Sıcaklık eşik dışında");
        if ((bits & 0x04) != 0) flags.Add("Cihaz güvenilirliği düşürüldü (aşırı hata)");
        if ((bits & 0x08) != 0) flags.Add("Ortam salt-okunur moda alındı");
        if ((bits & 0x10) != 0) flags.Add("Yedekleme (volatile memory backup) cihazı arızalandı");
        return flags;
    }

    // ---- Cihaz tespiti fallback ----

    /// <summary>
    /// SMART okuma/self-test başlatma/self-test durumu sorgulama — üçü de aynı cihazı bulmak
    /// zorunda, bu yüzden çözümleme burada tek bir yerde yapılır. Önce disk.DevicePath (Windows'un
    /// doğal "\\.\PHYSICALDRIVEn" yolu) ile "-a --json=c" denenir. Bazı NVMe denetleyicilerinde
    /// smartctl bu yoldan cihaz tipini tanıyamıyor (ör. Kingston SNV2S1000G): exit kodu bunu
    /// "device open failed" olarak işaretlemiyor, JSON geçerli dönüyor ama "device" alanı hiç yok;
    /// sonuç olarak SMART/self-test verileri sessizce boş kalıyor. Bu durumda smartctl'in kendi
    /// "--scan" çıktısındaki gerçek cihaz adını/tipini (ör. "/dev/sda -d nvme") kullanıp seri
    /// numarasıyla eşleştirerek yeniden denenir. Döndürülen Stdout/ExitCode/Stderr, çözümleme
    /// sırasında zaten çalıştırılmış olan "-a --json=c" çağrısına aittir (GetSelfTestStatusAsync
    /// ve ReadHealthAsync bunu ekstra bir smartctl çağrısı yapmadan doğrudan kullanır); Args ise
    /// StartSelfTestAsync'in "-t short/long" çağrısında cihazı hedeflemek için kullanılır.
    /// </summary>
    private async Task<(string[] Args, int ExitCode, string Stdout, string Stderr)> ResolveDeviceAsync(
        DiskInfo disk, CancellationToken ct)
    {
        string[] directArgs = [disk.DevicePath];
        var (exitCode, stdout, stderr) = await RunAsync(["-a", "--json=c", disk.DevicePath], ct);

        if ((exitCode & 0x02) == 0 && HasDeviceInfo(stdout))
            return (directArgs, exitCode, stdout, stderr);

        var scanned = await FindScanDeviceAsync(disk, ct);
        if (scanned is { } found)
            return (found.Args, 0, found.Json, "");

        return (directArgs, exitCode, stdout, stderr);
    }

    private static bool HasDeviceInfo(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("device", out _);
    }

    /// <summary>
    /// smartctl'in "--scan" ile bulduğu gerçek cihaz adını/tipini arar. Birden fazla disk
    /// taranırsa seri numarasıyla eşleştirme yapılır; eşleşme yoksa ve tarama tek bir aday
    /// gösteriyorsa (yaygın tek diskli senaryo) o kullanılır, aksi halde belirsizlik nedeniyle
    /// null döner.
    /// </summary>
    private async Task<(string[] Args, string Json)?> FindScanDeviceAsync(DiskInfo disk, CancellationToken ct)
    {
        var (_, scanOut, _) = await RunAsync(["--scan"], ct);

        var candidates = new List<(string Device, string Type)>();
        foreach (var line in scanOut.Split('\n'))
        {
            var m = ScanLineRegex.Match(line);
            if (m.Success)
                candidates.Add((m.Groups[1].Value, m.Groups[2].Value));
        }

        (string[] Args, string Json)? firstValid = null;
        foreach (var (device, type) in candidates)
        {
            ct.ThrowIfCancellationRequested();

            string[] args = ["-d", type, device];
            var (exit, json, _) = await RunAsync(["-a", "--json=c", ..args], ct);
            if ((exit & 0x02) != 0 || !HasDeviceInfo(json))
                continue;

            if (MatchesDisk(json, disk))
                return (args, json);

            firstValid ??= (args, json);
        }

        return candidates.Count == 1 ? firstValid : null;
    }

    /// <summary>
    /// Bir smartctl "--scan" adayının, WMI'dan gelen DiskInfo ile aynı fiziksel diski temsil
    /// edip etmediğini belirler. ÖNCE seri no denenir (varsa), AMA seri no WMI ile smartctl
    /// arasında birebir eşleşmeyebilir — gerçek bir NVMe sürücüde (Kingston SNV2S1000G)
    /// doğrulandı: WMI `Win32_DiskDrive.SerialNumber` = "0000_0000_0000_0000_0026_B778_58A7_DF35."
    /// (20 bayt, alt tire ayraçlı, ham NVMe SN alanının farklı bir kodlaması), smartctl
    /// `serial_number` = "50026B77858A7DF3" — AYNI DİSK, hiçbir normalizasyonla (trim/case/
    /// tire temizleme) eşleşmeyen tamamen farklı biçimler. Bu yüzden seri no eşleşmezse
    /// model adı + kapasite kombinasyonuna düşülür — bu ikisi gerçek makinede sırasıyla
    /// birebir ("KINGSTON SNV2S1000G" == "KINGSTON SNV2S1000G") ve ~%99,9997 yakın
    /// (WMI 1.000.202.273.280 bayt / smartctl 1.000.204.886.016 bayt, ~2,6 MB fark —
    /// mantıksal/hizalama farkı) çıktı, seri no'dan çok daha güvenilir.
    /// </summary>
    public static bool MatchesDisk(string json, DiskInfo disk)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string? candidateSerial = root.TryGetProperty("serial_number", out var sn) ? sn.GetString() : null;
        if (!string.IsNullOrWhiteSpace(disk.SerialNumber) && !string.IsNullOrWhiteSpace(candidateSerial)
            && NormalizeIdentifier(candidateSerial) == NormalizeIdentifier(disk.SerialNumber))
        {
            return true;
        }

        string? candidateModel = root.TryGetProperty("model_name", out var mn) ? mn.GetString() : null;
        bool modelMatches = !string.IsNullOrWhiteSpace(candidateModel) && !string.IsNullOrWhiteSpace(disk.ModelName)
            && IdentifiersOverlap(NormalizeIdentifier(candidateModel), NormalizeIdentifier(disk.ModelName));

        long? candidateCapacity = root.TryGetProperty("user_capacity", out var uc) && uc.TryGetProperty("bytes", out var b)
            ? b.GetInt64()
            : null;
        bool capacityMatches = candidateCapacity is { } cap && disk.CapacityBytes > 0
            && CapacityRoughlyMatches(cap, disk.CapacityBytes);

        return modelMatches && capacityMatches;
    }

    /// <summary>Harf/rakam olmayan her şeyi (boşluk, alt tire, tire, nokta) kaldırıp büyük
    /// harfe çevirir — WMI/smartctl'in aynı kimliği farklı ayraç/biçimle döndürdüğü
    /// durumlarda karşılaştırmayı mümkün kılar.</summary>
    private static string NormalizeIdentifier(string value)
        => new([.. value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant)]);

    private static bool IdentifiersOverlap(string a, string b)
        => a.Length > 0 && b.Length > 0 && (a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal));

    /// <summary>WMI ve smartctl'in aynı disk için bildirdiği kapasite birebir aynı olmayabilir
    /// (gerçek makinede doğrulandı, bkz. MatchesDisk'in belgesi) — %1'lik bir tolerans içinde
    /// eşleşme kabul edilir.</summary>
    private static bool CapacityRoughlyMatches(long a, long b)
    {
        if (a <= 0 || b <= 0) return false;
        double ratio = (double)Math.Min(a, b) / Math.Max(a, b);
        return ratio >= 0.99;
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
        try
        {
            proc.Start();
        }
        catch (Win32Exception ex)
        {
            // SORUN 5 (v1.0.0 gerçek kullanıcı raporu): smartctl.exe fiziksel olarak yoksa
            // (veya PATH'te bulunamıyorsa) burada Win32Exception fırlar; eskiden ham,
            // İngilizce OS mesajı ("The system cannot find the file specified") kullanıcıya
            // hiçbir açıklama yapmadan HealthViewModel.Error'a düşüyordu. SORUN 3'ün
            // açılış uyarısıyla aynı, anlaşılır Türkçe mesaja bağlanıyor.
            throw new InvalidOperationException(
                "smartctl bulunamadı. SMART disk sağlığı verisi bu eklenti olmadan okunamaz " +
                "— uygulama açılışındaki uyarıya bakın veya README.md'deki kurulum adımını izleyin.",
                ex);
        }

        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        return (proc.ExitCode, await stdoutTask, await stderrTask);
    }
}
