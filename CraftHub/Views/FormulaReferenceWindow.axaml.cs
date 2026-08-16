using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using CraftHub.Formulas.Functions;
using CraftHub.Helpers;

namespace CraftHub.Views;

/// <summary>
/// The formula reference: how to write one, how references address cells and columns, and the
/// complete function list.
///
/// The function list is generated from <see cref="FunctionRegistry.CreateStandard"/> rather than
/// written out by hand, so it cannot drift from what the engine actually implements — a function
/// added to the registry shows up here with its signature, description and example, and one
/// removed disappears. That is the whole reason this is code-behind rather than static XAML.
/// </summary>
public partial class FormulaReferenceWindow : Window
{
    private static readonly FunctionRegistry Registry = FunctionRegistry.CreateStandard();

    private const string Mono = "Cascadia Code,Cascadia Mono,Consolas,Menlo,Courier New,monospace";

    public FormulaReferenceWindow()
    {
        InitializeComponent();
        BuildProse();
        BuildFunctionList(filter: "");
    }

    // Each entry is (heading key, body key, example lines). Keeping the examples out of the
    // localized strings means the formula syntax in them can't be broken by a translation.
    private static readonly (string Heading, string Body, string[] Examples)[] Sections =
    {
        ("FormulaRefBasicsHeading", "FormulaRefBasicsBody", new[]
        {
            "=1 + 2 * 3",
            "=@[price] * @[qty]",
            "=ROUND(@[total] * 0.2, 2)"
        }),
        ("FormulaRefThisRowHeading", "FormulaRefThisRowBody", new[]
        {
            "=@[price]",
            "=@[price] * @[qty]",
            "=UPPER(@[name]) & \" — \" & @[sku]"
        }),
        ("FormulaRefWholeColumnHeading", "FormulaRefWholeColumnBody", new[]
        {
            "=SUM([total])",
            "=AVERAGE([price])",
            "=ROUND(@[total] / SUM([total]) * 100, 1)"
        }),
        ("FormulaRefOtherRowsHeading", "FormulaRefOtherRowsBody", new[]
        {
            "=B2",
            "=@[total] - D1",
            "=@[value] - $B$1",
            "=SUM(D1:D10)"
        }),
        ("FormulaRefRelativeHeading", "FormulaRefRelativeBody", new[]
        {
            "=D2 - D1",
            "=@[total] * $E$1"
        }),
        ("FormulaRefColumnFormulaHeading", "FormulaRefColumnFormulaBody", Array.Empty<string>()),
        ("FormulaRefTypesHeading", "FormulaRefTypesBody", new[]
        {
            "=VALUE(\"123\") + 1",
            "=IF(@[flag], 1, 0)",
            "=\"total: \" & @[total]"
        }),
        ("FormulaRefDatesHeading", "FormulaRefDatesBody", new[]
        {
            "=EDATE(@[orderedAt], 1)",
            "=DAYS(TODAY(), @[orderedAt])",
            "=YEAR(@[orderedAt])"
        }),
        ("FormulaRefErrorsHeading", "FormulaRefErrorsBody", new[]
        {
            "=IFERROR(@[a] / @[b], 0)",
            "=IF(@[b] = 0, 0, @[a] / @[b])"
        }),
        ("FormulaRefStorageHeading", "FormulaRefStorageBody", Array.Empty<string>())
    };

