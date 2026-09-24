using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace Midview.Master;

// The teacher's side of installing Midview on the lab (see InstallPackage):
// authorize this Master in the package before going around the lab, then
// import the machines it was installed on.
public partial class LabInstallWindow : Window
{
    private readonly string _masterThumbprint;
    private readonly Func<IReadOnlyList<KnownMachine>, (int Added, int Updated, int Unchanged)> _importToRoster;
    private string? _packageDirectory;

    public LabInstallWindow(
        string masterThumbprint, Func<IReadOnlyList<KnownMachine>, (int Added, int Updated, int Unchanged)> importToRoster)
    {
        InitializeComponent();
        _masterThumbprint = masterThumbprint;
        _importToRoster = importToRoster;
    }

    private void OnChoosePackageClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Escolher a pasta Midview-Instalacao" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        AuthorizeResultTextBlock.Text = string.Empty;
        ImportResultTextBlock.Text = string.Empty;
        if (InstallPackage.IsPackageDirectory(dialog.FolderName))
        {
            _packageDirectory = dialog.FolderName;
            PackageTextBlock.Text = dialog.FolderName;
        }
        else
        {
            _packageDirectory = null;
            PackageTextBlock.Text = $"{dialog.FolderName} não é um pacote de instalação (falta {InstallPackage.StudentInstallerFileName}).";
        }
        AuthorizeButton.IsEnabled = ImportButton.IsEnabled = _packageDirectory is not null;
    }

    private void OnAuthorizeClicked(object sender, RoutedEventArgs e)
    {
        if (_packageDirectory is not { } packageDirectory)
        {
            return;
        }

        try
        {
            var authorized = InstallPackage.AuthorizeMaster(packageDirectory, _masterThumbprint);
            AuthorizeResultTextBlock.Text = authorized.Count == 1
                ? "Autorizado. As máquinas instaladas com este pacote vão aceitar este professor."
                : $"Autorizado. O pacote autoriza {authorized.Count} professores (os outros já estavam nele).";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            AuthorizeResultTextBlock.Text = $"Não foi possível gravar no pacote: {ex.Message}";
        }
    }

    private void OnImportClicked(object sender, RoutedEventArgs e)
    {
        if (_packageDirectory is not { } packageDirectory)
        {
            return;
        }

        var (machines, problems) = InstallPackage.LoadInstalledMachines(
            packageDirectory, useComputerName: UseComputerNameCheckBox.IsChecked == true);
        var lines = new List<string>();
        if (machines.Count == 0)
        {
            lines.Add(problems.Count == 0
                ? $"Nenhuma ficha em \"{InstallPackage.MachinesDirectoryName}\" ainda: instale nas máquinas primeiro."
                : "Nenhuma máquina pôde ser importada.");
        }
        else
        {
            try
            {
                var (added, updated, unchanged) = _importToRoster(machines);
                lines.Add($"{machines.Count} máquina(s) importada(s): {added} nova(s), {updated} atualizada(s), {unchanged} sem mudança.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lines.Add($"As máquinas estão na grade, mas a lista não pôde ser gravada em disco: {ex.Message}");
            }
        }

        if (problems.Count > 0)
        {
            lines.Add("Fichas com problema:");
            lines.AddRange(problems.Select(problem => $"• {problem}"));
        }
        ImportResultTextBlock.Text = string.Join("\n", lines);
    }
}
