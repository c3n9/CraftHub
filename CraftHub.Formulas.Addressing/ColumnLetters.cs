using System;
using System.Text;

namespace CraftHub.Formulas.Addressing;

/// <summary>Excel-style bijective base-26 column letters: A=0, B=1, ..., Z=25, AA=26, AB=27, ...
/// "Bijective" is why the math looks a little unusual compared to normal base-26 — there's no
/// letter that means "zero", so each digit position runs 1..26 instead of 0..25.</summary>
public static class ColumnLetters
{
    public static int ToIndex(string letters)
    {
        if (string.IsNullOrEmpty(letters))
            throw new ArgumentException("Column letters cannot be empty.", nameof(letters));

        var result = 0;
        foreach (var c in letters)
        {
            if (!char.IsAsciiLetter(c))
                throw new ArgumentException($"'{letters}' is not a valid column letter sequence.", nameof(letters));
            result = result * 26 + (char.ToUpperInvariant(c) - 'A' + 1);
        }
        return result - 1; // 0-based
    }

    public static string ToLetters(int index)
    {
        if (index < 0)
            throw new ArgumentOutOfRangeException(nameof(index));

        index += 1; // shift into the 1-based bijective scheme
        var sb = new StringBuilder();
        while (index > 0)
        {
            var rem = (index - 1) % 26;
            sb.Insert(0, (char)('A' + rem));
            index = (index - 1) / 26;
        }
        return sb.ToString();
    }
}
