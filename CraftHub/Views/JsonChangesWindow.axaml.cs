using Avalonia.Controls;
using CraftHub.Helpers;
using CraftHub.ViewModels;

namespace CraftHub.Views;

public partial class JsonChangesWindow : Window
{
    public JsonChangesWindow()
    {
        InitializeComponent();
        WindowGeometryHelper.Attach(this, "JsonChangesWindow");

        DataContextChanged += (_, _) =>
        {
            // Only meaningful in confirm mode, where the window is shown modally and its result is
            // what decides whether the save goes ahead.
            if (DataContext is JsonChangesWindowViewModel vm)
                vm.RequestClose += result => Close(result);
        };
    }
}
