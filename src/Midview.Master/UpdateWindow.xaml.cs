using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;
using Midview.Protocol;

namespace Midview.Master;

// Pushes a new build of the student program to the lab (NetworkUpdate). One
// machine goes first, and the others only after it confirms: a build that
// doesn't work stops at one machine (which rolls itself back) instead of
// reaching the whole lab.
public partial class UpdateWindow : Window
{
    private const int MaxConcurrentTransfers = 4;

    // One run at a time, whichever window started it (UI thread only).
    private static bool s_running;

    private readonly string _packageDirectory;
    private readonly Func<IReadOnlyList<KnownMachine>> _targets;
    private readonly X509Certificate2 _masterCertificate;
    private readonly CancellationTokenSource _closing = new();
    private BuildManifest? _manifest;
    private string _fingerprintHex = string.Empty;
    private List<KnownMachine> _machines = new();
    private string[] _lines = Array.Empty<string>();
    private bool _busy;
    private bool _updating;
    private bool _closed;
    private volatile bool _stopRequested;

    private enum Outcome
    {
        Updated,
        AlreadyInstalled,
        Skipped,
        Failed,
        NoAnswer,
    }

    public UpdateWindow(string packageDirectory, Func<IReadOnlyList<KnownMachine>> targets, X509Certificate2 masterCertificate)
    {
        InitializeComponent();
        _packageDirectory = packageDirectory;
        _targets = targets;
        _masterCertificate = masterCertificate;
        PackageTextBlock.Text = $"Pacote: {packageDirectory}";
        Loaded += async (_, _) => await IdentifyPackageAsync();

        // Also when the window goes with its owner, which ignores a
        // cancelled Closing: nothing new starts and in-flight work stops
        // waiting.
        Closed += (_, _) =>
        {
            _closed = true;
            _stopRequested = true;
            _closing.Cancel();
        };
    }

    // On opening, and again before every check or update: the package may
    // have been rebuilt in the same folder since (Gerar-Pacote -Output <the
    // same folder>).
    private async Task IdentifyPackageAsync()
    {
        try
        {
            var (manifest, fingerprint) = await Task.Run(() => UpdatePackage.Identify(_packageDirectory));
            _manifest = manifest;
            _fingerprintHex = Convert.ToHexString(fingerprint);
            BuildTextBlock.Text = $"Versão do pacote: {UpdateText.ShortBuild(manifest.Build)}";
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _manifest = null;
            _fingerprintHex = string.Empty;
            BuildTextBlock.Text = ex.Message;
        }
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        CheckButton.IsEnabled = UpdateButton.IsEnabled = _manifest is not null && !_busy;
        StopButton.IsEnabled = _updating && !_stopRequested;
    }

    private async void OnCheckClicked(object sender, RoutedEventArgs e)
    {
        _busy = true;
        UpdateButtons();
        ProgressTextBlock.Text = "Verificando as versões...";
        await IdentifyPackageAsync();
        if (_manifest is not null)
        {
            await CheckAllAsync();
        }
        ProgressTextBlock.Text = string.Empty;
        _busy = false;
        UpdateButtons();
    }

    private void OnStopClicked(object sender, RoutedEventArgs e)
    {
        _stopRequested = true;
        UpdateButtons();
        ProgressTextBlock.Text = "Parando: nenhuma máquina nova vai começar; as que já estão trocando de versão terminam.";
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_updating)
        {
            return;
        }

