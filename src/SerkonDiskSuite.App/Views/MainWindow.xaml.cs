using System.Windows;
using SerkonDiskSuite.App.ViewModels;
using SerkonDiskSuite.App.Views.Pages;
using Wpf.Ui.Controls;

namespace SerkonDiskSuite.App.Views;

public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _viewModel;
    private readonly IServiceProvider _serviceProvider;
    private readonly HealthViewModel _healthViewModel;
    private bool _isInitialized;

    public MainWindow(MainViewModel viewModel, IServiceProvider serviceProvider, HealthViewModel healthViewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _serviceProvider = serviceProvider;
        _healthViewModel = healthViewModel;
        DataContext = _viewModel;

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Loaded birden fazla kez tetiklenebilir (ör. pencere gizlenip tekrar gösterilirse);
        // navigasyon kurulumu yalnızca ilk seferde yapılmalı.
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;

        // NavigationView'ın TargetPageType ile işaretlenmiş sayfaları DI konteynerinden
        // (yapıcısına ViewModel enjekte edilerek) çözebilmesi için servis sağlayıcıyı bağla.
        // Navigate, Loaded'dan önce (yapıcıda) çağrılırsa NavigationView henüz şablonunu
        // uygulamamış olur ve iç ContentPresenter null'dır -> NullReferenceException.
        RootNavigation.SetServiceProvider(_serviceProvider);

        RootNavigation.Navigate(typeof(HealthPage), null);

        // WPF-UI'nin INavigationAware.OnNavigatedToAsync'i, kullanıcı bir NavigationViewItem'a
        // tıkladığında güvenilir çalışıyor (madde 14/17'de doğrulandı) ama bu buradaki gibi
        // programatik, pencere henüz Loaded olurken yapılan İLK Navigate çağrısında tetiklenip
        // tetiklenmediği bu kütüphane sürümünde belgelenmemiş/doğrulanamadı (bkz. madde A2 —
        // canlı SMART izleme döngüsü hiç başlamadığından trend dosyası günlerce büyümüyordu).
        // Bu yüzden ilk sayfa için izlemeyi burada da açıkça başlatıyoruz; StartMonitoring zaten
        // "zaten çalışıyorsa yok say" koruması taşıdığından (bkz. HealthViewModel.StartMonitoring)
        // WPF-UI'nin kendi çağrısıyla çakışsa bile güvenli/tekrarsız.
        _healthViewModel.SetMonitoringActive(true);

        // Pencere yüklenince diskleri tara.
        await _viewModel.LoadCommand.ExecuteAsync(null);
    }
}
