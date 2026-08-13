using System;
using System.Xml;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;

namespace CraftHub.Helpers;

/// <summary>
/// Loads the bundled JSON syntax definitions, one per theme. Shared by every AvaloniaEdit surface
/// in the app so they all colour JSON identically and pay the .xshd load cost only once.
/// </summary>
public static class JsonHighlightingHelper
{
    private static IHighlightingDefinition? _light;
    private static IHighlightingDefinition? _dark;

    /// <summary>
    /// True when the app is currently rendering the dark theme. Reads ThemeService's own
    /// <c>RequestedThemeVariant</c> target, which is reliable — unlike resolving a *keyed resource*
    /// via <c>Application.Current.TryFindResource</c>, which can pick the wrong theme dictionary.
    /// </summary>
    public static bool IsDarkTheme => Application.Current?.ActualThemeVariant == ThemeVariant.Dark;

    public static IHighlightingDefinition? ForCurrentTheme() => Get(IsDarkTheme);

    /// <summary>
    /// Gives an editor the shared selection colours. AvaloniaEdit's default selection is a
    /// near-opaque blue that drowns out the syntax colours underneath; a translucent overlay with
    /// no forced foreground lets them show through. Applied to every JSON surface so a selection
    /// looks the same in the editor and in the comparer.
    /// </summary>
    public static void ApplySelectionColors(TextEditor editor)
    {
        editor.TextArea.SelectionBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x60, 0xA5, 0xFA));
        editor.TextArea.SelectionForeground = null;
    }

    public static IHighlightingDefinition? Get(bool dark)
    {
        if (dark)
        {
            _dark ??= Load("JsonHighlighting.Dark.xshd");
            return _dark;
        }

        _light ??= Load("JsonHighlighting.Light.xshd");
        return _light;
    }

    private static IHighlightingDefinition? Load(string fileName)
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri($"avares://CraftHub/Resources/{fileName}"));
            using var reader = XmlReader.Create(stream);
            return HighlightingLoader.Load(reader, HighlightingManager.Instance);
        }
        catch
        {
            // Fall back to AvaloniaEdit's built-in JSON definition if the bundled one fails to load.
            return HighlightingManager.Instance.GetDefinition("Json");
        }
    }
}
