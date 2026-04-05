using Avalonia;
using Avalonia.Controls;

namespace CraftHub.Views;

public partial class MessageBoxView : Window
{
    public MessageBoxView()
    {
        InitializeComponent();
        DataContext = this;

        OkButton.Click += (_, _) => CloseWithResult(true);
        CancelButton.Click += (_, _) => CloseWithResult(false);
    }

    public string TitleText
    {
        get => GetValue(TitleTextProperty);
        set => SetValue(TitleTextProperty, value);
    }

    public static readonly StyledProperty<string> TitleTextProperty =
        AvaloniaProperty.Register<MessageBoxView, string>(nameof(TitleText), string.Empty);

    public string MessageText
    {
        get => GetValue(MessageTextProperty);
        set => SetValue(MessageTextProperty, value);
    }

    public static readonly StyledProperty<string> MessageTextProperty =
        AvaloniaProperty.Register<MessageBoxView, string>(nameof(MessageText), string.Empty);

    public bool IsConfirm
    {
        get => GetValue(IsConfirmProperty);
        set => SetValue(IsConfirmProperty, value);
    }

    public static readonly StyledProperty<bool> IsConfirmProperty =
        AvaloniaProperty.Register<MessageBoxView, bool>(nameof(IsConfirm));

    private void CloseWithResult(bool result)
    {
        Close(result);
    }
}
