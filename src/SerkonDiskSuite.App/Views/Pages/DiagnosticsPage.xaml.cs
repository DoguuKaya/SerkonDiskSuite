using System.Windows.Controls;
using SerkonDiskSuite.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace SerkonDiskSuite.App.Views.Pages;

/// <summary>
/// Teşhis sayfası. Sayfaya her girildiğinde (ör. self-test başka bir sekmedeyken bitmiş
/// olabilir) durum yenilenir.
/// </summary>
public partial class DiagnosticsPage : Page, INavigationAware
{
    private readonly DiagnosticsViewModel _viewModel;

    public DiagnosticsPage(DiagnosticsViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = _viewModel;
        InitializeComponent();
    }

    public Task OnNavigatedToAsync()
    {
        return _viewModel.RefreshCommand.ExecuteAsync(null);
    }

    public Task OnNavigatedFromAsync() => Task.CompletedTask;
}
