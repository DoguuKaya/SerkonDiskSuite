// Serkon Disk Suite uygulama ikonu üretici.
// Tasarım: yuvarlatılmış köşeli mavi (WPF-UI aksan rengiyle uyumlu #2563EB) arka plan
// üzerinde beyaz bir "disk" gövdesi ve onun üzerinden geçen yeşil bir EKG/nabız çizgisi
// (disk + sağlık izleme temasını birleştiren, CrystalDiskInfo'nun disk+kalp ikonuna
// benzer bir dil). Çıktı: çok katmanlı (16/32/48/256) tek bir .ico dosyası.
//
// Çalıştırma: dotnet run --project tools/icon-gen -- <çıktı .ico yolu>
using SkiaSharp;

string outputPath = args.Length > 0
    ? args[0]
    : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
        "src", "SerkonDiskSuite.App", "Assets", "app.ico");
outputPath = Path.GetFullPath(outputPath);

int[] sizes = [16, 32, 48, 256];
var pngLayers = sizes.Select(DrawIcon).ToArray();

Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
WriteIco(outputPath, sizes, pngLayers);
Console.WriteLine($"Yazıldı: {outputPath} ({sizes.Length} katman: {string.Join(", ", sizes)})");

static byte[] DrawIcon(int size)
{
    using var bitmap = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
    using var canvas = new SKCanvas(bitmap);
    canvas.Clear(SKColors.Transparent);

    float margin = size * 0.04f;
    var bounds = new SKRect(margin, margin, size - margin, size - margin);
    float cornerRadius = size * 0.22f;

    using var bgPaint = new SKPaint { Color = new SKColor(0x25, 0x63, 0xEB), IsAntialias = true };
    canvas.DrawRoundRect(bounds, cornerRadius, cornerRadius, bgPaint);

    // Disk gövdesi (beyaz, yuvarlatılmış dikdörtgen — dış hatlarıyla bir SSD/HDD siluetini
    // andırıyor), dikey ortada.
    float diskWidth = size * 0.7f;
    float diskHeight = size * 0.42f;
    var diskRect = new SKRect(
        (size - diskWidth) / 2, (size - diskHeight) / 2,
        (size - diskWidth) / 2 + diskWidth, (size - diskHeight) / 2 + diskHeight);
    using var diskPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
    canvas.DrawRoundRect(diskRect, size * 0.05f, size * 0.05f, diskPaint);

    // EKG/nabız çizgisi: disk gövdesinin üzerinden geçen sağlık izleme sembolü.
    using var pulsePath = new SKPath();
    float midY = diskRect.MidY;
    float x0 = diskRect.Left + diskWidth * 0.08f;
    float x1 = diskRect.Left + diskWidth * 0.32f;
    float x2 = diskRect.Left + diskWidth * 0.42f;
    float x3 = diskRect.Left + diskWidth * 0.52f;
    float x4 = diskRect.Left + diskWidth * 0.62f;
    float x5 = diskRect.Left + diskWidth * 0.92f;
    float spikeTop = diskRect.Top + diskHeight * 0.12f;
    float spikeBottom = diskRect.Bottom - diskHeight * 0.12f;

    pulsePath.MoveTo(x0, midY);
    pulsePath.LineTo(x1, midY);
    pulsePath.LineTo(x2, spikeTop);
    pulsePath.LineTo(x3, spikeBottom);
    pulsePath.LineTo(x4, midY);
    pulsePath.LineTo(x5, midY);

    using var pulsePaint = new SKPaint
    {
        Color = new SKColor(0x22, 0xC5, 0x5E),
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = Math.Max(1f, size * 0.045f),
        StrokeCap = SKStrokeCap.Round,
        StrokeJoin = SKStrokeJoin.Round,
    };
    canvas.DrawPath(pulsePath, pulsePaint);

    using var image = SKImage.FromBitmap(bitmap);
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    return data.ToArray();
}

static void WriteIco(string path, int[] sizes, byte[][] pngLayers)
{
    using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
    using var writer = new BinaryWriter(stream);

    // ICONDIR
    writer.Write((ushort)0); // reserved
    writer.Write((ushort)1); // type: 1 = icon
    writer.Write((ushort)sizes.Length);

    int headerSize = 6 + 16 * sizes.Length;
    int offset = headerSize;

    // ICONDIRENTRY[]
    for (int i = 0; i < sizes.Length; i++)
    {
        byte dim = sizes[i] >= 256 ? (byte)0 : (byte)sizes[i];
        writer.Write(dim); // width
        writer.Write(dim); // height
        writer.Write((byte)0); // color count (PNG: n/a)
        writer.Write((byte)0); // reserved
        writer.Write((ushort)1); // planes
        writer.Write((ushort)32); // bit count
        writer.Write((uint)pngLayers[i].Length); // bytes in resource
        writer.Write((uint)offset); // image offset
        offset += pngLayers[i].Length;
    }

    // Image data (PNG blobs, Windows Vista+ destekler — herhangi bir boyut için geçerlidir).
    foreach (var png in pngLayers)
    {
        writer.Write(png);
    }
}
