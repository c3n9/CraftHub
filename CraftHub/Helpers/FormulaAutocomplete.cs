using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using CraftHub.Domain.Models;
using CraftHub.Formulas.Functions;

namespace CraftHub.Helpers;

/// <summary>
/// The formula editor's autocomplete: function names, column names inside <c>[ ]</c>/<c>@[ ]</c>,
/// and the signature of the call the caret sits in. Shared by the main workspace grid and the
/// nested JSON editor dialog so the two can't drift apart — each supplies its own
/// <see cref="Popup"/> host (a popup has to live in its own window's visual tree) and its own list
/// of column keys.
///
/// Three things happen in the one popup, chosen by what's immediately before the caret: completing
/// a function name, completing a COLUMN name inside brackets, and — when there's nothing to
/// complete but the caret sits inside a call's parentheses — showing that function's signature with
/// the argument being typed picked out. Column completion matters at least as much as the function
/// list: unlike Excel's A1 grid, these tables have named fields, so <c>@[unitPrice]</c> is the
/// reference people actually write, and getting it wrong by a character is the most likely way to
/// write a formula that doesn't work.
///
/// None of this parses the formula for real, so a function-shaped word or a '[' inside a string
/// literal can still trigger it — worth the simplicity given how rarely it happens.
/// </summary>
public sealed class FormulaAutocomplete
{
    private static readonly FunctionRegistry Registry = FunctionRegistry.CreateStandard();

    /// <summary>What to offer the instant '=' is typed: arithmetic and aggregation over a column, a
    /// conditional, rounding, and a date — the shapes most first formulas take. Keep it short; this
    /// is a doorway, not a catalogue (the full list is in the formula reference).</summary>
    private static readonly string[] StarterFunctions =
        { "SUM", "AVERAGE", "COUNT", "MIN", "MAX", "IF", "ROUND", "CONCAT", "TODAY" };

    /// <summary>One offered completion. <see cref="InsertText"/> is what replaces the token being
    /// typed; <see cref="Display"/>/<see cref="Detail"/> are what the row shows.</summary>
    private sealed record Suggestion(string InsertText, string Display, string Detail);

    private enum CompletionKind { None, Function, Column }

    private readonly Popup _popup;
    private readonly StackPanel _list;
    private readonly Control _themeHost;
    private readonly Func<IEnumerable<string>> _columnKeys;

    private List<Suggestion> _suggestions = new();
    private readonly List<Button> _rows = new();
    private int _selectedIndex;
    private CompletionKind _kind = CompletionKind.None;
    private int _completionStart;

    /// <summary>The editor the visible suggestions belong to. Set from the box that raised the
    /// event, never from a shared "current editor" field: a DataGrid reuses editor instances, so
    /// such a field goes stale exactly when a second cell is edited.</summary>
    private TextBox? _box;

    /// <summary>Ctrl+Enter handler ("apply this formula to the whole column"). Left null by hosts
    /// that don't offer it, in which case Ctrl+Enter is not intercepted.</summary>
    public Action<TextBox?>? ApplyToColumn { get; set; }

    public FormulaAutocomplete(Popup popup, StackPanel list, Control themeHost, Func<IEnumerable<string>> columnKeys)
    {
        _popup = popup;
        _list = list;
        _themeHost = themeHost;
        _columnKeys = columnKeys;
    }

    public bool IsOpen => _popup.IsOpen;

    public void Close() => _popup.IsOpen = false;

