using System.Windows.Controls;
using SerkonDiskSuite.App.ViewModels;

namespace SerkonDiskSuite.App.Views.Pages;

public partial class SystemPage : Page
{
    public SystemPage(SystemViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}
