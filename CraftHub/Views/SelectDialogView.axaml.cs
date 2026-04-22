using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;

namespace CraftHub.Views;

public partial class SelectDialogView : Window
{
    public SelectDialogView()
    {
        InitializeComponent();

        OkButton.Click += (_, _) => CloseWithResult(true);
        CancelButton.Click += (_, _) => Close(null);
        OkButton.IsEnabled = false;

        OptionsList.SelectionChanged += (_, _) =>
            OkButton.IsEnabled = OptionsList.SelectedItem != null;

        OptionsList.DoubleTapped += (_, _) => CloseWithResult(true);
    }

    public void SetOptions(string title, string message, string fileName, List<string> options)
    {
        TitleBlock.Text = title;
        MessageBlock.Text = message;
        FileName.Text = fileName;
        OptionsList.ItemsSource = options;
        if (options.Count > 0)
        {
            OptionsList.SelectedIndex = 0;
            OkButton.IsEnabled = true;
        }
    }

    private void CloseWithResult(bool ok)
    {
        Close(ok ? OptionsList.SelectedItem as string : null);
    }
}
