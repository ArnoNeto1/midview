using System.Threading.Tasks;
using System.Windows;
using Midview.Protocol;

namespace Midview.Master;

// Turns one machine or the whole class on over the network (Wake-on-LAN).
// Nothing answers a magic packet, so all it can say is which machines were
// sent one; the grid shows them coming online.
public partial class WakeWindow : Window
{
    // Read on each click rather than when the window opens, so a MAC address
    // learned in the meantime is used.
    private readonly Func<IReadOnlyList<KnownMachine>> _targets;

    public WakeWindow(Func<IReadOnlyList<KnownMachine>> targets, string description)
    {
        InitializeComponent();
        _targets = targets;
        TargetsTextBlock.Text = description;
    }

    private async void OnWakeClicked(object sender, RoutedEventArgs e)
    {
        WakeButton.IsEnabled = false;
        ResultTextBlock.Text = string.Empty;
        ProgressTextBlock.Text = "Enviando...";

        var machines = _targets();
        var known = machines
            .Select(machine => (machine.Name, MacAddress: WakeOnLan.TryParseMacAddress(machine.MacAddress, out var mac) ? mac : null))
            .ToList();
        var toWake = known.Where(machine => machine.MacAddress is not null).Select(machine => machine.MacAddress!).ToList();

        var (sent, lastError) = toWake.Count == 0
            ? (0, null)
            : await Task.Run(() => WakeOnLanSender.Send(toWake, WakeOnLanSender.BroadcastTargets(WakeOnLanSender.LocalNetworks())));

        var lines = known.Select(machine =>
            machine.MacAddress is null
                ? $"✗ {machine.Name}: o Midview ainda não conhece a placa de rede dela. Ligue-a uma vez com o MidviewMaster aberto, ou reinstale com o pacote de instalação atual."
                : sent > 0
                    ? $"✓ {machine.Name}: pedido enviado (placa {WakeOnLan.FormatMacAddress(machine.MacAddress)})"
                    : $"✗ {machine.Name}: o pedido não pôde ser enviado ({lastError ?? "sem rede"})");
        ResultTextBlock.Text = string.Join("\n", lines);

        var unknown = known.Count - toWake.Count;
        ProgressTextBlock.Text = toWake.Count == 0
            ? "Nenhuma dessas máquinas tem a placa de rede conhecida ainda."
            : sent == 0
                ? "O pedido não pôde ser enviado: este computador está na rede?"
                : (toWake.Count == 1 ? "Pedido enviado para 1 máquina." : $"Pedido enviado para {toWake.Count} máquinas.")
                  + (unknown == 0 ? string.Empty : $" {unknown} sem a placa de rede conhecida.");
        WakeButton.Content = "Enviar de novo";
        WakeButton.IsEnabled = true;
    }
}
