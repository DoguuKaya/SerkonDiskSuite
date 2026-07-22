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
            Passes = Passes
        };

        var progress = new Progress<BenchmarkProgress>(p =>
        {
            ProgressMessage = p.StatusMessage;
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
