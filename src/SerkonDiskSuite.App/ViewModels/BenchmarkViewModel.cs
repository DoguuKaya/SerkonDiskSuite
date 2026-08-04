using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SerkonDiskSuite.Core.Interfaces;
using SerkonDiskSuite.Core.Models;

namespace SerkonDiskSuite.App.ViewModels;

/// <summary>Seçili disk için benchmark testini yöneten sekme.</summary>
public partial class BenchmarkViewModel : ObservableObject
{
    private readonly IBenchmarkRunner _runner;
    private DiskInfo? _disk;
    private CancellationTokenSource? _cts;

    [ObservableProperty] private ObservableCollection<BenchmarkResult> _results = [];
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private string? _progressMessage;
    [ObservableProperty] private string? _targetDrive;

    // Kullanıcı ayarları
    [ObservableProperty] private int _testSizeGiB = 1;
    [ObservableProperty] private int _passes = 3;
    [ObservableProperty] private int _randomBlockSizeKiB = 4;
    [ObservableProperty] private BenchmarkProfile? _selectedProfile;

    /// <summary>Rastgele okuma/yazma testlerinde seçilebilecek blok boyutları (KiB).</summary>
    public IReadOnlyList<int> BlockSizeOptionsKiB { get; } = [4, 8, 16, 32, 64, 128, 256, 512, 1024];

    /// <summary>Hazır CrystalDiskMark profilleri (ör. "SEQ1M Q8T1"). Seçilmezse manuel/"Özel"
    /// ayarlar (TestSizeGiB, Passes, RandomBlockSizeKiB) kullanılır.</summary>
    public IReadOnlyList<BenchmarkProfile> Profiles { get; } = BenchmarkProfiles.All;

    public BenchmarkViewModel(IBenchmarkRunner runner) => _runner = runner;

    public void SetDisk(DiskInfo? disk)
    {
        _disk = disk;
        Results.Clear();
        // Benchmark bir sürücü harfi gerektirir (ör. "S:\").
        TargetDrive = disk?.DriveLetters.FirstOrDefault() is { } l ? $"{l}\\" : null;
    }

    private bool CanRun => !IsRunning && !string.IsNullOrEmpty(TargetDrive);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task StartAsync()
    {
        if (string.IsNullOrEmpty(TargetDrive)) return;

        IsRunning = true;
        Results.Clear();
        ProgressPercent = 0;
        _cts = new CancellationTokenSource();

        var options = new BenchmarkOptions
        {
            TargetPath = TargetDrive,
            TestFileSizeBytes = (long)TestSizeGiB * 1024 * 1024 * 1024,
            Passes = Passes,
            RandomBlockSize = RandomBlockSizeKiB * 1024
        };
        if (SelectedProfile is { } profile)
        {
            options = BenchmarkProfiles.Apply(options, profile);
        }

        var progress = new Progress<BenchmarkProgress>(p =>
        {
            ProgressMessage = p.StatusMessage;
            ProgressPercent = p.PercentComplete;
        });

        try
        {
            var results = await _runner.RunAsync(options, progress, _cts.Token);
            Results = new ObservableCollection<BenchmarkResult>(results);
            ProgressMessage = "Test tamamlandı.";
            ProgressPercent = 100;
        }
        catch (OperationCanceledException)
        {
            ProgressMessage = "Test iptal edildi.";
        }
        catch (Exception ex)
        {
            ProgressMessage = $"Hata: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();
}
