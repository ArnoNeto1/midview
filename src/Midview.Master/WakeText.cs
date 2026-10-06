namespace Midview.Master;

// What the status bar says after "Ligar". Nothing answers a magic packet, so
// it can only say who was sent one; the grid shows them coming online.
public static class WakeText
{
    // The status bar names this many machines it couldn't wake, then counts the rest.
    private const int UnknownNamed = 5;

    public static string Summary(IReadOnlyList<string> sentTo, IReadOnlyList<string> unknown, bool sent, string? error)
    {
        if (sentTo.Count == 0)
        {
            return unknown.Count == 1
                ? $"{unknown[0]}: o Midview ainda não conhece a placa de rede dela. Ligue-a uma vez com o Midview aberto."
                : "Nenhuma dessas máquinas tem a placa de rede conhecida ainda: ligue cada uma uma vez com o Midview aberto.";
        }
        if (!sent)
        {
            return $"O pedido para ligar não pôde ser enviado ({error ?? "sem rede"}): este computador está na rede?";
        }

        var text = sentTo.Count == 1
            ? $"Pedido para ligar enviado a {sentTo[0]}. Ela aparece na grade em 1 ou 2 minutos."
            : $"Pedido para ligar enviado a {sentTo.Count} máquinas. Elas aparecem na grade em 1 ou 2 minutos.";
        if (unknown.Count > 0)
        {
            var named = string.Join(", ", unknown.Take(UnknownNamed));
            var rest = unknown.Count > UnknownNamed ? $" e mais {unknown.Count - UnknownNamed}" : string.Empty;
            text += $" Sem a placa de rede conhecida (ligue-as uma vez com o Midview aberto): {named}{rest}.";
        }
        return text;
    }
}
