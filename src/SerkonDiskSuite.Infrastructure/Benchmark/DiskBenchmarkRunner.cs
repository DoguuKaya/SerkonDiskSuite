using System.Buffers;
using System.Diagnostics;
using Microsoft.Win32.SafeHandles;
using SerkonDiskSuite.Core.Formatting;
using SerkonDiskSuite.Core.Interfaces;
using SerkonDiskSuite.Core.Models;

namespace SerkonDiskSuite.Infrastructure.Benchmark;

/// <summary>
/// Gerçek disk I/O ile benchmark yapan motor. Doğru sonuç için işletim sistemi
/// önbelleğini (cache) atlamaya çalışır: FILE_FLAG_NO_BUFFERING + WRITE_THROUGH.
///
/// Bu sayede CrystalDiskMark benzeri gerçekçi sonuçlar elde edilir; aksi halde
/// RAM cache nedeniyle şişirilmiş (gerçek dışı yüksek) rakamlar görülür.
/// </summary>
public sealed class DiskBenchmarkRunner : IBenchmarkRunner
{
    // Windows: no-buffering için erişimler sektör boyutuna (genelde 512/4096) hizalı olmalı.
    private const int SectorAlignment = 4096;

    // P/Invoke olmadan cache-bypass için FileOptions kombinasyonu.
    // 0x20000000 = FILE_FLAG_NO_BUFFERING (FileOptions'ta doğrudan yok, ekliyoruz).
    private const FileOptions NoBuffering = (FileOptions)0x20000000;

    private static readonly BenchmarkTestKind[] TestKinds =
    [
        BenchmarkTestKind.SequentialWrite,
        BenchmarkTestKind.SequentialRead,
        BenchmarkTestKind.RandomWrite,
        BenchmarkTestKind.RandomRead
    ];

    public async Task<IReadOnlyList<BenchmarkResult>> RunAsync(
        BenchmarkOptions options,
        IProgress<BenchmarkProgress>? progress = null,
        CancellationToken ct = default)
    {
        var results = new List<BenchmarkResult>();
        var tempFile = Path.Combine(options.TargetPath, $".serkonbench_{Guid.NewGuid():N}.tmp");

        try
        {
            // Testler sırayla; her biri "Passes" kez çalışır, en iyi sonuç alınır.
            for (int testIndex = 0; testIndex < TestKinds.Length; testIndex++)
            {
                results.Add(await MeasureBestAsync(
                    TestKinds[testIndex], testIndex, TestKinds.Length, tempFile, options, progress, ct));
            }
        }
        finally
        {
            SafeDelete(tempFile);
        }

        return results;
    }

    private async Task<BenchmarkResult> MeasureBestAsync(
        BenchmarkTestKind kind,
        int testIndex,
        int totalTests,
        string filePath,
        BenchmarkOptions options,
        IProgress<BenchmarkProgress>? progress,
        CancellationToken ct)
    {
        double bestThroughput = 0;
        double bestIops = 0;
        var bestDuration = TimeSpan.MaxValue;
        double totalSteps = totalTests * options.Passes;

        for (int pass = 1; pass <= options.Passes; pass++)
        {
            ct.ThrowIfCancellationRequested();

            int passNumber = pass;
            void ReportProgress(double withinPassFraction)
            {
                double completedSteps = testIndex * options.Passes + (passNumber - 1) + withinPassFraction;
                double percent = Math.Clamp(completedSteps / totalSteps * 100.0, 0, 100);
                progress?.Report(new BenchmarkProgress(
                    kind, passNumber, options.Passes, percent,
                    $"{BenchmarkTestKindLabels.ToTurkish(kind)} çalışıyor (geçiş {passNumber}/{options.Passes})..."));
            }

            ReportProgress(0);

            var (throughput, iops, duration) = await RunSinglePassAsync(kind, filePath, options, ct, ReportProgress);

            if (throughput > bestThroughput)
            {
                bestThroughput = throughput;
                bestIops = iops;
                bestDuration = duration;
            }
        }

        bool isRandom = kind is BenchmarkTestKind.RandomRead or BenchmarkTestKind.RandomWrite;
        return new BenchmarkResult(
            kind, bestThroughput, isRandom ? bestIops : null, bestDuration,
            options.QueueDepth, options.ThreadCount);
    }

