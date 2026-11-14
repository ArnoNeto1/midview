using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

// "Tocar para encontrar" (MessageType.FindPhone): a lost phone or tablet
// rings at full alarm volume, vibrates and shows "Parar" until the student
// stops it, the teacher does ("Parar o toque") or the time is up. What the
// menus say, and one line per device for the status bar.
public static class PhoneFindText
{
    public const string StartMenuItem = "Tocar para encontrar";
    public const string StopMenuItem = "Parar o toque";

    public const string StartToolTip =
        "Faz o celular ou tablet tocar alto (mesmo no silencioso) por 1 minuto, para achar onde ele está. O aluno pode parar na tela do aparelho.";

    public const string StopToolTip = "Para o toque dos celulares e tablets que estão tocando.";

    // What an agent from before it answers to any command it does not know.
    public const string OldAgentAnswer = "Ainda não funciona em celular.";

    // "Parar o toque" while every device the menu reaches is ringing.
    public static string MenuItem(bool allRinging) => allRinging ? StopMenuItem : StartMenuItem;

    public static string Summary(string name, bool start, bool succeeded, string detail) => (start, succeeded, detail) switch
    {
        (_, false, OldAgentAnswer) => $"{name}: o Midview deste aparelho é de uma versão anterior; atualize-o para tocar.",
        (true, true, _) => $"{name} está tocando.",
        (false, true, _) => $"{name} parou de tocar.",
        _ => $"{name}: {detail}",
    };
}

// Sends FindPhone to one device's agent (port 28900).
public static class PhoneFindClient
{
    public static (bool Succeeded, string Detail) Send(
        KnownMachine phone, X509Certificate2 masterCertificate, int seconds, int port = ConnectionClient.AgentPort) =>
        CommandClient.SendCommandForResult(
            phone.HostAddress, port,
            phone.AgentThumbprint is { Length: > 0 } thumbprint ? thumbprint : throw new InvalidOperationException("sem a identidade do Agent"),
            masterCertificate, MessageType.FindPhone, PhoneFind.Encode(seconds));
}

// Which devices the teacher set ringing, and until when (by their agent's
// thumbprint): the menus offer "Parar o toque" while all those they reach
// still ring. The device itself may stop sooner (the student's "Parar").
public sealed class PhoneFindRinging
{
    private readonly Dictionary<string, DateTime> _until = new(StringComparer.OrdinalIgnoreCase);

    public void Started(IEnumerable<KnownMachine> phones, DateTime now, int seconds)
    {
        foreach (var phone in phones)
        {
            _until[Key(phone)] = now.AddSeconds(seconds);
        }
    }

    public void Stopped(IEnumerable<KnownMachine> phones)
    {
        foreach (var phone in phones)
        {
            _until.Remove(Key(phone));
        }
    }

    public bool IsRinging(KnownMachine phone, DateTime now) => _until.TryGetValue(Key(phone), out var until) && now < until;

    public bool AllRinging(IReadOnlyCollection<KnownMachine> phones, DateTime now) =>
        phones.Count > 0 && phones.All(phone => IsRinging(phone, now));

    private static string Key(KnownMachine phone) => phone.AgentThumbprint ?? phone.Name;
}
