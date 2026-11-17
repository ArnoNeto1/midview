namespace Midview.Master;

// The school's defaults (PhoneDefaultsFile) put on a phone or tablet the
// cable just prepared, once it answers on the network: the picture for its
// kind, then the PIN. One line says how it went, for the teacher's status
// bar; nothing when there is nothing to put.
public static class PreparedDefaults
{
    // sendWallpaper and sendPin: PhoneWallpaperClient.Send and
    // PhonePinClient.Send for this device (null from an agent too old for it).
    public static string? Apply(
        string name,
        AndroidDeviceKind kind,
        PhoneDefaultsFile.Defaults defaults,
        Func<PhoneDefaultsFile.Wallpaper, (bool Succeeded, string Detail)?> sendWallpaper,
        Func<string, (bool Succeeded, string Detail)?> sendPin)
    {
        var done = new List<string>();
        var failed = new List<string>();
        if (defaults.For(kind) is { } wallpaper)
        {
            Tally("papel de parede", Try(() => sendWallpaper(wallpaper)), done, failed);
        }
        if (defaults.Pin is { } pin)
        {
            Tally("PIN", Try(() => sendPin(pin)), done, failed);
        }
        if (done.Count == 0 && failed.Count == 0)
        {
            return null;
        }
        var put = done.Count == 0 ? "" : $"{string.Join(" e ", done)} da escola {(done.Count == 1 ? "posto" : "postos")}";
        var notPut = failed.Count == 0 ? "" : $"não deu para pôr {string.Join("; ", failed)}";
        return $"{name}: {put}{(put.Length > 0 && notPut.Length > 0 ? "; " : "")}{notPut}.";
    }

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

    private static void Tally(string what, (bool Succeeded, string Detail)? answer, List<string> done, List<string> failed)
    {
        if (answer is { Succeeded: true })
        {
            done.Add(what);
        }
        else
        {
            failed.Add($"o {what} ({answer?.Detail})");
        }
    }
}
