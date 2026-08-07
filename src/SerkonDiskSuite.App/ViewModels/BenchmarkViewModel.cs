using System.Collections.ObjectModel;
using System.IO;
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

    /// <summary>Windows'un yüklü olduğu sürücü kökü (ör. "C:") — bu sürücü test için seçilirse uyarı gösterilir.</summary>
    private static readonly string SystemDriveLetter =
        (Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\").TrimEnd('\\');

    [ObservableProperty] private ObservableCollection<BenchmarkResult> _results = [];
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private string? _progressMessage;
    [ObservableProperty] private string? _targetDrive;
    [ObservableProperty] private ObservableCollection<string> _availableDriveLetters = [];
    [ObservableProperty] private string? _selectedDriveLetter;
    [ObservableProperty] private bool _isSystemDriveSelected;

    // Kullanıcı ayarları
    [ObservableProperty] private int _testSizeGiB = 1;
    [ObservableProperty] private int _passes = 3;
    [ObservableProperty] private int _randomBlockSizeKiB = 4;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSequentialLocked))]
    [NotifyPropertyChangedFor(nameof(IsRandomLocked))]
    private BenchmarkProfile _selectedProfile = BenchmarkProfiles.Custom;

    // Madde C1: sıralı ve rastgele testler artık AYRI Q/T taşıyor (gerçek CrystalDiskMark'ta
    // "SEQ1M Q8T1" yalnızca sıralı, "RND4K Q32T16" yalnızca rastgele testleri etkiler).
    [ObservableProperty] private int _sequentialQueueDepth = 1;
    [ObservableProperty] private int _sequentialThreadCount = 1;
    [ObservableProperty] private int _randomQueueDepth = 1;
    [ObservableProperty] private int _randomThreadCount = 1;

    /// <summary>Madde C2: bir profil seçiliyken, o profilin belirlediği kategorinin (sıralı ya
    /// da rastgele) alanları kullanıcıya salt-okunur görünür — hangi ayarın profilden, hangisinin
    /// elle girildiğinden geldiği belirsiz kalmasın.</summary>
    public bool IsSequentialLocked => !ReferenceEquals(SelectedProfile, BenchmarkProfiles.Custom) && !SelectedProfile.IsRandom;
    public bool IsRandomLocked => !ReferenceEquals(SelectedProfile, BenchmarkProfiles.Custom) && SelectedProfile.IsRandom;

    /// <summary>Rastgele okuma/yazma testlerinde seçilebilecek blok boyutları (KiB).</summary>
    public IReadOnlyList<int> BlockSizeOptionsKiB { get; } = [4, 8, 16, 32, 64, 128, 256, 512, 1024];

    /// <summary>Hazır CrystalDiskMark profilleri + başta "Özel" (manuel ayarlar) seçeneği —
    /// ComboBox'ın boş başlamaması için varsayılan olarak "Özel" seçili gelir.</summary>
    public IReadOnlyList<BenchmarkProfile> Profiles { get; } = [BenchmarkProfiles.Custom, .. BenchmarkProfiles.All];

    public BenchmarkViewModel(IBenchmarkRunner runner) => _runner = runner;

    public void SetDisk(DiskInfo? disk)
    {
        _disk = disk;
        Results.Clear();
        AvailableDriveLetters = new ObservableCollection<string>(disk?.DriveLetters ?? []);
        // Benchmark bir sürücü harfi gerektirir (ör. "S:\"); varsayılan olarak sistem diski
        // OLMAYAN ilk harf seçilir (Windows'un yüklü olduğu sürücüyü test etmek riskli olduğundan
        // kullanıcıyı hemen bir uyarıyla karşılaştırmamak için) — yalnızca disk üzerinde sistem
        // sürücüsünden başka harf yoksa sistem sürücüsüne düşülür (bu durumda uyarı zaten haklı).
        SelectedDriveLetter = AvailableDriveLetters.FirstOrDefault(l => !IsSystemDriveLetter(l))
            ?? AvailableDriveLetters.FirstOrDefault();
    }

    private static bool IsSystemDriveLetter(string letter)
        => string.Equals(letter.TrimEnd('\\'), SystemDriveLetter, StringComparison.OrdinalIgnoreCase);

    partial void OnSelectedDriveLetterChanged(string? value)
    {
        TargetDrive = value is { } l ? $"{l}\\" : null;
        IsSystemDriveSelected = value is not null && IsSystemDriveLetter(value);
    }

    /// <summary>Profil seçildiğinde ilgili kategorinin Q/T (ve rastgeleyse blok boyutu) alanlarını
    /// UI'da da günceller — kullanıcı hangi değerin uygulanacağını görür, elle değiştiremez
    /// (IsSequentialLocked/IsRandomLocked ile salt-okunur yapılır, bkz. madde C2).</summary>
    partial void OnSelectedProfileChanged(BenchmarkProfile value)
    {
        if (ReferenceEquals(value, BenchmarkProfiles.Custom)) return;

        if (value.IsRandom)
        {
            RandomQueueDepth = value.QueueDepth;
            RandomThreadCount = value.ThreadCount;
            RandomBlockSizeKiB = value.BlockSize / 1024;
        }
        else
        {
            SequentialQueueDepth = value.QueueDepth;
            SequentialThreadCount = value.ThreadCount;
        }
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
            RandomBlockSize = RandomBlockSizeKiB * 1024,
            SequentialQueueDepth = SequentialQueueDepth,
            SequentialThreadCount = SequentialThreadCount,
            RandomQueueDepth = RandomQueueDepth,
            RandomThreadCount = RandomThreadCount,
        };
        if (!ReferenceEquals(SelectedProfile, BenchmarkProfiles.Custom))
        {
            options = BenchmarkProfiles.Apply(options, SelectedProfile);
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
