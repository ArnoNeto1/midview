using System.Text.RegularExpressions;

namespace Midview.Master;

// "Aluno-2" before "Aluno-10": runs of digits compare as numbers.
public sealed class NaturalOrder : IComparer<string>
{
    public static readonly NaturalOrder Instance = new();

    private static readonly Regex Parts = new(@"\d+|\D+", RegexOptions.CultureInvariant);

    public int Compare(string? x, string? y)
    {
        var left = Parts.Matches(x ?? string.Empty);
        var right = Parts.Matches(y ?? string.Empty);
        for (var i = 0; i < Math.Min(left.Count, right.Count); i++)
        {
            var a = left[i].Value;
            var b = right[i].Value;
            var result = char.IsDigit(a[0]) && char.IsDigit(b[0])
                ? CompareNumbers(a, b)
                : string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            if (result != 0)
            {
                return result;
            }
        }
        return left.Count.CompareTo(right.Count);
    }

    // By length first once leading zeros are gone, so any number of
    // digits works without overflowing.
    private static int CompareNumbers(string a, string b)
    {
        a = a.TrimStart('0');
        b = b.TrimStart('0');
        return a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b);
    }
}
