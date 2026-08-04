using SerkonDiskSuite.App.ViewModels;
using SerkonDiskSuite.App.Views.Pages;
using Wpf.Ui.Controls;

namespace SerkonDiskSuite.App.Views;

public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel, IServiceProvider serviceProvider)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        // NavigationView'ın TargetPageType ile işaretlenmiş sayfaları DI konteynerinden
        // (yapıcısına ViewModel enjekte edilerek) çözebilmesi için servis sağlayıcıyı bağla.
        RootNavigation.SetServiceProvider(serviceProvider);
        RootNavigation.Navigate(typeof(HealthPage), null);

        // Pencere yüklenince diskleri tara.
        Loaded += async (_, _) => await _viewModel.LoadCommand.ExecuteAsync(null);
    }
}
