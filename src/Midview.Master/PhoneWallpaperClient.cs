using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

// "Papel de parede dos celulares e tablets" on one phone, on its agent's
// port 28900: SetPhoneWallpaper with the screens and the picture
// (PhoneWallpaperImage's JPEG; empty puts the phone's own wallpaper back),
// answered with a CommandAck saying how it went.
public static class PhoneWallpaperClient
{
    // Android scales and saves the picture before it answers: a few seconds
    // on a slow phone, more than the usual 20.
    private static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(60);

    // Null for a phone agent from before this (it fails a command it doesn't
    // know); otherwise the agent's answer.
    public static (bool Succeeded, string Detail)? Send(
        KnownMachine phone, PhoneWallpaperScreens screens, byte[] image, X509Certificate2 masterCertificate,
        int port = ConnectionClient.AgentPort)
    {
        var payload = PhoneWallpaper.Encode(screens, image);
        var thumbprint = phone.AgentThumbprint is { Length: > 0 } known
            ? known
            : throw new InvalidOperationException("sem a identidade do celular");
        var (succeeded, detail) = CommandClient.SendCommandForResult(
            phone.HostAddress, port, thumbprint, masterCertificate, MessageType.SetPhoneWallpaper, payload, AnswerTimeout);
        return PhoneWallpaperText.IsOldAgent(succeeded, detail) ? null : (succeeded, detail);
    }
}