    /// <summary>
    /// Tek bir geçişi çalıştırır. Kuyruk derinliği (queue depth) x iş parçacığı (thread) kadar
    /// eşzamanlı I/O isteğini <see cref="Parallel.ForEachAsync{TSource}"/> ile havada tutar
    /// (gerçek overlapped I/O için handle <see cref="FileOptions.Asynchronous"/> ile açılır).
    /// Rastgele testlerde her blok indeksi için erişilecek ofset, paylaşılan değişebilir durum
    /// olmadan (thread-safe, sıra bağımsız) deterministik bir karma fonksiyonuyla hesaplanır;
    /// böylece eşzamanlı çalışmaya rağmen aynı test her seferinde aynı erişim setini üretir.
    /// </summary>
    private static async Task<(double ThroughputMBps, double Iops, TimeSpan Duration)> RunSinglePassAsync(
        BenchmarkTestKind kind,
        string filePath,
        BenchmarkOptions options,
        CancellationToken ct,
        Action<double>? onProgress)
    {
        bool isWrite = kind is BenchmarkTestKind.SequentialWrite or BenchmarkTestKind.RandomWrite;
        bool isRandom = kind is BenchmarkTestKind.RandomRead or BenchmarkTestKind.RandomWrite;
        int blockSize = isRandom ? options.RandomBlockSize : options.SequentialBlockSize;

        // Blok boyutunu sektöre hizala (no-buffering şartı).
        blockSize = AlignUp(blockSize, SectorAlignment);
        long fileSize = AlignUp(options.TestFileSizeBytes, blockSize);
        int totalBlocks = (int)(fileSize / blockSize);

        // Okuma testinden önce dosyanın var olması gerekir.
        if (!isWrite && !File.Exists(filePath))
            EnsureTestFile(filePath, fileSize, blockSize);

        // Yazma testlerinde tüm eşzamanlı istekler bu salt-okunur, hiç değişmeyen kaynağı
        // paylaşır (thread-safe); okuma testlerinde her istek havuzdan kendi arabelleğini kiralar.
        var sourceBuffer = AllocateAligned(blockSize);
        FillRandom(sourceBuffer);

        var access = isWrite ? FileAccess.Write : FileAccess.Read;
        // Preallocation yalnizca dosyayi (yeniden) olusturan modlarla (Create/CreateNew/Truncate)
        // kullanilabilir; OpenOrCreate veya Open ile preallocationSize>0 verilirse ArgumentException
        // firlar. Yazma gecisleri her seferinde dosyayi tazeden olusturur, okuma gecisleri var olan
        // dosyayi acar ve preallocation istemez.
        var mode = isWrite ? FileMode.Create : FileMode.Open;
        var opts = NoBuffering | FileOptions.WriteThrough | FileOptions.Asynchronous;
        long preallocationSize = isWrite ? fileSize : 0;

        using var handle = File.OpenHandle(filePath, mode, access, FileShare.None, opts, preallocationSize);

        long bytesProcessed = 0;
        long completed = 0;
        int reportEvery = Math.Max(1, totalBlocks / 50);
        int maxConcurrency = Math.Max(1, options.QueueDepth) * Math.Max(1, options.ThreadCount);

        var sw = Stopwatch.StartNew();

        await Parallel.ForEachAsync(
            Enumerable.Range(0, totalBlocks),
            new ParallelOptions { MaxDegreeOfParallelism = maxConcurrency, CancellationToken = ct },
            async (i, token) =>
            {
                long offset = isRandom
                    ? (long)DeterministicRandomBlockIndex(i, totalBlocks) * blockSize
                    : (long)i * blockSize;

                if (isWrite)
                {
                    await RandomAccess.WriteAsync(handle, sourceBuffer, offset, token);
                }
                else
                {
                    var rented = ArrayPool<byte>.Shared.Rent(blockSize);
                    try
                    {
                        await RandomAccess.ReadAsync(handle, rented.AsMemory(0, blockSize), offset, token);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(rented);
                    }
                }

                Interlocked.Add(ref bytesProcessed, blockSize);
                long done = Interlocked.Increment(ref completed);
                if (onProgress is not null && (done % reportEvery == 0 || done == totalBlocks))
                    onProgress((double)done / totalBlocks);
            });

        sw.Stop();

        double seconds = sw.Elapsed.TotalSeconds;
        double throughputMBps = seconds > 0 ? bytesProcessed / seconds / (1024 * 1024) : 0;
        double iops = seconds > 0 ? totalBlocks / seconds : 0;

        return (throughputMBps, iops, sw.Elapsed);
    }

    /// <summary>
    /// Blok indeksinden [0, totalBlocks) aralığında deterministik, paylaşılan durumsuz
    /// (thread-safe) bir "rastgele" indeks üretir (SplitMix64 sonlandırıcısı). Eşzamanlı
    /// isteklerin tamamlanma sırası değişse de her indeks her zaman aynı ofsete erişir.
    /// </summary>
    private static int DeterministicRandomBlockIndex(int index, int totalBlocks)
    {
        const ulong Seed = 0x5EED_C0DEUL;
        ulong x = unchecked((ulong)index + Seed) * 0x9E3779B97F4A7C15UL;
        x ^= x >> 30; x *= 0xBF58476D1CE4E5B9UL;
        x ^= x >> 27; x *= 0x94D049BB133111EBUL;
        x ^= x >> 31;
        return (int)(x % (ulong)totalBlocks);
    }

    private static void EnsureTestFile(string filePath, long fileSize, int blockSize)
    {
        var buffer = AllocateAligned(blockSize);
        FillRandom(buffer);
        var opts = NoBuffering | FileOptions.WriteThrough;
        using var handle = File.OpenHandle(filePath, FileMode.Create, FileAccess.Write, FileShare.None, opts, fileSize);
        for (long offset = 0; offset < fileSize; offset += blockSize)
            RandomAccess.Write(handle, buffer, offset);
    }

    private static int AlignUp(int value, int alignment)
        => (value + alignment - 1) / alignment * alignment;

    private static long AlignUp(long value, int alignment)
        => (value + alignment - 1) / alignment * alignment;

    private static byte[] AllocateAligned(int size) => new byte[size];

    private static void FillRandom(byte[] buffer) => Random.Shared.NextBytes(buffer);

    private static void SafeDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* geçici dosya silinemezse yut */ }
    }
}
