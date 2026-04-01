using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CraftHub.ViewModels;

namespace CraftHub.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        // Wire up tab click selection since binding to SelectedWorkspace
        // through ItemsControl item template is non-trivial
        UpdateTabVisuals();
    }

    private void UpdateTabVisuals()
    {
        // Tab visuals are handled via styles
    }
}
