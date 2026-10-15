using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Midview.Protocol;

namespace Midview.Master;

// Blocks the whole internet or one site on one machine or on the whole
// class, through each machine's Agent (hosts file and firewall; see the
// site-blocking design). Takes the Agent's thumbprint from the classroom
// list, filled in by "Importar máquinas instaladas".
public partial class SiteBlockWindow : Window
{
    private readonly IReadOnlyList<KnownMachine> _targets;
    private readonly X509Certificate2 _masterCertificate;

    // wholeClass: targets is every machine in the classroom list (TargetsText).
    public SiteBlockWindow(IReadOnlyList<KnownMachine> targets, bool wholeClass, X509Certificate2 masterCertificate)
    {
        InitializeComponent();
        _targets = targets;
        _masterCertificate = masterCertificate;
        TargetsTextBlock.Text = TargetsText.Describe(TargetsPreposition.In, targets, wholeClass);
    }

    private async void OnBlockInternetClicked(object sender, RoutedEventArgs e) =>
        await SendAsync(MessageType.BlockInternet, Array.Empty<byte>(), "internet bloqueada");

    private async void OnUnblockInternetClicked(object sender, RoutedEventArgs e) =>
        await SendAsync(MessageType.UnblockInternet, Array.Empty<byte>(), "internet liberada");

    private async void OnBlockSiteClicked(object sender, RoutedEventArgs e)
    {
        if (Site() is { } site)
        {
            await SendAsync(MessageType.BlockDomain, Encoding.UTF8.GetBytes(site), $"{site} bloqueado");
        }
    }

    private async void OnUnblockSiteClicked(object sender, RoutedEventArgs e)
    {
        if (Site() is { } site)
        {
            await SendAsync(MessageType.UnblockDomain, Encoding.UTF8.GetBytes(site), $"{site} liberado");
        }
    }

    private string? Site()
    {
        var site = SiteTextBox.Text.Trim();
        if (site.Length == 0)
        {
            ProgressTextBlock.Text = "Escreva o site primeiro, por exemplo youtube.com.";
            return null;
        }
        return site;
    }

    // Every machine at once: one short command each.
    private async Task SendAsync(MessageType command, byte[] payload, string done)
    {
        IsEnabled = false;
        ResultTextBlock.Text = string.Empty;
        ProgressTextBlock.Text = "Enviando...";

        var results = await Task.WhenAll(_targets.Select(machine => Task.Run(() => SendTo(machine, command, payload, done))));

        var failures = results.Count(result => !result.Succeeded);
        ProgressTextBlock.Text = failures == 0
            ? results.Length == 1 ? "Feito." : $"Feito nas {results.Length} máquinas."
            : $"Feito em {results.Length - failures} de {results.Length}.";
        ResultTextBlock.Text = string.Join("\n", results.Select(result =>
            $"{(result.Succeeded ? "✓" : "✗")} {result.Name}: {result.Detail}"));
        IsEnabled = true;
    }

    private (string Name, bool Succeeded, string Detail) SendTo(KnownMachine machine, MessageType command, byte[] payload, string done)
    {
        if (string.IsNullOrEmpty(machine.AgentThumbprint))
        {
            return (machine.Name, false,
                "a lista da sala não tem o certificado do Agent desta máquina: pareie-a de novo em \"Máquinas da sala\"");
        }
        try
        {
            var succeeded = CommandClient.SendCommand(
                machine.HostAddress, ConnectionClient.AgentPort, machine.AgentThumbprint, _masterCertificate, command, payload);
            return (machine.Name, succeeded, succeeded ? done
                : command is MessageType.BlockDomain or MessageType.UnblockDomain
                    ? "não foi possível (confira se o endereço do site está certo)"
                    : "não foi possível");
        }
        catch (Exception ex)
        {
            return (machine.Name, false, FailureText.DescribeAgent(ex));
        }
    }
}
