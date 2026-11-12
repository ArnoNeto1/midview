using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;
using Midview.Protocol;

namespace Midview.Master;

// "Atualizar celulares" (CELULARES): the phone agent's APK of this Master's
// version (PhoneApkSource: from GitHub, kept on this PC) sent to the phones
// over NetworkUpdate's messages. One phone goes first, and the others only
// after its new version answered: an APK that doesn't start stops at one
// phone. A phone keeps its old agent whenever the update doesn't go through.
public partial class PhoneUpdateWindow : MidviewWindow
{
    private const int MaxConcurrentTransfers = 4;

    private readonly X509Certificate2 _masterCertificate;
    private readonly PhoneApkSource _source;
    private readonly ObservableCollection<PhoneRow> _rows;
    private readonly CancellationTokenSource _closing = new();
    private StatusResult?[] _statuses;
    private bool _busy;

    private enum Outcome
    {
        Current,
        Skipped,
        Failed,
        NoAnswer,
    }

    // allPhones: phones is every phone in the classroom list.
    public PhoneUpdateWindow(
        IReadOnlyList<KnownMachine> phones, bool allPhones, X509Certificate2 masterCertificate, PhoneApkSource source)
    {
        InitializeComponent();
        _masterCertificate = masterCertificate;
        _source = source;
        Title = DialogHeading.Heading = PhoneUpdateText.Heading(phones.Count);
        TargetsTextBlock.Text = PhoneAppBlockText.Targets(phones.Select(phone => phone.Name).ToList(), allPhones);
        _rows = new ObservableCollection<PhoneRow>(phones.Select(phone => new PhoneRow(phone)));
        _statuses = new StatusResult?[_rows.Count];
        PhonesList.ItemsSource = _rows;
        ShowApk();
        Loaded += async (_, _) => await CheckAsync();
        Closed += (_, _) => _closing.Cancel();
    }

    private void ShowApk() => ApkTextBlock.Text = PhoneUpdateText.ApkLine(_source.Version, _source.IsKept());

    private void UpdateButtons() => UpdateButton.IsEnabled = CheckButton.IsEnabled = !_busy;

    private async void OnCheckClicked(object sender, RoutedEventArgs e) => await CheckAsync();

    // Closing stops following the phones; one that already got its APK
    // finishes installing it by itself.
    private void OnClosing(object? sender, CancelEventArgs e) => _closing.Cancel();

    private async Task CheckAsync()
    {
        if (_busy)
        {
            return;
        }
        _busy = true;
        UpdateButtons();
        ProgressTextBlock.Text = "Verificando as versões...";
        try
        {
            await ReadStatusesAsync();
            ProgressTextBlock.Text = string.Empty;
        }
        finally
        {
            _busy = false;
            UpdateButtons();
        }
    }

    // Every phone at once; the rows say each one's version (or why not).
    private async Task ReadStatusesAsync()
    {
        foreach (var row in _rows)
        {
            row.Show(PhoneRowState.Working, PhoneUpdateText.Checking);
        }
        var results = await Task.WhenAll(_rows.Select(row => Task.Run(() => ReadStatus(row.Phone))));
        for (var index = 0; index < _rows.Count; index++)
        {
            _statuses[index] = results[index].Status;
            _rows[index].Show(results[index].Status is { OldAgent: false, Report: not null } ? PhoneRowState.Waiting : PhoneRowState.Failed,
                results[index].Line);
        }
    }

    private (StatusResult? Status, string Line) ReadStatus(KnownMachine phone)
    {
        if (phone.AgentThumbprint is not { Length: > 0 })
        {
            return (null, "falta a identidade do celular: pareie-o de novo em \"Máquinas da sala\"");
        }
        try
        {
            var status = UpdateClient.GetStatus(phone, _masterCertificate);
            return (status, PhoneUpdateText.Installed(status));
        }
        catch (Exception ex)
        {
            return (null, FailureText.DescribePhoneAgent(ex));
        }
    }

