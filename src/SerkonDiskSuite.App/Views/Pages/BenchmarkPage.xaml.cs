using System.Windows.Controls;
using SerkonDiskSuite.App.ViewModels;

namespace SerkonDiskSuite.App.Views.Pages;

public partial class BenchmarkPage : Page
{
    public BenchmarkPage(BenchmarkViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
