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
    private IReadOnlyList<MachineBlockState> _states = Array.Empty<MachineBlockState>();
    private bool _preselected;
    private bool _loading;
    private bool _applying;

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
        private string _displayName = string.Empty;
        private string _detail = string.Empty;

        public required string ExeName { get; init; }

        // From the list of what's open (as opposed to typed, or already
        // blocked): such a row goes away when the program is no longer open
        // and isn't ticked.
        public bool FromOpenList { get; init; }

        public required string DisplayName
        {
            get => _displayName;
            set => Set(ref _displayName, value);
        }

        public required string Detail
        {
            get => _detail;
            set => Set(ref _detail, value);
        }

        public bool Checked
        {
            get => _checked;
            set => Set(ref _checked, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (!EqualityComparer<T>.Default.Equals(field, value))
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }
    }

    // The state first, then what's open: the teacher can already apply or lift
    // while the lists load (offline machines make them slow).
    private async Task LoadAsync()
    {
        if (_loading)
        {
            return;
        }

        _loading = true;
        UpdateButtons();
        StateTextBlock.Text = "Lendo o estado atual...";
        try
        {
            var states = await ReadStatesAsync();
            _states = states;
            StateTextBlock.Text = AppBlockText.Summarize(states);
            if (!_preselected)
            {
                _preselected = true;
                Preselect(AppBlockText.CommonActivePolicy(states));
            }

            // A machine's Session is asked only once its Agent has answered:
            // same package, same version, so the Session knows the question.
            var asked = _targets.Where((_, index) => states[index].Status is { OldAgent: false }).ToList();
            var (open, answered) = await ReadOpenProgramsAsync(asked);
            MergeOpenPrograms(open, answered);
        }
        finally
        {
            _loading = false;
            UpdateButtons();
        }
    }

    private async Task RefreshStateAsync()
    {
        _states = await ReadStatesAsync();
        StateTextBlock.Text = AppBlockText.Summarize(_states);
    }

    // One answer per target, in the targets' order, all asked at once: an
    // offline machine costs its connection timeout once, not once per machine.
    private Task<MachineBlockState[]> ReadStatesAsync() =>
        Task.WhenAll(_targets.Select(machine => Task.Run(() => ReadState(machine))));

    private MachineBlockState ReadState(KnownMachine machine)
    {
        try
        {
            return new MachineBlockState(machine.Name, AppBlockClient.GetStatus(machine, _masterCertificate));
        }
        catch (Exception)
        {
            return new MachineBlockState(machine.Name, null);
        }
    }

    private async Task<(IReadOnlyList<OpenProgramRow> Rows, int Answered)> ReadOpenProgramsAsync(IReadOnlyList<KnownMachine> machines)
    {
        var answers = await Task.WhenAll(machines.Select(machine => Task.Run(() => ReadOpenPrograms(machine))));
        var answered = answers.Where(answer => answer is not null).Select(answer => answer!).ToList();
        return (OpenProgramsAggregator.Aggregate(answered), answered.Count);
    }

    private IReadOnlyList<OpenProgram>? ReadOpenPrograms(KnownMachine machine)
    {
        try
        {
            return AppBlockClient.ListOpenPrograms(machine, _masterCertificate);
        }
        catch (Exception)
        {
            // Offline, no user logged in, or an older Session: left out.
            return null;
        }
    }

    // What every machine already follows is chosen to begin with, so that
    // changing the block is a matter of editing it.
    private void Preselect(AppBlockPolicy? current)
    {
        if (current is null)
        {
            return;
        }

        (current.Mode == AppBlockMode.AllowOnly ? AllowOnlyRadioButton : BlockListRadioButton).IsChecked = true;
        foreach (var program in current.Programs)
        {
            _rows.Add(new ProgramRow
            {
                ExeName = program,
                DisplayName = program,
                Detail = current.Mode == AppBlockMode.AllowOnly ? "— permitido agora" : "— bloqueado agora",
                Checked = true,
            });
        }
    }

    private void MergeOpenPrograms(IReadOnlyList<OpenProgramRow> open, int answered)
    {
        var openByName = open.ToDictionary(program => program.ExeName, StringComparer.Ordinal);

        foreach (var row in _rows.ToList())
        {
            if (openByName.TryGetValue(row.ExeName, out var program))
            {
                row.DisplayName = $"{program.DisplayName} ({program.ExeName})";
                row.Detail = OpenDetail(program, answered);
            }
            else if (row.FromOpenList)
            {
                if (row.Checked)
                {
                    row.Detail = "— não está aberto agora";
                }
                else
                {
                    _rows.Remove(row);
                }
            }
        }

        var known = _rows.Select(row => row.ExeName).ToHashSet(StringComparer.Ordinal);
        foreach (var program in open)
        {
            if (known.Add(program.ExeName))
            {
                _rows.Add(new ProgramRow
                {
                    ExeName = program.ExeName,
                    DisplayName = $"{program.DisplayName} ({program.ExeName})",
                    Detail = OpenDetail(program, answered),
                    FromOpenList = true,
                });
            }
        }
    }

    private string OpenDetail(OpenProgramRow program, int answered) =>
        _targets.Count == 1 ? "— aberto agora" : $"— aberto em {program.MachineCount} de {answered} máquina(s)";

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
        var replaces = _states.Any(state => state.Status is { Policy.IsActive: true })
            ? "\n\nIsso substitui o bloqueio que já está valendo."
            : string.Empty;
        var question = mode == AppBlockMode.AllowOnly
            ? $"Permitir só {programs.Count} programa(s) em {where}?\n\n"
              + "Todo o resto que o aluno abrir é fechado na hora, e o que já estiver aberto também (trabalho não salvo é perdido). "
              + "O Windows e o Midview continuam funcionando; o Prompt de Comando e o PowerShell também são fechados, a menos que estejam na lista.\n\n"
              + "Atenção: alguns programas usam processos auxiliares sem janela (o Terminal do Windows usa o OpenConsole.exe, por exemplo), "
              + "e os aplicativos da Microsoft Store (Calculadora, Fotos) não aparecem na lista. Se algo fechar sozinho, "
              + "digite também o nome do arquivo auxiliar. Teste antes em uma máquina."
              + replaces
            : $"Bloquear {programs.Count} programa(s) em {where}?\n\nSe estiverem abertos, são fechados (trabalho não salvo é perdido)."
              + replaces;
        if (MessageBox.Show(this, question, "Midview", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        await ApplyAsync(new AppBlockPolicy(NextSavedAt(), mode, programs));
    }

    private async void OnLiftClicked(object sender, RoutedEventArgs e)
    {
        var where = _targets.Count == 1 ? _targets[0].Name : $"as {_targets.Count} máquinas da turma";
        if (MessageBox.Show(this, $"Liberar todos os programas em {where}?", "Midview",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }
        await ApplyAsync(AppBlockPolicy.Lifted(NextSavedAt()));
    }

    // Newer than any block a machine reported, so it wins even when this PC's
    // clock is behind another teacher's.
    private long NextSavedAt() => AppBlockText.NextSavedAt(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), _states);

    private async Task ApplyAsync(AppBlockPolicy policy)
    {
        if (_applying)
        {
            return;
        }

        _applying = true;
        UpdateButtons();
        ProgressTextBlock.Text = "Aplicando...";
        ResultTextBlock.Text = string.Empty;

        try
        {
            var results = await Task.WhenAll(_targets.Select(machine => Task.Run(() => ApplyOn(machine, policy))));
            var applied = results.Count(result => result.Applied);
            ProgressTextBlock.Text = applied == results.Length
                ? results.Length == 1 ? "Concluído." : $"Concluído nas {results.Length} máquinas."
                : $"Concluído em {applied} de {results.Length}.";
            var lines = results.Select(result => $"{(result.Applied ? "✓" : "✗")} {result.Name}: {result.Detail}").ToList();
            var keptNewer = results.Any(result => result.KeptNewer);
            if (keptNewer)
            {
                lines.Add("Alguma máquina já tinha um bloqueio mais novo (de outro professor, ou salvo com o relógio adiantado) e o seu não valeu nela. "
                          + "Veja o estado atual acima e aplique de novo se quiser trocar.");
            }
            ResultTextBlock.Text = string.Join("\n", lines);

            // Only a class-wide block is remembered and resent to machines that
            // were off; a one-machine block is sent once. A block some machine
            // turned down for a newer one isn't the class's block.
            if (!keptNewer)
            {
                _onClassPolicyApplied?.Invoke(policy);
            }
        }
        finally
        {
            _applying = false;
            UpdateButtons();
        }

        await RefreshStateAsync();
    }

    private (string Name, bool Applied, bool KeptNewer, string Detail) ApplyOn(KnownMachine machine, AppBlockPolicy policy)
    {
        if (machine.AgentThumbprint is not { Length: > 0 })
        {
            return (machine.Name, false, false, "falta a identidade do Agent (reinstale ou preencha em \"Testar / bloquear sites...\")");
        }
        try
        {
            var (succeeded, detail) = AppBlockClient.Apply(machine, _masterCertificate, policy);
            var keptNewer = AppBlockText.KeptNewer(succeeded, detail);
            return (machine.Name, succeeded && !keptNewer, keptNewer, AppBlockText.ApplyLine(succeeded, detail));
        }
        catch (Exception ex)
        {
            return (machine.Name, false, false, FailureText.DescribeAgent(ex));
        }
    }

    private void UpdateButtons()
    {
        ApplyButton.IsEnabled = LiftButton.IsEnabled = AddButton.IsEnabled = !_applying;
        RefreshButton.IsEnabled = !_applying && !_loading;
    }
}
