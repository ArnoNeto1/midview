using Midview.Protocol;

namespace Midview.Master;

// "Otimizar" also puts the school's admin password on each device, when one
// is saved in "Preparar celulares" (PhoneDefaultsFile): the very payload kept
// there (its salted hash), sent with SetPhoneAdminPassword after the device
// answered the optimizing. A device that has it already gets it again
// harmlessly. Nothing when none is saved or the device's Midview is too old
// to optimize (it is too old for the password too).
public static class PhoneOptimizeAdmin
{
    public const string Put = "senha de administrador posta";

    // The note for the device's line, or null when nothing was sent.
    // setAdmin: PhoneNetworkClient.SetAdminPassword for this device.
    public static string? Send(
        PhoneOptimizeReport? report, byte[]? adminPayload, Func<byte[], (bool Succeeded, string Detail)?> setAdmin)
    {
        if (report is null || adminPayload is null)
        {
            return null;
        }
        try
        {
            return setAdmin(adminPayload) switch
            {
                { Succeeded: true } => Put,
                { } said => NotPut(said.Detail),
                null => NotPut("o Midview do aparelho é antigo"),
            };
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.Net.Sockets.SocketException
                                       or System.Security.Authentication.AuthenticationException
                                       or InvalidOperationException or TimeoutException)
        {
            return NotPut("o aparelho não respondeu");
        }
    }

    public static string NotPut(string detail) => $"senha de administrador não posta ({detail})";

    // The device's line with the note after it.
    public static string Line(string line, string? note) => note is null ? line : $"{line} · {note}";
}
