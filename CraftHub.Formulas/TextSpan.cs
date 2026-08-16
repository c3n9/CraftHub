namespace CraftHub.Formulas;

/// <summary>Position of a token or AST node within the original formula text (after the leading
/// <c>=</c>). Used for syntax highlighting in the formula bar, and for reference-click / F4
/// cycling, which need to know exactly which characters to rewrite.</summary>
public readonly record struct TextSpan(int Start, int Length)
{
    public int End => Start + Length;

    public override string ToString() => $"[{Start}..{End})";
}