    private async void OnUpdateClicked(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }
        _busy = true;
        UpdateButtons();
        try
        {
            if (await GetApkAsync() is not { } apk)
            {
                return;
            }
            using (apk)
            {
                ProgressTextBlock.Text = "Verificando as versões...";
                await ReadStatusesAsync();
                if (_closing.IsCancellationRequested)
                {
                    return;
                }
                await RunAsync(apk);
            }
        }
        finally
        {
            _busy = false;
            UpdateButtons();
        }
    }

    // The APK, downloading it with its progress shown when it isn't kept yet.
    private async Task<PhoneAgentApk?> GetApkAsync()
    {
        var kept = _source.IsKept();
        ProgressTextBlock.Text = kept ? "Conferindo o app do celular..." : PhoneUpdateText.Downloading(0);
        try
        {
            var percent = new Progress<int>(value => ProgressTextBlock.Text = PhoneUpdateText.Downloading(value));
            var apk = await _source.GetAsync(percent, _closing.Token);
            ShowApk();
            return apk;
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            ProgressTextBlock.Text = $"O app do celular não pôde ser obtido: {ex.Message}.";
            return null;
        }
    }

    private async Task RunAsync(PhoneAgentApk apk)
    {
        var queue = new Queue<int>();
        for (var index = 0; index < _rows.Count; index++)
        {
            if (_statuses[index] is not { } status)
            {
                continue;
            }
            switch (PhoneUpdateText.Plan(status.OldAgent ? null : status.Report, apk.Sha256Hex))
            {
                case PhoneUpdateStep.UpToDate:
                    _rows[index].Show(PhoneRowState.Done, PhoneUpdateText.UpToDate(status.Report!.Build));
                    break;
                case PhoneUpdateStep.OldAgent:
                    _rows[index].Show(PhoneRowState.Failed, PhoneUpdateText.OldAgent);
                    break;
                default:
                    _rows[index].Show(PhoneRowState.Waiting, "na fila para atualizar");
                    queue.Enqueue(index);
                    break;
            }
        }

        if (queue.Count > 0)
        {
            // One phone at a time until one proves the APK works; a phone that
            // refused before trying proves nothing.
            ProgressTextBlock.Text = "Atualizando o primeiro celular...";
            var proven = false;
            while (!proven && queue.Count > 0 && !_closing.IsCancellationRequested)
            {
                var outcome = await RunOneAsync(queue.Dequeue(), apk, releaseSlot: null);
                if (outcome is Outcome.Failed or Outcome.NoAnswer)
                {
                    foreach (var index in queue)
                    {
                        _rows[index].Show(PhoneRowState.Partial, "não começou: o primeiro celular não confirmou a versão nova");
                    }
                    ProgressTextBlock.Text = "O primeiro celular não confirmou a versão nova; os outros não foram atualizados.";
                    return;
                }
                proven = outcome == Outcome.Current;
            }

            if (queue.Count > 0)
            {
                ProgressTextBlock.Text = "Atualizando os outros celulares...";
                using var slots = new SemaphoreSlim(MaxConcurrentTransfers);
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
                        await RunOneAsync(index, apk, Release);
                    }
                    finally
                    {
                        Release();
                    }
                }));
            }
        }

        if (!_closing.IsCancellationRequested)
        {
            ProgressTextBlock.Text = PhoneUpdateText.Summary(_rows.Count(row => row.State == PhoneRowState.Done), _rows.Count);
        }
    }

    // Offer and send (holding a transfer slot), then follow this attempt by
    // its id until the new version answers, the phone says it failed, or the
    // deadline passes.
    private async Task<Outcome> RunOneAsync(int index, PhoneAgentApk apk, Action? releaseSlot)
    {
        var row = _rows[index];
        var attemptId = RandomNumberGenerator.GetBytes(NetworkUpdate.AttemptIdLength);
        row.Show(PhoneRowState.Working, PhoneUpdateText.Sending(0));
        row.Progress = 0;

        PushResult push;
        try
        {
            push = await Task.Run(() => UpdateClient.Push(row.Phone, _masterCertificate, apk, attemptId,
                (sent, total) => Dispatcher.InvokeAsync(() =>
                {
                    if (row.State == PhoneRowState.Working && row.HasProgress)
                    {
                        row.Show(PhoneRowState.Working, PhoneUpdateText.Sending(sent * 100 / Math.Max(1, total)));
                        row.Progress = (double)sent / Math.Max(1, total);
                    }
                }),
                _closing.Token));
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            return Outcome.NoAnswer;
        }
        catch (Exception ex)
        {
            row.Show(PhoneRowState.Failed, PhoneUpdateText.NotUpdated(FailureText.DescribePhoneAgent(ex)));
            return Outcome.Skipped;
        }
        finally
        {
            releaseSlot?.Invoke();
        }

        if (PhoneUpdateText.IsOldAgent(push))
        {
            row.Show(PhoneRowState.Failed, PhoneUpdateText.OldAgent);
            return Outcome.Skipped;
        }
        switch (push.Outcome)
        {
            case PushOutcome.AlreadyInstalled:
                row.Show(PhoneRowState.Done, PhoneUpdateText.UpToDate(_statuses[index]?.Report?.Build));
                return Outcome.Current;
            case PushOutcome.Refused:
                row.Show(PhoneRowState.Failed, PhoneUpdateText.NotUpdated(push.Detail));
                return Outcome.Skipped;
        }

        return await FollowAsync(row, attemptId);
    }

    private async Task<Outcome> FollowAsync(PhoneRow row, byte[] attemptId)
    {
        var attemptHex = Convert.ToHexString(attemptId);
        var deadline = DateTime.UtcNow + NetworkUpdate.MasterDeadline;
        row.Show(PhoneRowState.Working, PhoneUpdateText.Updating);
        row.Progress = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                await Task.Delay(NetworkUpdate.PollInterval, _closing.Token);
            }
            catch (OperationCanceledException)
            {
                return Outcome.NoAnswer;
            }

            StatusResult status;
            try
            {
                status = await Task.Run(() => UpdateClient.GetStatus(row.Phone, _masterCertificate, attemptId));
            }
            catch (Exception)
            {
                // The agent restarting in its new version: no answer for a few seconds.
                continue;
            }

            if (status.Report?.Attempt is not { } attempt
                || !string.Equals(attempt.Id, attemptHex, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            switch (attempt.State)
            {
                case UpdateStates.Committed:
                    row.Show(PhoneRowState.Done, PhoneUpdateText.Updated(status.Report.Build));
                    return Outcome.Current;
                case UpdateStates.Failed:
                case UpdateStates.RolledBack:
                    row.Show(PhoneRowState.Failed, PhoneUpdateText.NotUpdated(attempt.Detail));
                    return Outcome.Failed;
            }
        }

        row.Show(PhoneRowState.Partial, "ainda sem resposta: use \"Verificar versões\" daqui a pouco");
        return Outcome.NoAnswer;
    }
}
