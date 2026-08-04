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

    // Aynı anda birden çok disk için okuma/yazma olabileceğinden tek bir kilitle korunur;
    // yazım sıklığı (disk başına ~5 sn'de bir) düşük olduğundan tek kilit yeterlidir.
    private static readonly SemaphoreSlim FileLock = new(1, 1);

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

        await FileLock.WaitAsync(ct);
        try
        {
            await using var stream = File.OpenRead(path);
            var points = await JsonSerializer.DeserializeAsync<List<SmartTrendPoint>>(stream, cancellationToken: ct);
            return points ?? [];
        }
        finally
        {
            FileLock.Release();
        }
    }

    public async Task AppendAsync(string diskKey, SmartTrendPoint point, CancellationToken ct = default)
    {
        var path = GetFilePath(diskKey);
        Directory.CreateDirectory(_baseDirectory);

        await FileLock.WaitAsync(ct);
        try
        {
            List<SmartTrendPoint> points = [];
            if (File.Exists(path))
            {
                await using var readStream = File.OpenRead(path);
                points = await JsonSerializer.DeserializeAsync<List<SmartTrendPoint>>(readStream, cancellationToken: ct) ?? [];
            }

            points.Add(point);
            if (points.Count > MaxStoredPoints)
                points.RemoveRange(0, points.Count - MaxStoredPoints);

            await using var writeStream = File.Create(path);
            await JsonSerializer.SerializeAsync(writeStream, points, cancellationToken: ct);
        }
        finally
        {
            FileLock.Release();
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
