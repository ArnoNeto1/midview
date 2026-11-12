using System.IO;
using Midview.Protocol;

namespace Midview.Master;

// The roster after a prepared phone joined: a phone already listed with the
// same name (the same phone coming back after a wipe) is replaced; a PC with
// that name is left alone.
public static class PhoneJoinRoster
{
    public static IReadOnlyList<KnownMachine> ReplacingSameName(IReadOnlyList<KnownMachine> roster, KnownMachine joined) =>
        roster.Where(m => !(m.IsPhone && string.Equals(m.Name, joined.Name, StringComparison.OrdinalIgnoreCase)))
            .Append(joined).ToList();

    // Runs the step that adds the joined phone and saves the roster. By then
    // the phone trusts this PC, so a save that fails is not a reason to hide
    // the phone: null when it all worked, else the text for the status bar.
    public static string? AddAndSave(Action addAndSave)
    {
        try
        {
            addAndSave();
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            return "O celular entrou na turma, mas a lista de máquinas não pôde ser guardada: " + ErrorText.Describe(ex);
        }
    }
}
