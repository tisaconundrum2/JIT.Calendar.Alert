using JustInTimeAlerts.ViewModels;

namespace JustInTimeAlerts;

public partial class MainPage : ContentPage
{
    public MainPage(MainViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
