using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Rendering;

namespace CraftHub.Helpers;

/// <summary>Tints whole document lines with a flat background color — used to render git-style
/// added/removed line highlights in the embedded JSON compare editors. Each editor gets its own
/// instance; call <see cref="SetLines"/> then redraw the TextView after recomputing a diff.</summary>
public sealed class DiffLineBackgroundRenderer : IBackgroundRenderer
{
    private HashSet<int> _lines = new();

    public IBrush Brush { get; set; }

    public DiffLineBackgroundRenderer(IBrush brush) => Brush = brush;

    /// <summary>1-based document line numbers to highlight.</summary>
    public void SetLines(HashSet<int> lines) => _lines = lines;

    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_lines.Count == 0 || !textView.VisualLinesValid) return;

        foreach (var visualLine in textView.VisualLines)
        {
            if (!_lines.Contains(visualLine.FirstDocumentLine.LineNumber)) continue;

            var y = visualLine.VisualTop - textView.ScrollOffset.Y;
            var rect = new Rect(0, y, Math.Max(textView.Bounds.Width, 1), visualLine.Height);
            drawingContext.FillRectangle(Brush, rect);
        }
    }
}
