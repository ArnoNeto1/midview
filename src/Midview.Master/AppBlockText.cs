using Midview.Protocol;

namespace Midview.Master;

// How the block-programs window words things, kept apart so it can be tested.
public static class AppBlockText
{
    // What an Agent from before this feature answers to SetAppBlock /
    // GetAppBlock: a CommandAck with no detail, which CommandResult reads as
    // (false, "").
    public const string OldAgent =
        "o Midview dessa máquina é de uma versão anterior: atualize pela rede ou com o pendrive";

    // Common programs offered as one-click suggestions. Names are best
    // guesses; the teacher can always type others.
    public static readonly IReadOnlyList<(string Group, string[] Programs)> Suggestions = new[]
    {
        ("Navegadores", new[] { "chrome.exe", "msedge.exe", "firefox.exe", "opera.exe", "brave.exe" }),
        ("Jogos e lojas", new[] { "steam.exe", "epicgameslauncher.exe", "robloxplayerbeta.exe", "minecraftlauncher.exe" }),
        ("Mensagens", new[] { "discord.exe", "whatsapp.exe", "telegram.exe" }),
    };

    // The result line for one machine after applying, from the ack.
    public static string ApplyLine(bool succeeded, string detail) =>
        detail.Length == 0 ? OldAgent : detail;

    public static string Describe(AppBlockPolicy policy) => policy.Mode switch
    {
        AppBlockMode.BlockList => $"bloqueando {policy.Programs.Count} programa(s)",
        AppBlockMode.AllowOnly => policy.Programs.Count == 0
            ? "permitindo só o Windows"
            : $"permitindo só o Windows e {policy.Programs.Count} programa(s)",
        _ => "nenhum programa bloqueado",
    };
}
