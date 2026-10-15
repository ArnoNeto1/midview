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

    // wholeClass: targets is every machine in the classroom list (TargetsText).
    public SendFileWindow(IReadOnlyList<KnownMachine> targets, bool wholeClass, X509Certificate2 masterCertificate)
    {
        InitializeComponent();
        _targets = targets;
        _masterCertificate = masterCertificate;
        TargetsTextBlock.Text = TargetsText.Describe(TargetsPreposition.To, targets, wholeClass);
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
            FileTextBlock.Text = $"{Path.GetFileName(dialog.FileName)} ({SizeText.Describe(size)})";
            _filePath = dialog.FileName;
        }
        ResultTextBlock.Text = string.Empty;
        SendButton.IsEnabled = _filePath is not null;
        ShowProgramHint();
    }

    private void OnAfterSendChanged(object sender, RoutedEventArgs e) => ShowProgramHint();

    private void ShowProgramHint()
    {
        // Checked events can fire while the window is still being built.
        if (ProgramHintTextBlock is null || OpenFileRadioButton is null)
        {
            return;
        }
        var program = OpenFileRadioButton.IsChecked == true && _filePath is { } path && !FileOpenPolicy.MayOpen(path);
        ProgramHintTextBlock.Visibility = program ? Visibility.Visible : Visibility.Collapsed;
    }

    private SendFileOptions ChosenOptions() => new(
        OverwriteCheckBox.IsChecked == true,
        OpenFileRadioButton.IsChecked == true ? AfterSend.OpenFile
        : OpenFolderRadioButton.IsChecked == true ? AfterSend.OpenFolder
        : AfterSend.Nothing);

    private async void OnSendClicked(object sender, RoutedEventArgs e)
    {
        if (_filePath is not { } filePath)
        {
            return;
        }

        SendButton.IsEnabled = false;
        ResultTextBlock.Text = string.Empty;
        var options = ChosenOptions();
        var finished = 0;
        ProgressTextBlock.Text = $"Enviando... 0 de {_targets.Count} concluídas";

        using var slots = new SemaphoreSlim(MachinesAtATime);
        var results = await Task.WhenAll(_targets.Select(async machine =>
        {
            await slots.WaitAsync();
            try
            {
                return await Task.Run(() => SendTo(machine, filePath, options));
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

    private (string Name, bool Saved, string Detail) SendTo(KnownMachine machine, string filePath, SendFileOptions options)
    {
        try
        {
            var (saved, detail) = FileSender.Send(machine, _masterCertificate, filePath, options);
            return (machine.Name, saved, detail);
        }
        catch (Exception ex)
        {
            return (machine.Name, false, FailureText.Describe(ex));
        }
    }
}
