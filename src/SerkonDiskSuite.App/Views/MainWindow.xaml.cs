using System.Windows;
using SerkonDiskSuite.App.ViewModels;

namespace SerkonDiskSuite.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        // Pencere yüklenince diskleri tara.
        Loaded += async (_, _) => await _viewModel.LoadCommand.ExecuteAsync(null);

        // Sağlık sekmesi ilk açılışta zaten görünür olabileceğinden (IsVisibleChanged bu durumda
        // tetiklenmeyebilir), izleme durumunu yüklendiğinde de açıkça senkronize et.
        HealthTabContent.Loaded += (_, _) => _viewModel.Health.SetMonitoringActive(HealthTabContent.IsVisible);
    }

    private void HealthTabContent_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        => _viewModel.Health.SetMonitoringActive((bool)e.NewValue);
}
