using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Midview.Master;

// Lists the programs installed on one machine and uninstalls the ones the
// teacher marks, on that machine or the whole class, through each Agent
// (GetInstalledPrograms / UninstallPrograms on port 28900). The list is read
// from one machine; the uninstall goes by display name, so each machine
// removes the ones it actually has.
public partial class UninstallWindow : Window
{
    private readonly KnownMachine _listFrom;
    private readonly IReadOnlyList<KnownMachine> _classTargets;
    private readonly X509Certificate2 _masterCertificate;
    private readonly List<ProgramChoice> _all = new();

    public UninstallWindow(KnownMachine listFrom, IReadOnlyList<KnownMachine> classTargets, X509Certificate2 masterCertificate)
    {
        InitializeComponent();
        _listFrom = listFrom;
        _classTargets = classTargets;
        _masterCertificate = masterCertificate;
        TargetsTextBlock.Text = $"Programas instalados em {listFrom.Name}";
        WholeClassCheckBox.Content = $"Aplicar na turma toda ({classTargets.Count} máquinas)";
    }

    private async void OnWindowLoaded(object sender, RoutedEventArgs e) => await LoadAsync();

    private async void OnReloadClicked(object sender, RoutedEventArgs e) => await LoadAsync();

    private async Task LoadAsync()
    {
        IsEnabled = false;
        ResultTextBlock.Text = string.Empty;
        ProgramsList.ItemsSource = null;
        _all.Clear();
        ProgressTextBlock.Text = $"Lendo os programas de {_listFrom.Name}...";

        try
        {
            if (string.IsNullOrEmpty(_listFrom.AgentThumbprint))
            {
                ProgressTextBlock.Text = "A lista da sala não tem o certificado do Agent desta máquina: pareie-a de novo em \"Máquinas da sala\".";
                return;
            }

            var result = await Task.Run(() => LabConfigClient.GetInstalledPrograms(_listFrom, _masterCertificate))
                .WaitAsync(TimeSpan.FromSeconds(20));
            if (result.OldAgent)
            {
                ProgressTextBlock.Text = "Midview antigo nesta máquina: atualize-a pela rede para usar isto.";
                return;
            }

            foreach (var name in result.Programs!)
            {
                _all.Add(new ProgramChoice(name));
            }
            ApplyFilter();
            ProgressTextBlock.Text = _all.Count == 0
                ? "Nenhum programa removível foi encontrado nesta máquina."
                : $"{_all.Count} programa(s). Marque os que quer desinstalar.";
        }
        catch (Exception ex)
        {
            ProgressTextBlock.Text = $"Não foi possível ler os programas: {FailureText.DescribeAgent(ex)}";
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private void OnFilterChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        var filter = FilterTextBox.Text.Trim();
        ProgramsList.ItemsSource = filter.Length == 0
            ? _all
            : _all.Where(choice => choice.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)).ToList();
    }

    private async void OnUninstallClicked(object sender, RoutedEventArgs e)
    {
        // _all keeps the checked state even while the view is filtered.
        var chosen = _all.Where(choice => choice.IsChecked).Select(choice => choice.Name).ToList();
        if (chosen.Count == 0)
        {
            ProgressTextBlock.Text = "Marque pelo menos um programa primeiro.";
            return;
        }

        var targets = WholeClassCheckBox.IsChecked == true ? _classTargets : new[] { _listFrom };
        IsEnabled = false;
        ResultTextBlock.Text = string.Empty;
        // Name what is going, so a program checked earlier and then hidden by
        // the filter isn't removed as a surprise. A machine answers only when
        // it's done with every program, which can take minutes: each one's
        // line shows as soon as it does.
        var going = $"Desinstalando em {targets.Count} máquina(s): {string.Join(", ", chosen)}. Pode levar alguns minutos.";
        ProgressTextBlock.Text = going;
        var lines = new List<string>();

        var results = await Task.WhenAll(targets.Select(async machine =>
        {
            var result = await Task.Run(() =>
            {
                if (string.IsNullOrEmpty(machine.AgentThumbprint))
                {
                    return (machine.Name, false, "pareie-a de novo em \"Máquinas da sala\"");
                }
                try
                {
                    var (ok, detail) = LabConfigClient.UninstallPrograms(machine, _masterCertificate, chosen);
                    // An Agent from before this feature answers a bare CommandAck,
                    // which decodes to (false, ""); say so instead of a blank line.
                    if (!ok && string.IsNullOrEmpty(detail))
                    {
                        return (machine.Name, false, "Midview antigo nesta máquina: atualize-a pela rede");
                    }
                    return (machine.Name, ok, detail);
                }
                catch (Exception ex)
                {
                    return (machine.Name, false, FailureText.DescribeAgent(ex));
                }
            });

            // Back on the window's thread here (the await resumes where it
            // started), so the window can be updated directly.
            lines.Add($"{(result.Item2 ? "✓" : "✗")} {result.Item1}: {result.Item3}");
            ResultTextBlock.Text = string.Join("\n", lines);
            if (lines.Count < targets.Count)
            {
                ProgressTextBlock.Text = $"{going}\n{lines.Count} de {targets.Count} máquina(s) terminaram.";
            }
            return result;
        }));

        var failures = results.Count(r => !r.Item2);
        ProgressTextBlock.Text = failures == 0
            ? results.Length == 1 ? "Feito." : $"Feito nas {results.Length} máquinas."
            : $"Terminou em {results.Length} máquina(s), com avisos em {failures}.";
        ResultTextBlock.Text = string.Join("\n", results.Select(r => $"{(r.Item2 ? "✓" : "✗")} {r.Name}: {r.Item3}"));
        IsEnabled = true;
    }

    // A program row: its name and whether the teacher marked it. A plain
    // property is enough -- the checkbox writes back to it on toggle, and the
    // list is only read when the teacher clicks Desinstalar.
    private sealed class ProgramChoice
    {
        public ProgramChoice(string name) => Name = name;

        public string Name { get; }

        public bool IsChecked { get; set; }
    }
}
