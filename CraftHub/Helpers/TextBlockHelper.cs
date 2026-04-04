using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace CraftHub.Helpers;

public static class TextBlockHelper
{
    private static IBrush HighlightBrush => GetBrush("HighlightBackground", Brushes.Gold);
    private static IBrush HighlightForeground => GetBrush("HighlightForeground", Brushes.Black);

    public static readonly AttachedProperty<string?> HighlightTextProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, string?>("HighlightText", typeof(TextBlockHelper));

    public static string? GetHighlightText(TextBlock element) => element.GetValue(HighlightTextProperty);
    public static void SetHighlightText(TextBlock element, string? value) => element.SetValue(HighlightTextProperty, value);

    public static readonly AttachedProperty<string?> OriginalTextProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, string?>("OriginalText", typeof(TextBlockHelper));

    public static string? GetOriginalText(TextBlock element) => element.GetValue(OriginalTextProperty);
    public static void SetOriginalText(TextBlock element, string? value) => element.SetValue(OriginalTextProperty, value);

    static TextBlockHelper()
    {
        HighlightTextProperty.Changed.AddClassHandler<TextBlock>(OnHighlightChanged);
        OriginalTextProperty.Changed.AddClassHandler<TextBlock>(OnHighlightChanged);
    }

    private static IBrush GetBrush(string key, IBrush fallback)
    {
        if (Application.Current?.TryFindResource(key, out var resource) == true && resource is IBrush brush)
        {
            return brush;
        }

        return fallback;
    }

    private static void OnHighlightChanged(TextBlock textBlock, AvaloniaPropertyChangedEventArgs e)
    {
        var originalText = GetOriginalText(textBlock) ?? string.Empty;
        var highlightText = GetHighlightText(textBlock);

        if (string.IsNullOrEmpty(highlightText) || string.IsNullOrEmpty(originalText))
        {
            textBlock.Inlines?.Clear();
            textBlock.Text = originalText;
            return;
        }

        // We use Inlines for segments, clear the Text property to avoid confusion
        textBlock.Text = null;
        var inlines = textBlock.Inlines;
        inlines?.Clear();

        int lastIndex = 0;
        int nextIndex;

        while ((nextIndex = originalText.IndexOf(highlightText, lastIndex, StringComparison.OrdinalIgnoreCase)) != -1)
        {
            // Text before highlight
            if (nextIndex > lastIndex)
            {
                inlines?.Add(new Run(originalText.Substring(lastIndex, nextIndex - lastIndex)));
            }

            // Highlighted text
            var highlightedRun = new Run(originalText.Substring(nextIndex, highlightText.Length))
            {
                Background = HighlightBrush,
                Foreground = HighlightForeground,
                FontWeight = FontWeight.SemiBold
            };
            inlines?.Add(highlightedRun);

            lastIndex = nextIndex + highlightText.Length;
        }

        // Remaining text
        if (lastIndex < originalText.Length)
        {
            inlines?.Add(new Run(originalText.Substring(lastIndex)));
        }
    }
}
