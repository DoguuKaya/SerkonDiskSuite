using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using SerkonDiskSuite.App.ViewModels;
using SerkonDiskSuite.App.Views;
using SerkonDiskSuite.App.Views.Pages;
using SerkonDiskSuite.Core.Interfaces;
using SerkonDiskSuite.Infrastructure.Benchmark;
using SerkonDiskSuite.Infrastructure.Hardware;
using SerkonDiskSuite.Infrastructure.Smart;
using SerkonDiskSuite.Infrastructure.SystemInfo;
using SerkonDiskSuite.Infrastructure.Trend;
using SerkonDiskSuite.Infrastructure.Wmi;

namespace SerkonDiskSuite.App;

public partial class App : Application
{
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SerkonDiskSuite", "logs");

    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        var services = new ServiceCollection();
        ConfigureServices(services);
        _services = services.BuildServiceProvider();

        var window = _services.GetRequiredService<MainWindow>();
        window.Show();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        HandleUnhandledException(e.Exception, "DispatcherUnhandledException");
        e.Handled = true;
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            HandleUnhandledException(ex, "AppDomain.UnhandledException");
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        HandleUnhandledException(e.Exception, "TaskScheduler.UnobservedTaskException");
        e.SetObserved();
    }

    private static void HandleUnhandledException(Exception exception, string source)
    {
        string? logPath = null;
        try
        {
            Directory.CreateDirectory(LogDirectory);
            logPath = Path.Combine(LogDirectory, $"crash-{DateTime.Now:yyyyMMdd-HHmmss-fff}.log");
            File.WriteAllText(logPath, $"[{DateTime.Now:O}] {source}{Environment.NewLine}{exception}");
        }
        catch
        {
            // Loglama başarısız olsa bile kullanıcıya hata penceresi gösterilmeye devam edilmeli.
        }

        string message = logPath is not null
            ? $"Beklenmeyen bir hata oluştu ve uygulama bu işlemi tamamlayamadı.\n\nHata kaydı: {logPath}\n\n{exception.Message}"
            : $"Beklenmeyen bir hata oluştu.\n\n{exception.Message}";

        MessageBox.Show(message, "Serkon Disk Suite — Hata", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // smartctl uygulama ile birlikte tools/ altında dağıtılır.
        string smartctlPath = Path.Combine(
            AppContext.BaseDirectory, "tools", "smartctl.exe");

        // Altyapı servisleri (tek örnek yeterli)
        services.AddSingleton<IDiskProvider, WmiDiskProvider>();
        services.AddSingleton<ISmartProvider>(_ => new SmartctlSmartProvider(
            File.Exists(smartctlPath) ? smartctlPath : null));
        services.AddSingleton<IBenchmarkRunner, DiskBenchmarkRunner>();
        services.AddSingleton<ISystemInfoProvider, WmiSystemInfoProvider>();
        services.AddSingleton<IHardwareMonitorProvider, LibreHardwareMonitorProvider>();
        services.AddSingleton<ISmartTrendStore>(_ => new JsonSmartTrendStore());

        // ViewModel'ler
        services.AddSingleton<HealthViewModel>();
        services.AddSingleton<BenchmarkViewModel>();
        services.AddSingleton<SystemViewModel>();
        services.AddSingleton<DiagnosticsViewModel>();
        services.AddSingleton<MainViewModel>();

        // NavigationView sayfaları (ui:NavigationViewItem.TargetPageType üzerinden
        // NavigationView.SetServiceProvider ile DI konteynerinden çözülür)
        services.AddSingleton<HealthPage>();
        services.AddSingleton<BenchmarkPage>();
        services.AddSingleton<SystemPage>();
        services.AddSingleton<DiagnosticsPage>();

        // Pencereler
        services.AddSingleton<MainWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }
}
