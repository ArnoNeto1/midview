using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Midview.Protocol;

namespace Midview.Master;

// Blocks programs on one machine or the whole class (see AppBlockPolicy):
// the teacher picks a mode and programs (from what's open across the class,
// or typed) and applies it; "Liberar tudo" lifts it. The block lasts until
// lifted; the Agent keeps it and enforces it, the Session only lists what's
// open.
public partial class AppBlockWindow : Window
{
    private readonly IReadOnlyList<KnownMachine> _targets;
    private readonly X509Certificate2 _masterCertificate;
    private readonly Action<AppBlockPolicy>? _onClassPolicyApplied;
    private readonly ObservableCollection<ProgramRow> _rows = new();
    private bool _busy;

    // onClassPolicyApplied is set only when the target is the whole class, so
    // the Master remembers and resends it; a one-machine block isn't kept.
    public AppBlockWindow(
        IReadOnlyList<KnownMachine> targets, X509Certificate2 masterCertificate,
        Action<AppBlockPolicy>? onClassPolicyApplied = null)
    {
        InitializeComponent();
        _targets = targets;
        _masterCertificate = masterCertificate;
        _onClassPolicyApplied = onClassPolicyApplied;
        TargetsTextBlock.Text = targets.Count == 1
            ? $"Em: {targets[0].Name}"
            : $"Na turma toda ({targets.Count} máquinas)";
        ProgramsList.ItemsSource = _rows;
        Loaded += async (_, _) => await LoadAsync();
    }

    private sealed class ProgramRow : INotifyPropertyChanged
    {
        private bool _checked;

        public required string ExeName { get; init; }
        public required string DisplayName { get; init; }
        public required string Detail { get; set; }

        public bool Checked
        {
            get => _checked;
            set
            {
                if (_checked != value)
                {
                    _checked = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Checked)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private async Task LoadAsync()
    {
        _busy = true;
        UpdateButtons();
        StateTextBlock.Text = "Lendo o estado atual...";
        var (state, open) = await Task.Run(() => (ReadState(), ReadOpenPrograms()));
        StateTextBlock.Text = state;
        MergeOpenPrograms(open);
        _busy = false;
        UpdateButtons();
    }

    private string ReadState()
    {
        var blocked = new List<string>();
        int off = 0, oldAgent = 0, noAnswer = 0;
        foreach (var machine in _targets)
        {
            try
            {
                var status = AppBlockClient.GetStatus(machine, _masterCertificate);
                if (status.OldAgent)
                {
                    oldAgent++;
                }
                else if (status.Policy is { IsActive: true } policy)
                {
                    blocked.Add($"{machine.Name}: {AppBlockText.Describe(policy)}");
                }
                else
                {
                    off++;
                }
            }
            catch (Exception)
            {
                noAnswer++;
            }
        }

        var parts = new List<string>();
        if (off > 0)
        {
            parts.Add($"{off} sem bloqueio");
        }
        parts.AddRange(blocked);
        if (oldAgent > 0)
        {
            parts.Add($"{oldAgent} com Midview de versão anterior");
        }
        if (noAnswer > 0)
        {
            parts.Add($"{noAnswer} sem resposta");
        }
        return parts.Count == 0 ? "Nenhuma máquina respondeu." : string.Join(" · ", parts);
    }

    private IReadOnlyList<OpenProgramRow> ReadOpenPrograms()
    {
        var answers = new List<IReadOnlyList<OpenProgram>>();
        foreach (var machine in _targets)
        {
            try
            {
                answers.Add(AppBlockClient.ListOpenPrograms(machine, _masterCertificate));
            }
            catch (Exception)
            {
                // Offline, no user logged in, or an older Session: left out.
            }
        }
        return OpenProgramsAggregator.Aggregate(answers);
    }

    private void MergeOpenPrograms(IReadOnlyList<OpenProgramRow> open)
    {
        var known = _rows.Select(row => row.ExeName).ToHashSet(StringComparer.Ordinal);
        foreach (var program in open)
        {
            if (known.Add(program.ExeName))
            {
                _rows.Add(new ProgramRow
                {
                    ExeName = program.ExeName,
                    DisplayName = $"{program.DisplayName} ({program.ExeName})",
                    Detail = _targets.Count == 1 ? string.Empty : $"— aberto em {program.MachineCount} máquina(s)",
                });
            }
        }
    }

    private void OnRefreshClicked(object sender, RoutedEventArgs e) => _ = LoadAsync();

    private void OnAddKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnAddClicked(sender, e);
        }
    }