    private void BuildProse()
    {
        foreach (var (headingKey, bodyKey, examples) in Sections)
        {
            var block = new StackPanel { Spacing = 6 };

            block.Children.Add(new TextBlock
            {
                Text = Localizer.Get(headingKey),
                FontSize = 16,
                FontWeight = FontWeight.SemiBold,
                [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextPrimary")
            });

            block.Children.Add(new TextBlock
            {
                Text = Localizer.Get(bodyKey),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 20,
                [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextSecondary")
            });

            if (examples.Length > 0)
                block.Children.Add(BuildExampleBox(examples));

            ProseSections.Children.Add(block);
        }
    }

    private Control BuildExampleBox(IEnumerable<string> lines)
    {
        var stack = new StackPanel { Spacing = 2 };
        foreach (var line in lines)
            stack.Children.Add(new TextBlock
            {
                Text = line,
                FontFamily = new FontFamily(Mono),
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
                [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextPrimary")
            });

        return new Border
        {
            Padding = new Avalonia.Thickness(12, 9),
            CornerRadius = new Avalonia.CornerRadius(6),
            Margin = new Avalonia.Thickness(0, 2, 0, 0),
            [!Border.BackgroundProperty] = new DynamicResourceExtension("SurfaceBackground"),
            [!Border.BorderBrushProperty] = new DynamicResourceExtension("BorderColor"),
            BorderThickness = new Avalonia.Thickness(1),
            Child = stack
        };
    }

    private void OnSearchChanged(object? sender, TextChangedEventArgs e) =>
        BuildFunctionList(SearchBox.Text ?? "");

    private void BuildFunctionList(string filter)
    {
        FunctionSections.Children.Clear();

        var matches = Registry.All()
            .Where(f => Matches(f, filter))
            .GroupBy(f => f.Category)
            .OrderBy(g => g.Key)
            .ToList();

        foreach (var group in matches)
        {
            var section = new StackPanel { Spacing = 6 };
            section.Children.Add(new TextBlock
            {
                Text = CategoryLabel(group.Key),
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("AccentPrimary")
            });

            foreach (var meta in group.OrderBy(f => f.Name, StringComparer.Ordinal))
                section.Children.Add(BuildFunctionRow(meta));

            FunctionSections.Children.Add(section);
        }

        NoMatchesText.IsVisible = matches.Count == 0;
    }

    // Name, description and argument names all match, so searching "date", "round" or "column"
    // finds things by what they do and not only by what they're called.
    private static bool Matches(FunctionMetadata meta, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;
        var f = filter.Trim();
        return meta.Name.Contains(f, StringComparison.OrdinalIgnoreCase)
               || meta.Description.Contains(f, StringComparison.OrdinalIgnoreCase)
               || meta.Arguments.Any(a => a.Name.Contains(f, StringComparison.OrdinalIgnoreCase));
    }

    private Control BuildFunctionRow(FunctionMetadata meta)
    {
        var argsText = string.Join(", ", meta.Arguments.Select(a =>
        {
            var name = a.Optional ? $"[{a.Name}]" : a.Name;
            return a.Repeating ? name + "…" : name;
        }));

        var signature = new TextBlock
        {
            Text = $"{meta.Name}({argsText})",
            FontFamily = new FontFamily(Mono),
            FontSize = 12.5,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextPrimary")
        };

        var description = new TextBlock
        {
            Text = meta.Volatile
                ? $"{meta.Description} {Localizer.Get("FormulaVolatileNote")}"
                : meta.Description,
            FontSize = 12,
            Opacity = 0.75,
            TextWrapping = TextWrapping.Wrap,
            [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextSecondary")
        };

        var example = new TextBlock
        {
            Text = meta.Example,
            FontFamily = new FontFamily(Mono),
            FontSize = 11.5,
            Opacity = 0.6,
            TextWrapping = TextWrapping.Wrap,
            [!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextSecondary")
        };

        return new Border
        {
            Padding = new Avalonia.Thickness(12, 8),
            CornerRadius = new Avalonia.CornerRadius(6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            [!Border.BackgroundProperty] = new DynamicResourceExtension("SurfaceBackground"),
            Child = new StackPanel { Spacing = 2, Children = { signature, description, example } }
        };
    }

    private static string CategoryLabel(FunctionCategory category) => category switch
    {
        FunctionCategory.Math => Localizer.Get("FormulaCatMath"),
        FunctionCategory.Statistics => Localizer.Get("FormulaCatStatistics"),
        FunctionCategory.Logic => Localizer.Get("FormulaCatLogic"),
        FunctionCategory.Text => Localizer.Get("FormulaCatText"),
        FunctionCategory.Json => Localizer.Get("FormulaCatJson"),
        FunctionCategory.Date => Localizer.Get("FormulaCatDate"),
        _ => category.ToString()
    };
}
