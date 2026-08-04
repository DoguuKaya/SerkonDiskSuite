using System.Windows.Controls;
using SerkonDiskSuite.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace SerkonDiskSuite.App.Views.Pages;

/// <summary>
/// Sağlık sayfası. <see cref="INavigationAware"/> ile NavigationView'ın
/// sayfa geçişlerinden haberdar olur: sayfaya girildiğinde sıcaklık izleme
/// döngüsü başlar, sayfadan çıkıldığında (başka sayfaya geçilince) durur.
/// </summary>
public partial class HealthPage : Page, INavigationAware
{
    private readonly HealthViewModel _viewModel;

    public HealthPage(HealthViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = _viewModel;
        InitializeComponent();
    }

    public Task OnNavigatedToAsync()
    {
        _viewModel.SetMonitoringActive(true);
        return Task.CompletedTask;
    }

    public Task OnNavigatedFromAsync()
    {
        _viewModel.SetMonitoringActive(false);
        return Task.CompletedTask;
    }
}
