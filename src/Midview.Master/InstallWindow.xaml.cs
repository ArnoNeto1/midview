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
// sending, installing, then how it went. A phone's agent takes an Android
// app (.apk) the same way and installs it silently as the Device Owner: an
// .apk goes to the phones among the machines, any other installer to the PCs
// (PhoneProgramsText.InstallTargets), and the window says who is left out.
public partial class InstallWindow : MidviewWindow
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
        TargetsTextBlock.Text = PhoneProgramsText.Describe(targets, wholeClass);
        HintTextBlock.Text = PhoneProgramsText.InstallHint(targets);
        ChooseButton.Content = PhoneProgramsText.ChooseButton(targets);
        FileTextBlock.Text = PhoneProgramsText.NothingChosen(targets);
        if (PhoneProgramsText.OnlyPhones(targets))
        {
            Title = DialogHeading.Heading = targets.Count == 1 ? "Instalar app no celular" : "Instalar app nos celulares";
            ArgumentsPanel.Visibility = Visibility.Collapsed;
        }
        else if (PhoneProgramsText.HasPhones(targets))
        {
            HintTextBlock.Text += " " + PhoneProgramsText.MixedInstallNote;
        }
    }

    private void OnChooseFileClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = PhoneProgramsText.OnlyPhones(_targets) ? "Escolher o app (.apk)" : "Escolher o instalador ou script",
            Filter = PhoneProgramsText.FileFilter(_targets),
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) == true)
        {
            ChooseFile(dialog.FileName);
        }
    }

    // The file the teacher picked: checked, described, and ready to send when
    // the machines take it.
    internal void ChooseFile(string fileName)
    {
        _filePath = null;
        ResultTextBlock.Text = string.Empty;
        KindTextBlock.Visibility = Visibility.Collapsed;
        var name = Path.GetFileName(fileName);
        try
        {
            var size = new FileInfo(fileName).Length;
            if (PhoneProgramsText.FileProblem(name, size, _targets) is { } problem)
            {
                FileTextBlock.Text = problem;
            }
            else if (ProgramInstall.IsApkName(name))
            {
                FileTextBlock.Text = $"{name} ({SizeText.Describe(size)})";
                KindTextBlock.Text = Joined(PhoneProgramsText.ApkKind, PhoneProgramsText.InstallLeftOut(_targets, name));
                KindTextBlock.Visibility = Visibility.Visible;
                ArgumentsPanel.Visibility = Visibility.Collapsed;
                ArgumentsTextBox.Text = string.Empty;
                _filePath = fileName;
            }
            else
            {
                var kind = InstallerDetection.Detect(fileName);
                FileTextBlock.Text = $"{name} ({SizeText.Describe(size)})";
                KindTextBlock.Text = Joined(InstallerDetection.Describe(kind), PhoneProgramsText.InstallLeftOut(_targets, name));
                KindTextBlock.Visibility = Visibility.Visible;
                ArgumentsPanel.Visibility = Visibility.Visible;
                ArgumentsTextBox.Text = InstallerDetection.DefaultArguments(kind);
                _filePath = fileName;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            FileTextBlock.Text = $"{name} não pôde ser lido: {ErrorText.Describe(ex)}";
        }
        ArgumentsTextBox.IsEnabled = InstallButton.IsEnabled = _filePath is not null;
    }

    private static string Joined(string first, string? second) => second is null ? first : $"{first}\n{second}";

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

        // An .apk to the phones, any other installer to the PCs.
        var apk = ProgramInstall.IsApkName(filePath);
        var targets = PhoneProgramsText.InstallTargets(_targets, filePath);
        if (apk)
        {
            arguments = string.Empty;
        }
        var unit = apk ? "celular(es)" : "máquina(s)";

        // Not the whole window: the machines' lines must still scroll while
        // the installers run.
        ChooseButton.IsEnabled = ArgumentsTextBox.IsEnabled = InstallButton.IsEnabled = false;
        _lines = targets.Select(machine => $"… {machine.Name}: na fila").ToArray();
        ResultTextBlock.Text = string.Join("\n", _lines);
        var finished = 0;
        var going = PhoneProgramsText.Installing(filePath, targets.Count);
        ProgressTextBlock.Text = going;

        using var slots = new SemaphoreSlim(MachinesAtATime);
        var results = await Task.WhenAll(targets.Select(async (machine, index) =>
        {
            var installed = await InstallOn(machine, index, filePath, arguments, slots);
            // Back on the window's thread here (the awaits resume on it).
            finished++;
            if (finished < targets.Count)
            {
                ProgressTextBlock.Text = $"{going}\n{finished} de {targets.Count} {unit} terminaram.";
            }
            return installed;
        }));

        var succeeded = results.Count(installed => installed);
        ProgressTextBlock.Text = succeeded == results.Length
            ? results.Length == 1 ? "Instalado." : apk ? $"Instalado nos {results.Length} celulares." : $"Instalado nas {results.Length} máquinas."
            : succeeded == 0 ? "Não instalado."
            : $"Instalado em {succeeded} de {results.Length} {unit}.";
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
                    return new InstallResult(false, machine.IsPhone ? PhoneAgentCommand.NoIdentity : "pareie-a de novo em \"Máquinas da sala\"");
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
                    return new InstallResult(false, machine.IsPhone ? FailureText.DescribePhoneAgent(ex) : FailureText.DescribeAgent(ex));
                }
            });

            var detail = machine.IsPhone ? PhoneProgramsText.InstallLine(result.Succeeded, result.Detail)
                : result.OldAgent ? "Midview antigo nesta máquina: atualize-a pela rede" : result.Detail;
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
