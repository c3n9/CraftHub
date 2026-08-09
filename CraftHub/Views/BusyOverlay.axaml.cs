using Avalonia;
using Avalonia.Controls;

namespace CraftHub.Views;

/// <summary>
/// Drop-in "something is happening" scrim for the places that can chew on a large document —
/// diffing, parsing, importing. Overlay the content it covers and bind <see cref="IsActive"/> to
/// the owning view-model's busy flag.
/// </summary>
public partial class BusyOverlay : UserControl
{
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<BusyOverlay, bool>(nameof(IsActive));

    // Left empty by default: each site supplies its own wording, and resolving a localized string
    // in a static initializer would run before LanguageService has loaded its dictionary.
    public static readonly StyledProperty<string> MessageProperty =
        AvaloniaProperty.Register<BusyOverlay, string>(nameof(Message), defaultValue: string.Empty);

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public string Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public BusyOverlay()
    {
        InitializeComponent();
    }
}
