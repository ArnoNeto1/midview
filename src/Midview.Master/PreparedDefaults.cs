namespace Midview.Master;

// The school's defaults (PhoneDefaultsFile) put on a phone or tablet the
// cable just prepared, once it answers on the network: the picture for its
// kind, then the PIN, then the phones' admin password (its salted hash). One
// line says how it went, for the teacher's status bar; nothing when there is
// nothing to put.
public static class PreparedDefaults
{
    // sendWallpaper, sendPin and sendAdminPassword: PhoneWallpaperClient.Send,
    // PhonePinClient.Send and PhoneNetworkClient.SetAdminPassword for this
    // device (null from an agent too old for it).
    public static string? Apply(
        string name,
        AndroidDeviceKind kind,
        PhoneDefaultsFile.Defaults defaults,
        Func<PhoneDefaultsFile.Wallpaper, (bool Succeeded, string Detail)?> sendWallpaper,
        Func<string, (bool Succeeded, string Detail)?> sendPin,
        Func<byte[], (bool Succeeded, string Detail)?>? sendAdminPassword = null)
    {
        var done = new List<Part>();
        var failed = new List<string>();
        if (defaults.For(kind) is { } wallpaper)
        {
            Tally(Wallpaper, Try(() => sendWallpaper(wallpaper)), done, failed);
        }
        if (defaults.Pin is { } pin)
        {
            Tally(Pin, Try(() => sendPin(pin)), done, failed);
        }
        if (defaults.AdminPassword is { } admin && sendAdminPassword is not null)
        {
            Tally(AdminPassword, Try(() => sendAdminPassword(admin)), done, failed);
        }
        if (done.Count == 0 && failed.Count == 0)
        {
            return null;
        }
        // "posto" with anything masculine among them; "posta" for the password alone.
        var put = done.Count == 0
            ? ""
            : $"{PhoneDefaultsText.List(done.Select(part => part.What).ToList())} da escola " +
              $"{(done.Count > 1 ? "postos" : done[0].Feminine ? "posta" : "posto")}";
        var notPut = failed.Count == 0 ? "" : $"não deu para pôr {string.Join("; ", failed)}";
        return $"{name}: {put}{(put.Length > 0 && notPut.Length > 0 ? "; " : "")}{notPut}.";
    }

    private sealed record Part(string What, bool Feminine);

    private static readonly Part Wallpaper = new("papel de parede", false);
    private static readonly Part Pin = new("PIN", false);
    private static readonly Part AdminPassword = new("senha de administrador", true);

    private static (bool Succeeded, string Detail)? Try(Func<(bool Succeeded, string Detail)?> send)
    {
        try
        {
            return send() ?? (false, "o Midview do aparelho é antigo");
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.Net.Sockets.SocketException
                                       or System.Security.Authentication.AuthenticationException
                                       or InvalidOperationException or TimeoutException)
        {
            return (false, "o aparelho não respondeu");
        }
    }

    private static void Tally(Part part, (bool Succeeded, string Detail)? answer, List<Part> done, List<string> failed)
    {
        if (answer is { Succeeded: true })
        {
            done.Add(part);
        }
        else
        {
            failed.Add($"{(part.Feminine ? "a" : "o")} {part.What} ({answer?.Detail})");
        }
    }
}
