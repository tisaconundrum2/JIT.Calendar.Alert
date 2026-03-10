using JustInTimeAlerts.ViewModels;

namespace JustInTimeAlerts.Views;

public partial class LogPage : ContentPage
{
    public LogPage(LogViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
