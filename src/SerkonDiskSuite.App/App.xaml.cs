using System.Diagnostics;
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

        WarnIfSmartctlMissing();

        var services = new ServiceCollection();
        ConfigureServices(services);
        _services = services.BuildServiceProvider();

        var window = _services.GetRequiredService<MainWindow>();
        window.Show();
    }

    /// <summary>
    /// SORUN 3 (v1.0.0 gerçek kullanıcı raporu): eski uyarı metni çok teknikti
    /// ("smartctl.exe bulunamadı") ve kullanıcılar bunu "program bozuk" sanıp ne
    /// yapacaklarını bilemedi. smartctl.exe GPL-2.0 lisanslı olduğundan yükleyici
    /// (installer) onu paketlemiyor — bu turda GPL-2.0'ın "mere aggregation" maddesi
    /// (gnu.org/licenses/gpl-2.0-faq: ayrı, birbirine bağlanmamış programların aynı
    /// dağıtım ortamında bulunması lisansı etkilemez) araştırıldı ve smartctl.exe'yi
    /// AYRI bir süreç olarak çalıştırdığımız için (linklemiyoruz) paketlemek muhtemelen
    /// yasal olarak mümkün — ANCAK bu, GPL lisans metnini kurulum paketine dahil etmeyi
    /// ve tam kaynak/sürüm izlenebilirliğini gerektiriyor (bu turda henüz yapılmadı).
    /// Otomatik indirme de değerlendirildi: smartmontools resmi GitHub Releases'inde
    /// (github.com/smartmontools/smartmontools/releases) yalnızca bir NSIS kurulum
    /// programı (.win32-setup.exe) var, taşınabilir/tek-exe bir zip YOK — bu yüzden
    /// otomatik indirip sessizce kurmak, üçüncü taraf bir kurulum programını sessiz
    /// bayraklarla (/S /D=) çalıştırmayı gerektirir; bu turda bu yaklaşımın kırılganlığı
    /// (sürüm değişince bayraklar/davranış değişebilir, AV/SmartScreen uyarısı riski)
    /// nedeniyle ERTELENDİ. Bu turda uygulanan: kullanıcıyı resmi indirme sayfasına
    /// TEK TIKLA yönlendiren, "bu bir eklenti, bozuk değil" hissini veren daha anlaşılır
    /// bir uyarı. WPF-UI'nin özel butonlu MessageBox'ı (Content=Window alt sınıfı)
    /// değerlendirildi ama gerçek MainWindow'dan ÖNCE gösterildiği için
    /// Application.MainWindow/ShutdownMode etkileşimi riski taşıyor ve bu ajan
    /// oturumunda interaktif UI testi yapılamadığından, zaten kanıtlanmış/güvenli olan
    /// standart System.Windows.MessageBox (YesNo) tercih edildi.
    /// </summary>
    private static void WarnIfSmartctlMissing()
    {
        string smartctlPath = Path.Combine(AppContext.BaseDirectory, "tools", "smartctl.exe");
        if (File.Exists(smartctlPath))
        {
            return;
        }

        var result = MessageBox.Show(
            "Serkon Disk Suite'in disk sağlığı (SMART) özelliğini kullanabilmesi için " +
            "\"smartmontools\" adlı ücretsiz, açık kaynaklı bir eklentiye ihtiyacı var. " +
            "Bu bir hata veya bozuk kurulum DEĞİL — lisans farkı nedeniyle eklenti " +
            "uygulamanın kurulumuna dahil edilemiyor, ayrıca indirmeniz gerekiyor.\n\n" +
            "Bu eklenti olmadan disk listesi ve benchmark özellikleri her zamanki gibi " +
            "çalışır; yalnızca SMART sağlık verileri (sıcaklık, kalan ömür vb.) boş kalır.\n\n" +
            "Şimdi indirme sayfasını açmak için EVET'e basın. Kurulum dosyasını " +
            "çalıştırdıktan sonra smartctl.exe (ve yanındaki dosyaları) bu uygulamanın " +
            "klasöründeki \"tools\" alt klasörüne kopyalamanız yeterli (ayrıntı: README.md).",
            "Serkon Disk Suite — isteğe bağlı bir eklenti eksik",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);

        if (result == MessageBoxResult.Yes)
        {
            OpenSmartctlDownloadPage();
        }
    }

    private static void OpenSmartctlDownloadPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/smartmontools/smartmontools/releases/latest",
                UseShellExecute = true,
            });
        }
        catch
        {
            // Tarayıcı açılamazsa (ör. hiçbir varsayılan tarayıcı kayıtlı değilse) sessizce
            // yut — kullanıcı README.md'deki adresi elle de ziyaret edebilir.
        }
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
        services.AddSingleton<IVbsStatusProvider, WmiVbsStatusProvider>();
        services.AddSingleton<ISmartTrendStore>(_ => new JsonSmartTrendStore());
        services.AddSingleton<IHardwareTrendStore>(_ => new JsonHardwareTrendStore());

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
