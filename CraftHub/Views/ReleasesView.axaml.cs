using Avalonia.Controls;
using Avalonia.Interactivity;
using CraftHub.ViewModels;

namespace CraftHub.Views;

public partial class ReleasesView : Window
{
    public ReleasesView()
    {
        InitializeComponent();
        CloseButton.Click += (_, _) => Close();
        Opened += OnOpened;
    }

    private void OnOpened(object? sender, System.EventArgs e)
    {
        // Fetch releases once the window is shown; the view-model drives loading/error state.
        if (DataContext is ReleasesViewModel vm)
            _ = vm.LoadAsync();
    }
}
