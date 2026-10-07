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

    /// <summary>
    /// 机型 AutoSuggestBox 输入时实时显示候选。
    /// </summary>
    private void OnModelTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            sender.ItemsSource = App.AircraftCatalog.Search(sender.Text);
        }
    }
}
