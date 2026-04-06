using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace CraftHub.Views;

public partial class InputDialogView : Window
{
    public InputDialogView()
    {
        InitializeComponent();
        DataContext = this;

        OkButton.Click += (_, _) => CloseWithResult(true);
        CancelButton.Click += (_, _) => CloseWithResult(false);
        InputBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                CloseWithResult(true);
            }
        };
    }

    public string TitleText
    {
        get => GetValue(TitleTextProperty);
        set => SetValue(TitleTextProperty, value);
    }

    public static readonly StyledProperty<string> TitleTextProperty =
        AvaloniaProperty.Register<InputDialogView, string>(nameof(TitleText), string.Empty);

    public string MessageText
    {
        get => GetValue(MessageTextProperty);
        set => SetValue(MessageTextProperty, value);
    }

    public static readonly StyledProperty<string> MessageTextProperty =
        AvaloniaProperty.Register<InputDialogView, string>(nameof(MessageText), string.Empty);

    public string InputText
    {
        get => GetValue(InputTextProperty);
        set => SetValue(InputTextProperty, value);
    }

    public static readonly StyledProperty<string> InputTextProperty =
        AvaloniaProperty.Register<InputDialogView, string>(nameof(InputText), string.Empty);

    public string PlaceholderText
    {
        get => GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    public static readonly StyledProperty<string> PlaceholderTextProperty =
        AvaloniaProperty.Register<InputDialogView, string>(nameof(PlaceholderText), string.Empty);

    private void CloseWithResult(bool ok)
    {
        Close(ok ? InputText : null);
    }
}
