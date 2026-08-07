using System.Text.Json;
using SerkonDiskSuite.Core.Interfaces;
using SerkonDiskSuite.Core.Models;

namespace SerkonDiskSuite.Infrastructure.Trend;

/// <summary>
/// SMART trend noktalarını disk başına bir JSON dosyasında saklar
/// (varsayılan konum: %LOCALAPPDATA%\SerkonDiskSuite\trend\&lt;disk anahtarı&gt;.json).
/// Dosya boyutunu sınırlı tutmak için her yazımda en eski noktalar budanır.
/// </summary>
public sealed class JsonSmartTrendStore : ISmartTrendStore
{
    private const int MaxStoredPoints = 20_000;

    // Kullanıcının aynı anda birden fazla (genellikle yönetici hakkıyla açılmış, kapatılamayan
    // eski) SerkonDiskSuite.exe sürecine sahip olması bu ortamda alışılmış bir durum (bkz.
    // PROGRESS.md). Eski bir in-process SemaphoreSlim yalnızca AYNI süreç içindeki eşzamanlılığı
    // korur; SÜREÇLER ARASI korumayı sağlamaz. İki süreç aynı anda dosyayı oku-değiştir-yaz
    // yaparsa klasik "lost update" oluşur: geç kaydeden, diğerinin eklediği tüm yeni noktaları
    // (ve bazen daha eski geçmişi) sessizce siler — tam olarak gözlemlenen "geçmiş sıfırlandı"
    // belirtisi. Düzeltme: dosyayı `FileShare.None` ile açıp okuma+değiştirme+yazmanın TAMAMINI
    // tek bir işletim sistemi seviyesi özel kilit altında tutmak; bu, aynı süreç içindeki
    // eşzamanlılığı da otomatik olarak kapsadığından ayrı bir in-process kilide gerek kalmıyor.
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(50);
    // Gerçek kullanımda tek bir disk için ~5 sn'de bir çağrı olur, bu yüzden cömert bir üst
    // sınır (yaklaşık 15 sn) güvenli: normal senaryoda birkaç denemeden fazla sürmez, ama
    // (test paketindeki gibi) çok sayıda sürecin GERÇEKTEN aynı anda yarıştığı uç durumlarda
    // erken pes edip veri kaybına yol açmak yerine sırasını beklemeyi tercih eder.
    private const int MaxOpenAttempts = 300;

    private readonly string _baseDirectory;

    public JsonSmartTrendStore(string? baseDirectory = null)
    {
        _baseDirectory = baseDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SerkonDiskSuite", "trend");
    }

    public async Task<IReadOnlyList<SmartTrendPoint>> LoadAsync(string diskKey, CancellationToken ct = default)
    {
        var path = GetFilePath(diskKey);
        if (!File.Exists(path))
            return [];

        await using var stream = await OpenExclusiveAsync(path, ct);
        if (stream.Length == 0)
            return [];

        var points = await JsonSerializer.DeserializeAsync<List<SmartTrendPoint>>(stream, cancellationToken: ct);
        return points ?? [];
    }

    public async Task AppendAsync(string diskKey, SmartTrendPoint point, CancellationToken ct = default)
    {
        var path = GetFilePath(diskKey);
        Directory.CreateDirectory(_baseDirectory);

        await using var stream = await OpenExclusiveAsync(path, ct);
        List<SmartTrendPoint> points = stream.Length > 0
            ? await JsonSerializer.DeserializeAsync<List<SmartTrendPoint>>(stream, cancellationToken: ct) ?? []
            : [];

        points.Add(point);
        if (points.Count > MaxStoredPoints)
            points.RemoveRange(0, points.Count - MaxStoredPoints);

        stream.SetLength(0);
        stream.Position = 0;
        await JsonSerializer.SerializeAsync(stream, points, cancellationToken: ct);
        await stream.FlushAsync(ct);
    }

    /// <summary>
    /// Dosyayı <see cref="FileShare.None"/> ile açar: aynı anda başka bir süreç (ya da aynı
    /// süreçteki başka bir çağrı) aynı dosyayı açmaya çalışırsa <see cref="IOException"/> alır.
    /// Kısa bir aralıkla yeniden denenir; bu, sürece özgü bir kilit yerine işletim sisteminin
    /// kendi karşılıklı dışlama mekanizmasını kullanarak süreçler arası güvenliği sağlar.
    /// </summary>
    private static async Task<FileStream> OpenExclusiveAsync(string path, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (attempt < MaxOpenAttempts)
            {
                await Task.Delay(RetryDelay, ct);
            }
        }
    }

    private string GetFilePath(string diskKey)
    {
        var sanitized = new string(diskKey.Where(char.IsLetterOrDigit).ToArray());
        if (string.IsNullOrEmpty(sanitized))
            sanitized = "unknown";
        return Path.Combine(_baseDirectory, $"{sanitized}.json");
    }
}
