using AirPhotoGarage.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace AirPhotoGarage.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        InitializeComponent();
        ViewModel = App.SettingsViewModel;
    }
}
