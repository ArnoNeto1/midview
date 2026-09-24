using System.Diagnostics;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;
using Midview.Protocol;

namespace Midview.Master;

// Collects the "Entregar ao professor" folder of one machine or of the whole
// class (FileCollector, one connection per machine) into
// Documents\Midview\Recolhidos\<date time>\<machine>.
public partial class CollectFilesWindow : Window
{
    // Like SendFileWindow: keeps the network busy without 25 machines
    // uploading to the teacher's PC at once.
    private const int MachinesAtATime = 4;

    private readonly IReadOnlyList<KnownMachine> _targets;
    private readonly X509Certificate2 _masterCertificate;
    private string? _lastCollectionDirectory;

    public CollectFilesWindow(IReadOnlyList<KnownMachine> targets, X509Certificate2 masterCertificate)
    {
        InitializeComponent();
        _targets = targets;
        _masterCertificate = masterCertificate;
        TargetsTextBlock.Text = targets.Count == 1
            ? $"De: {targets[0].Name}"
            : $"Da turma toda ({targets.Count} máquinas)";
    }

    public static string CollectionsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Midview", "Recolhidos");

    private async void OnCollectClicked(object sender, RoutedEventArgs e)
    {
        var collectionDirectory = Path.Combine(CollectionsDirectory, DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss"));
        var destinations = MachineDirectories(collectionDirectory);

        CollectButton.IsEnabled = false;
        OpenFolderButton.IsEnabled = false;
        ResultTextBlock.Text = string.Empty;
        var finished = 0;
        ProgressTextBlock.Text = $"Recolhendo... 0 de {_targets.Count} concluídas";

        using var slots = new SemaphoreSlim(MachinesAtATime);
        var results = await Task.WhenAll(_targets.Select(async machine =>
        {
            await slots.WaitAsync();
            try
            {
                return (machine.Name, Result: await Task.Run(() => CollectFrom(machine, destinations[machine])));
            }
            finally
            {
                // Back on the UI thread here: the awaits above resume on it.
                slots.Release();
                finished++;
                ProgressTextBlock.Text = $"Recolhendo... {finished} de {_targets.Count} concluídas";
            }
        }));

        var reached = results.Count(result => result.Result.Succeeded);
        var files = results.Sum(result => result.Result.Files);
        var bytes = results.Sum(result => result.Result.Bytes);
        ProgressTextBlock.Text = files == 0
            ? $"Nenhum arquivo recolhido ({reached} de {results.Length} máquina(s) responderam)."
            : $"{files} arquivo(s), {SizeText.Describe(bytes)}, de {reached} de {results.Length} máquina(s). Salvos em {collectionDirectory}";
        ResultTextBlock.Text = string.Join("\n", results.Select(result => Describe(result.Name, result.Result)));

        _lastCollectionDirectory = files > 0 ? collectionDirectory : null;
        OpenFolderButton.IsEnabled = _lastCollectionDirectory is not null;
        CollectButton.IsEnabled = true;
    }

    private void OnOpenFolderClicked(object sender, RoutedEventArgs e)
    {
        if (_lastCollectionDirectory is { } directory && Directory.Exists(directory))
        {
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        }
    }

    // One folder per machine, named after it; two roster names that clean
    // up to the same folder name get "(2)".
    private Dictionary<KnownMachine, string> MachineDirectories(string collectionDirectory)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new Dictionary<KnownMachine, string>();
        foreach (var machine in _targets)
        {
            var name = FileNames.Safe(machine.Name);
            var unique = name;
            for (var copy = 2; !used.Add(unique); copy++)
            {
                unique = $"{name} ({copy})";
            }
            directories[machine] = Path.Combine(collectionDirectory, unique);
        }
        return directories;
    }

    private CollectResult CollectFrom(KnownMachine machine, string destination)
    {
        try
        {
            return FileCollector.Collect(machine, _masterCertificate, destination);
        }
        catch (Exception ex)
        {
            return new CollectResult(false, 0, 0, FailureText.Describe(ex));
        }
    }

    private static string Describe(string name, CollectResult result)
    {
        var leftOut = result.Detail.Length > 0 ? $"; {result.Detail}" : string.Empty;
        if (result.Succeeded)
        {
            return result.Files == 0
                ? $"✓ {name}: pasta vazia{leftOut}"
                : $"✓ {name}: {result.Files} arquivo(s), {SizeText.Describe(result.Bytes)}{leftOut}";
        }
        return result.Files == 0
            ? $"✗ {name}: {result.Detail}"
            : $"✗ {name}: parou depois de {result.Files} arquivo(s): {result.Detail}";
    }
}
