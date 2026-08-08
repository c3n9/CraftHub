using Avalonia.Controls;
using CraftHub.ViewModels;

namespace CraftHub.Views;

public partial class JsonDiffView : Window
{
    public JsonDiffView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is JsonDiffViewModel vm)
                vm.RequestClose += result => Close(result);
        };
    }
}
