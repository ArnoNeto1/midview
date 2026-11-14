using Midview.Protocol;

namespace Midview.Master;

// How the "Papel de parede dos celulares e tablets" window words the
// picture, the screens and each device's answer, kept apart so it can be
// tested. The agent's own details ("papel de parede posto na tela
// inicial"...) are already in Portuguese and are shown as they come.
public static class PhoneWallpaperText
{
    public const string OldAgent = "o Midview deste celular é de uma versão anterior e ainda não troca o papel de parede";

    public const string NoIdentity = PhonePinText.NoIdentity;

    public const string NotAPicture = "Esse arquivo não é uma imagem JPG ou PNG que dê para abrir.";

    public const string FileTooBig = "Imagem grande demais: escolha uma de até 50 MB.";

    // Too many pixels to open (PhoneWallpaperImage.MaxPixels), or the
    // computer ran out of memory opening it.
    public const string TooManyPixels =
        "Imagem grande demais para abrir neste computador. Escolha uma foto menor, ou diminua esta num editor de imagens.";

    public const string NothingChosen = "Nenhuma imagem escolhida.";

    public const string FileFilter = "Imagens (*.jpg; *.jpeg; *.png)|*.jpg;*.jpeg;*.png";

    // The screens, in the order the window offers them; the first is the default.
    public static readonly IReadOnlyList<PhoneWallpaperScreens> Choices =
        new[] { PhoneWallpaperScreens.Both, PhoneWallpaperScreens.Home, PhoneWallpaperScreens.Lock };

    public static string Choice(PhoneWallpaperScreens screens) => screens switch
    {
        PhoneWallpaperScreens.Home => "Só tela inicial",
        PhoneWallpaperScreens.Lock => "Só tela de bloqueio",
        _ => "Tela inicial e de bloqueio",
    };

    // Every Android device runs the same agent, phones and tablets alike.
    public static string Heading(int devices) =>
        devices == 1 ? "Papel de parede do celular ou tablet" : "Papel de parede dos celulares e tablets";

    // Under the button, once a picture is chosen: its name, the size it goes
    // at and how much is sent to each device.
    public static string Picture(string fileName, int width, int height, int bytes) =>
        $"{fileName}: vai com {width} × {height} pixels ({Size(bytes)})";

    public static string Size(int bytes) => bytes < 1024 * 1024
        ? $"{Math.Max(1, (bytes + 512) / 1024)} KB"
        : $"{bytes / (1024.0 * 1024.0):0.0} MB".Replace('.', ',');

    // An agent from before this: it fails a command it doesn't know.
    public static bool IsOldAgent(bool succeeded, string detail) => PhonePinText.IsOldAgent(succeeded, detail);

    // One line per device, from the agent's answer (PhoneWallpaperClient:
    // null for an older agent).
    public static string Line((bool Succeeded, string Detail)? answer) => answer is { } said ? said.Detail : OldAgent;

    public static string Working(bool restore) => restore ? "voltando ao padrão…" : "enviando a imagem…";

    // While the devices are being asked.
    public static string Progress(bool restore, int devices) => (restore, devices) switch
    {
        (false, 1) => "Pondo o papel de parede no aparelho...",
        (false, _) => $"Pondo o papel de parede nos {devices} aparelhos...",
        (true, 1) => "Voltando o papel de parede do aparelho ao padrão...",
        _ => $"Voltando o papel de parede dos {devices} aparelhos ao padrão...",
    };

    // Above the list, once every device answered (or didn't).
    public static string Summary(bool restore, int done, int total)
    {
        var did = restore ? "Papel de parede de volta ao padrão" : "Papel de parede posto";
        return (done, total) switch
        {
            (1, 1) => $"{did}.",
            (_, 1) => "Não deu certo no aparelho (veja abaixo).",
            _ when done == total => $"{did} nos {total} aparelhos.",
            (0, _) => $"Não deu certo em nenhum dos {total} aparelhos (veja abaixo).",
            _ => $"{did} em {done} dos {total} aparelhos (veja abaixo).",
        };
    }
}
