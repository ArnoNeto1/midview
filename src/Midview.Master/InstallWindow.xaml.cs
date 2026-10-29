using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using Midview.Protocol;

namespace Midview.Master;

// Installs one program on one machine or the whole class (InstallClient, one
// connection per machine to its Agent, which runs the installer as SYSTEM:
// no UAC prompt, no password). Each machine's line shows where it is: queued,
// sending, installing, then how it went.
public partial class InstallWindow : Window
{
    // As many uploads at once as "Enviar arquivo" (an installer is as big as
    // the files a teacher sends); a machine that has the whole file frees its
    // place while it installs, so the slow part runs on every machine at once.
    private const int MachinesAtATime = 4;

    private readonly IReadOnlyList<KnownMachine> _targets;
    private readonly X509Certificate2 _masterCertificate;
    private string[] _lines = Array.Empty<string>();
    private string? _filePath;

    // wholeClass: targets is every machine in the classroom list (TargetsText).
    public InstallWindow(IReadOnlyList<KnownMachine> targets, bool wholeClass, X509Certificate2 masterCertificate)
    {
        InitializeComponent();
        _targets = targets;
        _masterCertificate = masterCertificate;
        TargetsTextBlock.Text = TargetsText.Describe(TargetsPreposition.In, targets, wholeClass);
    }

    private void OnChooseFileClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Escolher o instalador ou script",
            Filter = "Instaladores e scripts (*.exe; *.msi; *.bat; *.cmd)|*.exe;*.msi;*.bat;*.cmd",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        _filePath = null;
        ResultTextBlock.Text = string.Empty;
        KindTextBlock.Visibility = Visibility.Collapsed;
        var name = Path.GetFileName(dialog.FileName);
        try
        {
            var size = new FileInfo(dialog.FileName).Length;
            if (!ProgramInstall.IsInstallerName(name))
            {
                FileTextBlock.Text = $"{name} não é um instalador ou script (.exe, .msi, .bat ou .cmd).";
            }
            else if (size > ProgramInstall.MaxFileSize)
            {
                FileTextBlock.Text = $"{name} passa de 4 GB e não pode ser enviado.";
            }
            else
            {
                var kind = InstallerDetection.Detect(dialog.FileName);
                FileTextBlock.Text = $"{name} ({SizeText.Describe(size)})";
                KindTextBlock.Text = InstallerDetection.Describe(kind);
                KindTextBlock.Visibility = Visibility.Visible;
                ArgumentsTextBox.Text = InstallerDetection.DefaultArguments(kind);
                _filePath = dialog.FileName;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            FileTextBlock.Text = $"{name} não pôde ser lido: {ErrorText.Describe(ex)}";
        }
        ArgumentsTextBox.IsEnabled = InstallButton.IsEnabled = _filePath is not null;
    }

    private async void OnInstallClicked(object sender, RoutedEventArgs e)
    {
        if (_filePath is not { } filePath)
        {
            return;
        }
        var arguments = ArgumentsTextBox.Text.Trim();
        if (Encoding.UTF8.GetByteCount(arguments) > ProgramInstall.MaxArgumentsBytes || arguments.Any(char.IsControl))
        {
            ProgressTextBlock.Text = "As opções do instalador têm de caber em uma linha curta.";
            return;
        }

        // Not the whole window: the machines' lines must still scroll while
        // the installers run.
        ChooseButton.IsEnabled = ArgumentsTextBox.IsEnabled = InstallButton.IsEnabled = false;
        _lines = _targets.Select(machine => $"… {machine.Name}: na fila").ToArray();
        ResultTextBlock.Text = string.Join("\n", _lines);
        var finished = 0;
        var going = $"Instalando {Path.GetFileName(filePath)} em {_targets.Count} máquina(s). Pode levar vários minutos.";
        ProgressTextBlock.Text = going;

        using var slots = new SemaphoreSlim(MachinesAtATime);
        var results = await Task.WhenAll(_targets.Select(async (machine, index) =>
        {
            var installed = await InstallOn(machine, index, filePath, arguments, slots);
            // Back on the window's thread here (the awaits resume on it).
            finished++;
            if (finished < _targets.Count)
            {
                ProgressTextBlock.Text = $"{going}\n{finished} de {_targets.Count} máquina(s) terminaram.";
            }
            return installed;
        }));

        var succeeded = results.Count(installed => installed);
        ProgressTextBlock.Text = succeeded == results.Length
            ? results.Length == 1 ? "Instalado." : $"Instalado nas {results.Length} máquinas."
            : $"Instalado em {succeeded} de {results.Length} máquina(s).";
        ChooseButton.IsEnabled = ArgumentsTextBox.IsEnabled = InstallButton.IsEnabled = true;
    }

    private async Task<bool> InstallOn(KnownMachine machine, int index, string filePath, string arguments, SemaphoreSlim slots)
    {
        await slots.WaitAsync();
        // Freed once: when the file is on the machine, or when it ends sooner.
        var released = 0;
        void Release()
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
            {
                slots.Release();
            }
        }

        try
        {
            Show(index, $"… {machine.Name}: enviando");
            var result = await Task.Run(() =>
            {
                if (string.IsNullOrEmpty(machine.AgentThumbprint))
                {
                    return new InstallResult(false, "pareie-a de novo em \"Máquinas da sala\"");
                }
                try
                {
                    var shownPercent = -1;
                    return InstallClient.Install(machine, _masterCertificate, filePath, arguments,
                        progress: (sent, total) =>
                        {
                            var percent = (int)(sent * 100 / total);
                            if (percent != shownPercent)
                            {
                                shownPercent = percent;
                                Dispatcher.BeginInvoke(() => Show(index, $"… {machine.Name}: enviando {percent}%"));
                            }
                        },
                        uploaded: () =>
                        {
                            Release();
                            var since = DateTime.Now;
                            Dispatcher.BeginInvoke(() => Show(index, $"… {machine.Name}: instalando (desde {since:HH:mm})"));
                        });
                }
                catch (Exception ex)
                {
                    return new InstallResult(false, FailureText.DescribeAgent(ex));
                }
            });

            var detail = result.OldAgent ? "Midview antigo nesta máquina: atualize-a pela rede" : result.Detail;
            Show(index, $"{(result.Succeeded ? "✓" : "✗")} {machine.Name}: {detail}");
            return result.Succeeded;
        }
        finally
        {
            Release();
        }
    }

    private void Show(int index, string line)
    {
        _lines[index] = line;
        ResultTextBlock.Text = string.Join("\n", _lines);
    }
}