        var answer = MessageBox.Show(this,
            "Uma atualização está em andamento. Fechar a janela para de acompanhar: as máquinas que ainda não " +
            "confirmaram a versão nova voltam sozinhas para a anterior em alguns minutos.\n\nFechar mesmo assim?",
            "Midview", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer == MessageBoxResult.Yes)
        {
            _stopRequested = true;
            _closing.Cancel();
        }
        else
        {
            e.Cancel = true;
        }
    }

    private async void OnUpdateClicked(object sender, RoutedEventArgs e)
    {
        if (s_running)
        {
            ProgressTextBlock.Text = "Já há uma atualização pela rede em andamento, em outra janela.";
            return;
        }

        _busy = true;
        UpdateButtons();
        try
        {
            ProgressTextBlock.Text = "Verificando as versões...";
            await IdentifyPackageAsync();
            if (_manifest is not { } manifest)
            {
                ProgressTextBlock.Text = string.Empty;
                return;
            }
            var packageFingerprint = _fingerprintHex;
            var statuses = await CheckAllAsync();
            if (_closed)
            {
                return;
            }
            var candidates = Enumerable.Range(0, _machines.Count)
                .Where(index => statuses[index] is { OldAgent: false, Report: { } report }
                                && !string.Equals(report.Fingerprint, _fingerprintHex, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (candidates.Count == 0)
            {
                ProgressTextBlock.Text = "Nenhuma máquina para atualizar (veja a lista abaixo).";
                return;
            }

            var newer = candidates.Count(index => UpdateText.IsNewer(statuses[index]!.Report!.Build, manifest.Build));
            if (!Confirm(candidates.Count, manifest.Build, newer) || _closed)
            {
                ProgressTextBlock.Text = string.Empty;
                return;
            }

            // Again: another window may have started while this one checked
            // and asked (all windows share this thread, and nothing is
            // awaited from here to the assignment).
            if (s_running)
            {
                ProgressTextBlock.Text = "Já há uma atualização pela rede em andamento, em outra janela.";
                return;
            }

            s_running = _updating = true;
            _stopRequested = false;
            UpdateButtons();
            await RunAsync(candidates, packageFingerprint);
        }
        finally
        {
            if (_updating)
            {
                s_running = _updating = false;
            }
            _busy = false;
            UpdateButtons();
        }
    }

    private bool Confirm(int count, string packageBuild, int newer)
    {
        var text =
            $"Atualizar {count} máquina(s) para a versão {UpdateText.ShortBuild(packageBuild)}?\n\n" +
            "• Os alunos podem continuar usando o computador.\n" +
            "• Você perde a visão e o controle dessas máquinas por 1 a 3 minutos.\n" +
            "• Bloqueios de teclado e mouse são desfeitos; mensagens e conversas abertas fecham.\n" +
            "• Não desligue as máquinas até terminar.\n" +
            "• Os professores que cada máquina aceita não mudam.\n\n" +
            "Uma máquina é atualizada primeiro; as outras só depois que ela confirmar que a versão nova funciona.";
        if (newer > 0)
        {
            text += $"\n\n⚠ {newer} máquina(s) têm uma versão MAIS NOVA que a do pacote e vão voltar para a do pacote.";
        }
        return MessageBox.Show(this, text, "Midview", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
               == MessageBoxResult.Yes;
    }

    // packageFingerprint: the build the machines were checked and the
    // teacher confirmed against.
    private async Task RunAsync(IReadOnlyList<int> candidates, string packageFingerprint)
    {
        ProgressTextBlock.Text = "Preparando o pacote...";
        UpdatePackage package;
        try
        {
            package = await Task.Run(() => UpdatePackage.Create(_packageDirectory, Path.GetTempPath()));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            ProgressTextBlock.Text = $"O pacote não pôde ser preparado: {ex.Message}";
            return;
        }

        using (package)
        {
            if (!string.Equals(Convert.ToHexString(package.Fingerprint), packageFingerprint, StringComparison.OrdinalIgnoreCase))
            {
                ProgressTextBlock.Text = "O pacote mudou na pasta desde a verificação. Clique em Atualizar de novo.";
                return;
            }

            // Machines are tried one at a time until one proves the build
            // works (or fails, which stops everything); a machine that
            // refused before trying proves nothing.
            var queue = new Queue<int>(candidates);
            var proven = false;
            ProgressTextBlock.Text = "Atualizando a primeira máquina...";
            while (!proven && queue.Count > 0 && !_stopRequested)
            {
                var outcome = await RunOneAsync(queue.Dequeue(), package, releaseSlot: null);
                if (outcome is Outcome.Failed or Outcome.NoAnswer)
                {
                    foreach (var index in queue)
                    {
                        SetLine(index, "• não iniciada: a primeira máquina não confirmou a versão nova");
                    }
                    ProgressTextBlock.Text = "A primeira máquina não confirmou a versão nova; as outras não foram atualizadas.";
                    return;
                }
                proven = outcome is Outcome.Updated or Outcome.AlreadyInstalled;
            }

            ProgressTextBlock.Text = "Atualizando as outras máquinas...";
            using var slots = new SemaphoreSlim(MaxConcurrentTransfers);
            var stopAll = false;
            await Task.WhenAll(queue.Select(async index =>
            {
                await slots.WaitAsync();
                var released = false;
                void Release()
                {
                    if (!released)
                    {
                        released = true;
                        slots.Release();
                    }
                }

                try
                {
                    if (_stopRequested || stopAll)
                    {
                        SetLine(index, "• não iniciada (atualização parada)");
                        return;
                    }
                    var outcome = await RunOneAsync(index, package, Release);
                    if (outcome is Outcome.Failed)
                    {
                        stopAll = true;
                    }
                }
                finally
                {
                    Release();
                }
            }));
        }

        var done = _lines.Count(line => line.StartsWith('✓'));
        ProgressTextBlock.Text = _stopRequested
            ? $"Parada. {done} de {_machines.Count} máquina(s) nesta versão."
            : $"Terminado. {done} de {_machines.Count} máquina(s) nesta versão.";
    }

    // Offer and upload (holding a transfer slot), then follow this attempt,
    // by its id, until it ends or the deadline passes.
    private async Task<Outcome> RunOneAsync(int index, UpdatePackage package, Action? releaseSlot)
    {
        var machine = _machines[index];
        var attemptId = RandomNumberGenerator.GetBytes(NetworkUpdate.AttemptIdLength);
        SetLine(index, "• enviando...");

        PushResult push;
        try
        {
            push = await Task.Run(() => UpdateClient.Push(machine, _masterCertificate, package, attemptId,
                (sent, total) => Dispatcher.InvokeAsync(() => SetLine(index, $"• enviando {sent * 100 / Math.Max(1, total)}%")),
                _closing.Token));
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            SetLine(index, "✗ envio interrompido (a janela foi fechada)");
            return Outcome.Failed;
        }
        catch (Exception ex)
        {
            SetLine(index, $"✗ {FailureText.DescribeAgent(ex)}");
            return Outcome.Skipped;
        }
        finally
        {
            releaseSlot?.Invoke();
        }

        switch (push.Outcome)
        {
            case PushOutcome.AlreadyInstalled:
                SetLine(index, "✓ já nesta versão");
                return Outcome.AlreadyInstalled;
            case PushOutcome.OldAgent:
                SetLine(index, "✗ Midview anterior à atualização pela rede (atualize uma vez com o pendrive)");
                return Outcome.Skipped;
            case PushOutcome.Refused:
                SetLine(index, $"✗ {push.Detail}");
                return Outcome.Skipped;
        }

        return await FollowAsync(index, machine, attemptId, package.Manifest.Build);
    }

    private async Task<Outcome> FollowAsync(int index, KnownMachine machine, byte[] attemptId, string packageBuild)
    {
        var attemptHex = Convert.ToHexString(attemptId);
        var deadline = DateTime.UtcNow + NetworkUpdate.MasterDeadline;
        SetLine(index, "• recebida; conferindo...");
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                await Task.Delay(NetworkUpdate.PollInterval, _closing.Token);
            }
            catch (OperationCanceledException)
            {
                SetLine(index, "• a janela foi fechada: se a versão nova não for confirmada, a máquina volta para a anterior");
                return Outcome.NoAnswer;
            }

            StatusResult status;
            try
            {
                // Asking with the attempt's id is also what lets the new
                // build confirm itself.
                status = await Task.Run(() => UpdateClient.GetStatus(machine, _masterCertificate, attemptId));
            }
            catch (Exception)
            {
                SetLine(index, "• reiniciando...");
                continue;
            }

            if (status.Report?.Attempt is not { } attempt || attempt.Id != attemptHex)
            {
                continue;
            }
            switch (attempt.State)
            {
                case UpdateStates.Committed:
                    SetLine(index, $"✓ atualizada ({UpdateText.ShortBuild(packageBuild)})");
                    return Outcome.Updated;
                // Not final, but it won't commit: the machine goes back to
                // the old build when it restarts.
                case UpdateStates.RolledBack:
                case UpdateStates.Failed:
                case UpdateStates.RollbackPending:
                    SetLine(index, $"✗ {attempt.Detail}");
                    return Outcome.Failed;
                default:
                    SetLine(index, $"• {UpdateText.DescribeProgress(attempt.State)}");
                    break;
            }
        }

        SetLine(index, "• ainda sem resposta — use \"Verificar versões\" depois");
        return Outcome.NoAnswer;
    }

    private async Task<StatusResult?[]> CheckAllAsync()
    {
        _machines = _targets().ToList();
        _lines = _machines.Select(_ => "• verificando...").ToArray();
        Render();

        var results = await Task.WhenAll(_machines.Select(machine => Task.Run(() => CheckOne(machine))));
        for (var index = 0; index < results.Length; index++)
        {
            _lines[index] = results[index].Line;
        }
        Render();
        return results.Select(result => result.Status).ToArray();
    }

    private (StatusResult? Status, string Line) CheckOne(KnownMachine machine)
    {
        if (machine.AgentThumbprint is not { Length: > 0 })
        {
            return (null, "✗ sem a identidade do Agent (importe a ficha da máquina de novo)");
        }

        try
        {
            var status = UpdateClient.GetStatus(machine, _masterCertificate);
            return (status, _manifest is { } manifest
                ? UpdateText.DescribeStatus(status, _fingerprintHex, manifest.Build)
                : string.Empty);
        }
        catch (Exception ex)
        {
            return (null, $"✗ {FailureText.DescribeAgent(ex)}");
        }
    }

    private void SetLine(int index, string line)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(() => SetLine(index, line));
            return;
        }
        _lines[index] = line;
        Render();
    }

    private void Render() =>
        ResultTextBlock.Text = string.Join("\n", _machines.Select((machine, index) => $"{machine.Name}: {_lines[index]}"));
}
