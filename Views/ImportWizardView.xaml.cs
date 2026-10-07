using AirPhotoGarage.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AirPhotoGarage.Views;

public sealed partial class ImportWizardView : UserControl
{
    public ImportWizardViewModel ViewModel { get; }

    public ImportWizardView(ImportWizardViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
    }

    public ImportWizardView()
    {
        InitializeComponent();
        ViewModel = App.ImportWizardViewModel;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        ViewModel.CancelCommand.Execute(null);
    }
}