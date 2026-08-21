using System;
using System.Collections.Generic;
using System.Linq;
using CraftHub.Formulas.Ast;
using CraftHub.Formulas.Parsing;

namespace CraftHub.Formulas.Addressing;

public abstract record JsonPathSegment
{
    public sealed record Key(string Name) : JsonPathSegment;
    public sealed record Index(int Value) : JsonPathSegment;
}

/// <summary>
/// The stable, resolved address of one JSON node — what a formula actually depends on, and what
/// the sidecar's <c>cellFormulas</c>/<c>columnFormulas</c>/<c>state</c> keys are. Unlike A1
/// notation, a path survives column reordering, sorting, and filtering untouched, which is exactly
/// why the app's own field-mapping already addresses nested data by path rather than position (see
/// CLAUDE.md's "Nested JSON paths" section) — this reuses that idea rather than inventing a new one.
/// </summary>
public sealed class JsonPath : IEquatable<JsonPath>
{
    public IReadOnlyList<JsonPathSegment> Segments { get; }
    private readonly string _canonical;

    public JsonPath(IReadOnlyList<JsonPathSegment> segments)
    {
        Segments = segments;
        _canonical = AstPrinter.Print(ToSyntax(segments));
    }

    public static JsonPath RootRow(int rowIndex) => new(new JsonPathSegment[] { new JsonPathSegment.Index(rowIndex) });

    public JsonPath Append(JsonPathSegment segment) => new(Segments.Append(segment).ToList());

    /// <summary>Parses canonical path text (a sidecar's cellFormulas/state key, e.g.
    /// <c>$[3].total</c>) back into a <see cref="JsonPath"/>. Throws <see cref="FormatException"/>
    /// if the text isn't a JSON path at all, or is a template rather than a concrete one — a
    /// wildcard (<c>$[*]</c>) or relative-row (<c>$[r+1]</c>) segment has no single resolved
    /// address, so it can't become a <see cref="JsonPath"/>; those only ever appear inside a
    /// formula's own text, never as a cellFormulas/state key.</summary>
    public static JsonPath Parse(string text)
    {
        Ast.FormulaAst ast;
        try
        {
            ast = FormulaParser.ParseExpressionText(text);
        }
        catch (FormulaParseException ex)
        {
            throw new FormatException($"'{text}' is not a valid JSON path: {ex.Message}");
        }

        if (ast is not JsonPathSyntax syntax)
            throw new FormatException($"'{text}' is not a JSON path.");

        var segments = new List<JsonPathSegment>();
        foreach (var seg in syntax.Segments)
        {
            switch (seg)
            {
                case PathSegmentSyntax.Key k:
                    segments.Add(new JsonPathSegment.Key(k.Name));
                    break;
                case PathSegmentSyntax.Index { Value: PathIndexSyntax.Literal lit }:
                    segments.Add(new JsonPathSegment.Index(lit.Value));
                    break;
                default:
                    throw new FormatException($"'{text}' is a template (relative or wildcard), not a concrete path.");
            }
        }
        return new JsonPath(segments);
    }

    /// <summary>Canonical text form, e.g. <c>$[2].address.city</c> — what gets written into the
    /// sidecar and re-parsed on load. Reuses <see cref="AstPrinter"/> so there is exactly one place
    /// that decides how a path is spelled (bare identifier vs quoted-bracket key, etc.).</summary>
    public string ToCanonicalString() => _canonical;

    public override string ToString() => _canonical;

    public bool Equals(JsonPath? other) => other is not null && _canonical == other._canonical;
    public override bool Equals(object? obj) => Equals(obj as JsonPath);
    public override int GetHashCode() => _canonical.GetHashCode();

    private static JsonPathSyntax ToSyntax(IReadOnlyList<JsonPathSegment> segments)
    {
        var syntaxSegments = segments.Select(PathSegmentSyntax (s) => s switch
        {
            JsonPathSegment.Key k => new PathSegmentSyntax.Key(k.Name),
            JsonPathSegment.Index i => new PathSegmentSyntax.Index(new PathIndexSyntax.Literal(i.Value)),
            _ => throw new ArgumentOutOfRangeException(nameof(segments))
        }).ToList();
        return new JsonPathSyntax(default, syntaxSegments);
    }
}
