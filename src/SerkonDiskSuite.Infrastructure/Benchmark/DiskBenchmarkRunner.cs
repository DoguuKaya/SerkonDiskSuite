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

            var (throughput, iops, duration) = await Task.Run(
                () => RunSinglePass(kind, filePath, options, ct, ReportProgress), ct);

            if (throughput > bestThroughput)
            {
                bestThroughput = throughput;
                bestIops = iops;
                bestDuration = duration;
            }
        }

        bool isRandom = kind is BenchmarkTestKind.RandomRead or BenchmarkTestKind.RandomWrite;
        return new BenchmarkResult(kind, bestThroughput, isRandom ? bestIops : null, bestDuration);
    }

    private static (double ThroughputMBps, double Iops, TimeSpan Duration) RunSinglePass(
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

        var buffer = AllocateAligned(blockSize);
        FillRandom(buffer);

        var access = isWrite ? FileAccess.Write : FileAccess.Read;
        // Preallocation yalnizca dosyayi (yeniden) olusturan modlarla (Create/CreateNew/Truncate)
        // kullanilabilir; OpenOrCreate veya Open ile preallocationSize>0 verilirse ArgumentException
        // firlar. Yazma gecisleri her seferinde dosyayi tazeden olusturur, okuma gecisleri var olan
        // dosyayi acar ve preallocation istemez.
        var mode = isWrite ? FileMode.Create : FileMode.Open;
        var opts = NoBuffering | FileOptions.WriteThrough;
        long preallocationSize = isWrite ? fileSize : 0;

        using var handle = File.OpenHandle(filePath, mode, access, FileShare.None, opts, preallocationSize);

        var rng = new Random(12345); // deterministik erişim deseni
        var sw = Stopwatch.StartNew();
        long bytesProcessed = 0;

        // Geçiş başına ~50 ilerleme bildirimi yeterli; her bloğu bildirmek gereksiz UI güncellemesine yol açar.
        int reportEvery = Math.Max(1, totalBlocks / 50);

        for (int i = 0; i < totalBlocks; i++)
        {
            ct.ThrowIfCancellationRequested();

            long offset = isRandom
                ? (long)rng.Next(0, totalBlocks) * blockSize
                : (long)i * blockSize;

            if (isWrite)
                RandomAccess.Write(handle, buffer, offset);
            else
                RandomAccess.Read(handle, buffer, offset);

            bytesProcessed += blockSize;

            if (onProgress is not null && (i % reportEvery == 0 || i == totalBlocks - 1))
                onProgress((double)(i + 1) / totalBlocks);
        }

        sw.Stop();

        double seconds = sw.Elapsed.TotalSeconds;
        double throughputMBps = seconds > 0 ? bytesProcessed / seconds / (1024 * 1024) : 0;
        double iops = seconds > 0 ? totalBlocks / seconds : 0;

        return (throughputMBps, iops, sw.Elapsed);
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
