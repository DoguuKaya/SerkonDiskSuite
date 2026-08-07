using System.Windows.Controls;
using SerkonDiskSuite.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace SerkonDiskSuite.App.Views.Pages;

/// <summary>
/// Sistem sayfası. <see cref="INavigationAware"/> ile NavigationView'ın sayfa
/// geçişlerinden haberdar olur: sayfaya girildiğinde CPU/GPU/RAM izleme döngüsü
/// başlar, sayfadan çıkıldığında (başka sayfaya geçilince) durur — HealthPage'deki
/// desenle aynı.
/// </summary>
public partial class SystemPage : Page, INavigationAware
{
    private readonly SystemViewModel _viewModel;

    public SystemPage(SystemViewModel viewModel)
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
