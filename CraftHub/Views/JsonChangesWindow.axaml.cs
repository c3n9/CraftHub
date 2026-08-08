using Avalonia.Controls;
using CraftHub.Helpers;

namespace CraftHub.Views;

public partial class JsonChangesWindow : Window
{
    public JsonChangesWindow()
    {
        InitializeComponent();
        WindowGeometryHelper.Attach(this, "JsonChangesWindow");
    }
}
