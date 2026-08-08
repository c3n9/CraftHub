using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using CraftHub.ViewModels;

namespace CraftHub.Helpers;

/// <summary>
/// Renders diff segments as inline runs of a single <see cref="TextBlock"/>, so a long line wraps
/// as one flowing paragraph instead of forcing horizontal scrolling — which in side-by-side mode
/// would push the other column's changes out of view.
/// </summary>
public static class DiffInlineBuilder
{
    public static readonly AttachedProperty<IReadOnlyList<DiffSegment>?> SegmentsProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, IReadOnlyList<DiffSegment>?>("Segments", typeof(DiffInlineBuilder));

    public static IReadOnlyList<DiffSegment>? GetSegments(TextBlock element) => element.GetValue(SegmentsProperty);
    public static void SetSegments(TextBlock element, IReadOnlyList<DiffSegment>? value) => element.SetValue(SegmentsProperty, value);

    static DiffInlineBuilder()
    {
        SegmentsProperty.Changed.AddClassHandler<TextBlock>((tb, _) =>
        {
            // Method group (not a lambda) so the -=/+= pair actually de-duplicates when the list
            // virtualizer recycles this TextBlock for another row.
            tb.ActualThemeVariantChanged -= OnThemeChanged;
            tb.ActualThemeVariantChanged += OnThemeChanged;
            Rebuild(tb);
        });
    }

    private static void OnThemeChanged(object? sender, EventArgs e)
    {
        if (sender is TextBlock tb) Rebuild(tb);
    }

    private static void Rebuild(TextBlock textBlock)
    {
        var segments = GetSegments(textBlock);

        textBlock.Inlines?.Clear();

        if (segments == null || segments.Count == 0)
        {
            textBlock.Text = string.Empty;
            return;
        }

        // Inlines and Text are mutually exclusive — clear Text so the runs are what renders.
        textBlock.Text = null;

        var added = GetBrush(textBlock, "DiffAddedWordBackground");
        var removed = GetBrush(textBlock, "DiffRemovedWordBackground");

        foreach (var segment in segments)
        {
            var run = new Run(segment.Text);

            if (segment.IsAdded) run.Background = added;
            else if (segment.IsRemoved) run.Background = removed;

            textBlock.Inlines?.Add(run);
        }
    }

    // Resolved against the TextBlock itself: Application-level keyed lookups don't reliably track
    // the active theme variant in this app (see SearchHighlightConverter).
    private static IBrush? GetBrush(TextBlock anchor, string key)
        => anchor.TryFindResource(key, out var resource) && resource is IBrush brush ? brush : null;
}
