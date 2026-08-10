using System.Text.Json;
using SerkonDiskSuite.Core.Interfaces;
using SerkonDiskSuite.Core.Models;

namespace SerkonDiskSuite.Infrastructure.Trend;

/// <summary>
/// CPU/GPU trend noktalarını tek bir JSON dosyasında saklar
/// (varsayılan konum: %LOCALAPPDATA%\SerkonDiskSuite\trend\hardware.json). Dosya boyutunu
/// sınırlı tutmak için her yazımda en eski noktalar budanır. Süreçler arası güvenli eşzamanlılık
/// için <see cref="JsonSmartTrendStore"/>'daki aynı `FileShare.None` + yeniden deneme deseni
/// kullanılır (madde 40 A2'nin "lost update" düzeltmesiyle aynı gerekçe: kullanıcı genelde
/// birden fazla, kapatılamayan yönetici sürecine sahip olabiliyor).
/// </summary>
public sealed class JsonHardwareTrendStore : IHardwareTrendStore
{
    private const int MaxStoredPoints = 20_000;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(50);
    private const int MaxOpenAttempts = 300;

    private readonly string _filePath;

    public JsonHardwareTrendStore(string? baseDirectory = null)
    {
        var directory = baseDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SerkonDiskSuite", "trend");
        _filePath = Path.Combine(directory, "hardware.json");
    }

    public async Task<IReadOnlyList<HardwareTrendPoint>> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        await using var stream = await OpenExclusiveAsync(_filePath, ct);
        if (stream.Length == 0)
        {
            return [];
        }

        var points = await JsonSerializer.DeserializeAsync<List<HardwareTrendPoint>>(stream, cancellationToken: ct);
        return points ?? [];
    }

    public async Task AppendAsync(HardwareTrendPoint point, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

        await using var stream = await OpenExclusiveAsync(_filePath, ct);
        List<HardwareTrendPoint> points = stream.Length > 0
            ? await JsonSerializer.DeserializeAsync<List<HardwareTrendPoint>>(stream, cancellationToken: ct) ?? []
            : [];

        points.Add(point);
        if (points.Count > MaxStoredPoints)
        {
            points.RemoveRange(0, points.Count - MaxStoredPoints);
        }

        stream.SetLength(0);
        stream.Position = 0;
        await JsonSerializer.SerializeAsync(stream, points, cancellationToken: ct);
        await stream.FlushAsync(ct);
    }

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
}
