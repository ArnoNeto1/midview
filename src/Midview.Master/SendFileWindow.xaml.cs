using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using Midview.Protocol;

namespace Midview.Master;

// Sends one file to one machine or to the whole class (FileSender, one
// connection per machine).
public partial class SendFileWindow : Window
{
    // Enough to keep the lab network busy without the teacher's PC uploading
    // 25 copies of a large file at once.
    private const int MachinesAtATime = 4;

    private readonly IReadOnlyList<KnownMachine> _targets;
    private readonly X509Certificate2 _masterCertificate;
    private string? _filePath;

    public SendFileWindow(IReadOnlyList<KnownMachine> targets, X509Certificate2 masterCertificate)
    {
        InitializeComponent();
        _targets = targets;
        _masterCertificate = masterCertificate;
        TargetsTextBlock.Text = targets.Count == 1
            ? $"Para: {targets[0].Name}"
            : $"Para a turma toda ({targets.Count} máquinas)";
    }

    private void OnChooseFileClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Escolher o arquivo para enviar", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var size = new FileInfo(dialog.FileName).Length;
        if (size > FileTransfer.MaxFileSize)
        {
            FileTextBlock.Text = $"{Path.GetFileName(dialog.FileName)} passa de 1 GB e não pode ser enviado.";
            _filePath = null;
        }
        else
        {
            FileTextBlock.Text = $"{Path.GetFileName(dialog.FileName)} ({DescribeSize(size)})";
            _filePath = dialog.FileName;
        }
        ResultTextBlock.Text = string.Empty;
        SendButton.IsEnabled = _filePath is not null;
    }

    private async void OnSendClicked(object sender, RoutedEventArgs e)
    {
        if (_filePath is not { } filePath)
        {
            return;
        }

        SendButton.IsEnabled = false;
        ResultTextBlock.Text = string.Empty;
        var finished = 0;
        ProgressTextBlock.Text = $"Enviando... 0 de {_targets.Count} concluídas";

        using var slots = new SemaphoreSlim(MachinesAtATime);
        var results = await Task.WhenAll(_targets.Select(async machine =>
        {
            await slots.WaitAsync();
            try
            {
                return await Task.Run(() => SendTo(machine, filePath));
            }
            finally
            {
                // Back on the UI thread here: the awaits above resume on it.
                slots.Release();
                finished++;
                ProgressTextBlock.Text = $"Enviando... {finished} de {_targets.Count} concluídas";
            }
        }));

        var failures = results.Where(result => !result.Saved).ToList();
        var delivered = results.Length - failures.Count;
        ProgressTextBlock.Text = failures.Count == 0
            ? results.Length == 1 ? "Arquivo enviado." : $"Arquivo enviado para as {delivered} máquinas."
            : $"Enviado para {delivered} de {results.Length}.";
        ResultTextBlock.Text = string.Join("\n", results.Select(result => result.Saved
            ? $"✓ {result.Name}: salvo como {result.Detail}"
            : $"✗ {result.Name}: {result.Detail}"));
        SendButton.IsEnabled = true;
    }

    private (string Name, bool Saved, string Detail) SendTo(KnownMachine machine, string filePath)
    {
        try
        {
            var (saved, detail) = FileSender.Send(machine, _masterCertificate, filePath);
            return (machine.Name, saved, detail);
        }
        catch (Exception ex)
        {
            return (machine.Name, false, FailureText.Describe(ex));
        }
    }

    private static string DescribeSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} bytes",
        < 1024 * 1024 => $"{bytes / 1024.0:F0} KB",
        _ => $"{bytes / (1024.0 * 1024):F1} MB",
    };
}