    private void OnAddClicked(object sender, RoutedEventArgs e)
    {
        var name = AppBlockPolicy.NormalizeExeName(AddTextBox.Text);
        if (name is null)
        {
            AddHintTextBlock.Text = "Nome inválido (use o nome do arquivo, por exemplo chrome.exe).";
            return;
        }
        if (ProtectedPrograms.IsProtected(name))
        {
            AddHintTextBlock.Text = "Esse programa faz parte do Windows ou do Midview e não pode ser bloqueado.";
            return;
        }

        var existing = _rows.FirstOrDefault(row => row.ExeName == name);
        if (existing is not null)
        {
            existing.Checked = true;
        }
        else
        {
            _rows.Insert(0, new ProgramRow { ExeName = name, DisplayName = name, Detail = "— digitado", Checked = true });
        }
        AddTextBox.Clear();
        AddHintTextBlock.Text = "Adicionar pelo nome (por exemplo chrome.exe).";
    }

    private async void OnApplyClicked(object sender, RoutedEventArgs e)
    {
        var mode = AllowOnlyRadioButton.IsChecked == true ? AppBlockMode.AllowOnly : AppBlockMode.BlockList;
        var programs = _rows.Where(row => row.Checked).Select(row => row.ExeName).ToList();
        if (mode == AppBlockMode.BlockList && programs.Count == 0)
        {
            ProgressTextBlock.Text = "Escolha pelo menos um programa, ou use \"Liberar tudo\".";
            return;
        }

        var where = _targets.Count == 1 ? _targets[0].Name : $"as {_targets.Count} máquinas da turma";
        var question = mode == AppBlockMode.AllowOnly
            ? $"Permitir só {programs.Count} programa(s) em {where}?\n\nTodo o resto que o aluno abrir é fechado na hora, e o que já estiver aberto também (trabalho não salvo é perdido). O Windows e o Midview continuam funcionando."
            : $"Bloquear {programs.Count} programa(s) em {where}?\n\nSe estiverem abertos, são fechados (trabalho não salvo é perdido).";
        if (MessageBox.Show(this, question, "Midview", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        await ApplyAsync(new AppBlockPolicy(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), mode, programs));
    }

    private async void OnLiftClicked(object sender, RoutedEventArgs e)
    {
        var where = _targets.Count == 1 ? _targets[0].Name : $"as {_targets.Count} máquinas da turma";
        if (MessageBox.Show(this, $"Liberar todos os programas em {where}?", "Midview",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }
        await ApplyAsync(AppBlockPolicy.Lifted(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
    }

    private async Task ApplyAsync(AppBlockPolicy policy)
    {
        _busy = true;
        UpdateButtons();
        ProgressTextBlock.Text = "Aplicando...";
        ResultTextBlock.Text = string.Empty;

        var results = await Task.WhenAll(_targets.Select(machine => Task.Run(() => ApplyOn(machine, policy))));
        var failures = results.Count(result => !result.Succeeded);
        ProgressTextBlock.Text = failures == 0
            ? results.Length == 1 ? "Concluído." : $"Concluído nas {results.Length} máquinas."
            : $"Concluído em {results.Length - failures} de {results.Length}.";
        ResultTextBlock.Text = string.Join("\n", results.Select(result =>
            $"{(result.Succeeded ? "✓" : "✗")} {result.Name}: {result.Detail}"));

        // Only a class-wide block is remembered and resent to machines that
        // were off; a one-machine block is sent once.
        if (_onClassPolicyApplied is { } applied && _targets.Count > 1)
        {
            applied(policy);
        }

        _busy = false;
        UpdateButtons();
    }

    private (string Name, bool Succeeded, string Detail) ApplyOn(KnownMachine machine, AppBlockPolicy policy)
    {
        if (machine.AgentThumbprint is not { Length: > 0 })
        {
            return (machine.Name, false, "falta a identidade do Agent (reinstale ou preencha em \"Testar / bloquear sites...\")");
        }
        try
        {
            var (succeeded, detail) = AppBlockClient.Apply(machine, _masterCertificate, policy);
            return (machine.Name, succeeded, AppBlockText.ApplyLine(succeeded, detail));
        }
        catch (Exception ex)
        {
            return (machine.Name, false, FailureText.DescribeAgent(ex));
        }
    }

    private void UpdateButtons()
    {
        ApplyButton.IsEnabled = LiftButton.IsEnabled = RefreshButton.IsEnabled = AddButton.IsEnabled = !_busy;
    }
}
