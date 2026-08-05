using System.Windows;
using SerkonDiskSuite.App.ViewModels;
using SerkonDiskSuite.App.Views.Pages;
using Wpf.Ui.Controls;

namespace SerkonDiskSuite.App.Views;

public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _viewModel;
    private readonly IServiceProvider _serviceProvider;
    private bool _isInitialized;

    public MainWindow(MainViewModel viewModel, IServiceProvider serviceProvider)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _serviceProvider = serviceProvider;
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

        // Pencere yüklenince diskleri tara.
        await _viewModel.LoadCommand.ExecuteAsync(null);
    }
}
