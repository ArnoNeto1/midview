namespace Midview.Master;

// What the "Preparar celulares" assistant prepares: a phone or a tablet. Both
// run the same phone app; the kind only names the device ("Tablet 07") and
// the assistant's titles. Not Protocol's DeviceKind, which tells a PC from
// an Android device.
public enum AndroidDeviceKind
{
    Phone,
    Tablet,
}

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

    public static string NameOf(string number) => NameOf(number, AndroidDeviceKind.Phone);

    public static string NameOf(string number, AndroidDeviceKind kind) =>
        (kind == AndroidDeviceKind.Tablet ? "Tablet " : "Celular ") + number;

    // The kind of an Android device, from the name NameOf gave it: "Tablet 07"
    // is a tablet; any other name ("Celular 07", one from before tablets) is
    // a phone. The only tablet marker there is.
    public static AndroidDeviceKind KindOf(string name) =>
        name.StartsWith("Tablet ", StringComparison.Ordinal) ? AndroidDeviceKind.Tablet : AndroidDeviceKind.Phone;
}
