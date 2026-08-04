using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SerkonDiskSuite.App.ViewModels;
using SerkonDiskSuite.App.Views;
using SerkonDiskSuite.Core.Interfaces;
using SerkonDiskSuite.Infrastructure.Benchmark;
using SerkonDiskSuite.Infrastructure.Smart;
using SerkonDiskSuite.Infrastructure.SystemInfo;
using SerkonDiskSuite.Infrastructure.Trend;
using SerkonDiskSuite.Infrastructure.Wmi;

namespace SerkonDiskSuite.App;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        ConfigureServices(services);
        _services = services.BuildServiceProvider();

        var window = _services.GetRequiredService<MainWindow>();
        window.Show();
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
        services.AddSingleton<ISmartTrendStore>(_ => new JsonSmartTrendStore());

        // ViewModel'ler
        services.AddSingleton<HealthViewModel>();
        services.AddSingleton<BenchmarkViewModel>();
        services.AddSingleton<MainViewModel>();

        // Pencereler
        services.AddSingleton<MainWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }
}
