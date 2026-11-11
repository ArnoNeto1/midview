namespace Midview.Master;

// The number the teacher types for a school phone ("7" becomes "07") and the
// name the phone gets from it.
public static class PhoneNumber
{
    // Null unless the text, once spaces are removed, is 1 to 3 digits. The
    // number is what counts, so "007" and "7" are both "07" (and "0" is "00").
    public static string? Normalize(string typed)
    {
        var digits = (typed ?? "").Trim();
        if (digits.Length is < 1 or > 3 || !digits.All(c => c is >= '0' and <= '9'))
        {
            return null;
        }
        return int.Parse(digits, System.Globalization.CultureInfo.InvariantCulture).ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
    }

    public static string NameOf(string number) => "Celular " + number;
}