    /// <summary>Gives a freshly created cell editor the formula behaviour: completion of function
    /// and column names, the signature hint, keyboard selection, and (if the host wants it)
    /// Ctrl+Enter. The popup is anchored to this box, so it follows whichever cell is open.</summary>
    public void Attach(TextBox box)
    {
        // A DataGrid reuses editor instances between cells, so this can be handed the same TextBox
        // more than once — wiring it twice would fire every handler twice.
        if (box.Tag is "formula-editing") return;
        box.Tag = "formula-editing";

        box.TextChanged += (_, _) => Update(box);
        // Moving the caret changes what the hint should say just as much as typing does — clicking
        // into an existing formula's parentheses is exactly when someone wants its signature.
        box.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.CaretIndexProperty) Update(box);
        };
        box.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        box.DetachedFromVisualTree += (_, _) => Close();
    }

    // Tunnel, so these are claimed before the DataGrid turns Enter/Escape into its own
    // commit/cancel — otherwise accepting a suggestion with Enter would close the editor instead.
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Ctrl+Enter is "apply to the whole column", never "accept this suggestion".
        if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control) && ApplyToColumn is { } apply)
        {
            e.Handled = true;
            apply(sender as TextBox);
            return;
        }

        if (!_popup.IsOpen) return;

        if (e.Key is Key.Down or Key.Up && _suggestions.Count > 0)
        {
            // Wraps at both ends, so holding one arrow key cycles the list rather than stalling.
            e.Handled = true;
            var count = _suggestions.Count;
            _selectedIndex = e.Key == Key.Down
                ? (_selectedIndex + 1) % count
                : (_selectedIndex - 1 + count) % count;
            HighlightSelected();
        }
        else if (e.Key is Key.Tab or Key.Enter && _suggestions.Count > 0)
        {
            e.Handled = true;
            Accept(_suggestions[Math.Clamp(_selectedIndex, 0, _suggestions.Count - 1)]);
        }
        else if (e.Key == Key.Escape)
        {
            // First Escape dismisses the popup; a second one reaches the grid and cancels the edit.
            e.Handled = true;
            Close();
        }
    }

    private void Update(TextBox box)
    {
        _box = box;
        _popup.PlacementTarget = box;

        var text = box.Text ?? "";
        var caret = Math.Clamp(box.CaretIndex, 0, text.Length);

        (_kind, _completionStart) = FindCompletionTarget(text, caret);
        var prefix = _kind == CompletionKind.None ? "" : text[_completionStart..caret];
        // A nested-field reference is typed quoted (@["a.b"]); the opening quote isn't part of the
        // name being matched.
        if (_kind == CompletionKind.Column && prefix.StartsWith('"')) prefix = prefix[1..];

        _suggestions = _kind switch
        {
            // An empty prefix means "just typed =", and alphabetical order would open on ABS and
            // AND — accurate and useless. A short hand-picked list of the ones people actually
            // reach for first is worth the small maintenance cost.
            CompletionKind.Function when prefix.Length == 0 => StarterFunctions
                .Select(name => Registry.TryGetMetadata(name, out var meta) ? meta : null)
                .Where(meta => meta is not null)
                .Select(meta => ToSuggestion(meta!))
                .ToList(),

            CompletionKind.Function => Registry.All()
                .Where(f => f.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.Name, StringComparer.Ordinal)
                .Take(8)
                .Select(ToSuggestion)
                .ToList(),

            CompletionKind.Column => _columnKeys()
                .Where(k => JsonPropertyDefinition.GetDisplayPath(k).Contains(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(k => k, StringComparer.Ordinal)
                .Take(8)
                .Select(k => new Suggestion(ColumnInsertBody(k), JsonPropertyDefinition.GetDisplayPath(k),
                    Localizer.Get("FormulaColumnSuggestionDetail")))
                .ToList(),

            _ => new List<Suggestion>()
        };

        _list.Children.Clear();
        _rows.Clear();
        // Typing narrows the list; keep the highlight on the first match rather than a stale index.
        _selectedIndex = 0;

        if (_suggestions.Count > 0)
        {
            for (var i = 0; i < _suggestions.Count; i++)
            {
                var row = BuildRow(_suggestions[i], i);
                _rows.Add(row);
                _list.Children.Add(row);
            }
            HighlightSelected();
        }
        else if (FindEnclosingCall(text, caret) is ({ } meta, var activeArg))
        {
            _list.Children.Add(BuildSignatureHint(meta, activeArg));
        }

        _popup.IsOpen = _list.Children.Count > 0;
    }

    /// <summary>Tints the arrow-selected row and scrolls it into view; everything else goes back to
    /// transparent. Cheap enough to call on every keystroke — it only sets a brush on a few
    /// buttons.</summary>
    private void HighlightSelected()
    {
        if (_rows.Count == 0) return;
        _selectedIndex = Math.Clamp(_selectedIndex, 0, _rows.Count - 1);

        var accent = _themeHost.TryFindResource("AccentPrimary", out var res) && res is ISolidColorBrush brush
            ? new SolidColorBrush(brush.Color, 0x33 / 255.0)
            : new SolidColorBrush(Colors.Gray, 0x33 / 255.0);

        for (var i = 0; i < _rows.Count; i++)
            _rows[i].Background = i == _selectedIndex ? accent : Brushes.Transparent;

        _rows[_selectedIndex].BringIntoView();
    }

    /// <summary>What goes between the brackets of a completed <c>[…]</c>/<c>@[…]</c> reference: a
    /// flat column by its bare name, an expanded nested field by its dotted display path in quotes
    /// (a bare dot wouldn't lex as one token).</summary>
    private static string ColumnInsertBody(string columnKey) =>
        columnKey.Contains(JsonFieldMapping.PathSeparator)
            ? $"\"{JsonPropertyDefinition.GetDisplayPath(columnKey)}\""
            : columnKey;

    private static Suggestion ToSuggestion(FunctionMetadata meta)
    {
        var argsText = string.Join(", ", meta.Arguments.Select(a => a.Repeating ? a.Name + "…" : a.Name));
        var detail = meta.Volatile
            ? $"{meta.Description} {Localizer.Get("FormulaVolatileNote")}"
            : meta.Description;
        return new Suggestion(meta.Name, $"{meta.Name}({argsText})", detail);
    }

    /// <summary>What the caret is in the middle of typing, and where that token starts. A column
    /// reference wins over a function name whenever there's an unclosed '[' before the caret —
    /// inside brackets, a run of letters is a field name, never a function.</summary>
    private static (CompletionKind Kind, int Start) FindCompletionTarget(string text, int caret)
    {
        if (!text.StartsWith('=')) return (CompletionKind.None, 0);

        var open = caret > 0 ? text.LastIndexOf('[', caret - 1) : -1;
        if (open >= 0 && text.IndexOf(']', open, caret - open) < 0)
            return (CompletionKind.Column, open + 1);

        var start = caret;
        while (start > 0 && char.IsAsciiLetter(text[start - 1])) start--;
        if (start != caret) return (CompletionKind.Function, start);

        // Nothing typed yet after the '=' — offer a starting point rather than an empty box. This
        // is the moment someone has just learned that '=' begins a formula and has no idea what may
        // follow, so the list is what makes the feature usable at all.
        return caret > 0 && text[caret - 1] == '=' ? (CompletionKind.Function, caret) : (CompletionKind.None, 0);
    }

    /// <summary>The call the caret sits inside, and which argument it's on — found by scanning back
    /// for an unmatched '(' and counting the commas at that same depth along the way.</summary>
    private static (FunctionMetadata? Meta, int ActiveArg) FindEnclosingCall(string text, int caret)
    {
        var depth = 0;
        var commas = 0;

        for (var i = Math.Min(caret, text.Length) - 1; i >= 0; i--)
        {
            switch (text[i])
            {
                case ')':
                    depth++;
                    break;

                case '(' when depth == 0:
                    var end = i;
                    var start = end;
                    while (start > 0 && char.IsAsciiLetter(text[start - 1])) start--;
                    if (start == end) return (null, 0); // a grouping paren, not a call
                    return Registry.TryGetMetadata(text[start..end].ToUpperInvariant(), out var meta)
                        ? (meta, commas)
                        : (null, 0);

                case '(':
                    depth--;
                    break;

                case ',' when depth == 0:
                    commas++;
                    break;
            }
        }

        return (null, 0);
    }

    private Button BuildRow(Suggestion suggestion, int index)
    {
        var button = new Button
        {
            Content = new StackPanel
            {
                Spacing = 1,
                Children =
                {
                    new TextBlock
                    {
                        Text = suggestion.Display,
                        FontFamily = new FontFamily("Cascadia Code,Cascadia Mono,Consolas,Menlo,Courier New,monospace"),
                        FontWeight = FontWeight.SemiBold,
                        FontSize = 12
                    },
                    new TextBlock
                    {
                        Text = suggestion.Detail,
                        FontSize = 11,
                        Opacity = 0.65,
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            },
            HorizontalContentAlignment = HorizontalAlignment.Left,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = Brushes.Transparent,
            BorderThickness = new Avalonia.Thickness(0),
            Padding = new Avalonia.Thickness(8, 4),
            Cursor = Cursor.Parse("Hand")
        };
        // Mouse-down (not Click) so the editor doesn't lose focus before the text swap runs.
        button.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            e.Handled = true;
            Accept(suggestion);
        }, RoutingStrategies.Tunnel);
        // Hovering a row makes it the selected one, so mouse and keyboard never disagree about
        // which suggestion Enter would take.
        button.PointerEntered += (_, _) =>
        {
            _selectedIndex = index;
            HighlightSelected();
        };
        return button;
    }

    // Not a Button: there's nothing to accept here, only something to read while typing arguments.
    private static Control BuildSignatureHint(FunctionMetadata meta, int activeArg)
    {
        var signature = new TextBlock
        {
            FontFamily = new FontFamily("Cascadia Code,Cascadia Mono,Consolas,Menlo,Courier New,monospace"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };
        signature.Inlines?.Add(new Run(meta.Name + "(") { FontWeight = FontWeight.SemiBold });

        for (var i = 0; i < meta.Arguments.Count; i++)
        {
            if (i > 0) signature.Inlines?.Add(new Run(", "));

            var arg = meta.Arguments[i];
            var label = arg.Optional ? $"[{arg.Name}]" : arg.Name;
            if (arg.Repeating) label += "…";

            // A repeating final argument stays highlighted however many values get typed into it.
            var isActive = i == activeArg || (arg.Repeating && i == meta.Arguments.Count - 1 && activeArg >= i);
            signature.Inlines?.Add(new Run(label)
            {
                FontWeight = isActive ? FontWeight.Bold : FontWeight.Normal,
                TextDecorations = isActive ? TextDecorations.Underline : null
            });
        }
        signature.Inlines?.Add(new Run(")") { FontWeight = FontWeight.SemiBold });

        var activeSpec = activeArg < meta.Arguments.Count
            ? meta.Arguments[activeArg]
            : meta.Arguments.LastOrDefault(a => a.Repeating);

        return new StackPanel
        {
            Spacing = 1,
            Margin = new Avalonia.Thickness(8, 4),
            Children =
            {
                signature,
                new TextBlock
                {
                    Text = activeSpec?.Description ?? meta.Description,
                    FontSize = 11,
                    Opacity = 0.65,
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };
    }

    /// <summary>Where the token being completed ends, which is not always the caret: someone fixing
    /// a typo in the middle of <c>@[pri|ce]</c> or <c>SU|M(</c> means to replace the whole name, not
    /// to splice the completion in front of the rest of it.</summary>
    private int CompletionEnd(string text, int caret)
    {
        switch (_kind)
        {
            case CompletionKind.Column:
                var close = text.IndexOf(']', caret);
                return close >= 0 ? close : caret;

            case CompletionKind.Function:
                var end = caret;
                while (end < text.Length && char.IsAsciiLetter(text[end])) end++;
                return end;

            default:
                return caret;
        }
    }

    private void Accept(Suggestion suggestion)
    {
        if (_box is not { } box) return;

        var text = box.Text ?? "";
        var caret = Math.Clamp(box.CaretIndex, 0, text.Length);
        var start = Math.Clamp(_completionStart, 0, caret);
        var end = CompletionEnd(text, caret);

        // A function name brings its own '('; a column name closes its bracket unless one is
        // already sitting there (which it is whenever the user is editing an existing reference) —
        // in which case the caret still steps over it, so typing continues after the reference
        // rather than inside it.
        var bracketAlreadyThere = end < text.Length && text[end] == ']';
        var replacement = _kind switch
        {
            CompletionKind.Function => suggestion.InsertText + "(",
            CompletionKind.Column => bracketAlreadyThere ? suggestion.InsertText : suggestion.InsertText + "]",
            _ => suggestion.InsertText
        };
        var caretShift = _kind == CompletionKind.Column && bracketAlreadyThere ? 1 : 0;

        box.Text = text[..start] + replacement + text[end..];
        box.CaretIndex = start + replacement.Length + caretShift;

        Close();
        box.Focus();
    }
}
