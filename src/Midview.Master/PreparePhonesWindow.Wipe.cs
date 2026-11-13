using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Midview.Master;

// "Apagar e preparar de novo" on the "ligar" page of a phone that was already
// in the class. Where it stands is PhoneSetupProgress.Wipe, so redrawing the
// page never offers it again after it was done.
public partial class PreparePhonesWindow
{
    // Called by ShowStep (so, on a step page) and when a wipe changes.
    // The wipe offer, or what became of it, and what the "ligar" page shows
    // with it: after a failed wipe the page shows only the way forward
    // (recovery mode), and the steps come back once it is done.
    private void ShowWipe()
    {
        if (ShownStep is not { } step)
        {
            return;
        }
        if (step.Id != "ligar")
        {
            WipeCard.Visibility = Visibility.Collapsed;
            StepBody.Visibility = Visibility.Visible;
            Footer(back: _progress.StepIndex > 0, next: step.NextText);
            return;
        }

        var name = _progress.Name;
        var state = _progress.Wipe;
        var failed = state == WipeState.Failed;
        WipeCard.Visibility = Shown(state != WipeState.None);
        StepBody.Visibility = Shown(!failed);
        WipeText.Text = state switch
        {
            WipeState.Offered => $"O {name} ainda está ligado e na turma. O Midview apaga o {DeviceWord} daqui e ele volta ao \"Bem-vindo\" sozinho.",
            WipeState.Wiping => $"Apagando o {name}...",
            WipeState.Done => $"O {name} está sendo apagado e volta sozinho ao \"Bem-vindo\" em alguns minutos. Então siga a ajuda \"Apareceu Bem-vindo\", aberta abaixo.",
            WipeState.Failed => $"Não deu para apagar o {name} daqui: {_progress.WipeFailure?.TrimEnd('.', ' ')}. " +
                                $"Um {DeviceWord} que já foi da escola não deixa restaurar pelo menu das Configurações. " +
                                "Restaure-o pelo modo de recuperação (\"Celular travado com PIN\") e depois toque em \"Já restaurei\".",
            _ => "",
        };
        WipeButton.Visibility = Shown(state == WipeState.Offered);
        WipeRestoreButton.Visibility = Shown(failed);
        WipeRetryButton.Visibility = Shown(failed);
        // Beside the text for the short states; under it for the failure's longer one.
        DockPanel.SetDock(WipeButtons, failed ? Dock.Bottom : Dock.Right);
        WipeButtons.Margin = failed ? new Thickness(0, 10, 0, 0) : new Thickness(12, 0, 0, 0);
        WipeButtons.Visibility = Shown(state is WipeState.Offered or WipeState.Failed);

        // After a failed wipe the footer's button says the phone was restored by hand.
        Footer(back: _progress.StepIndex > 0, next: failed ? "Já restaurei, apareceu o Bem-vindo" : step.NextText, primary: !failed);
        ShowHelp(); // the failure adds "Celular travado com PIN" to the helps
    }

    // The question before wiping; replaced only by a visual check that cannot answer a dialog.
    internal Func<string, bool>? ConfirmWipe { get; set; }

    private async void OnWipeClicked(object sender, RoutedEventArgs e)
    {
        var name = _progress.Name;
        if (_wipe is null || name is null || _progress.Wipe is not (WipeState.Offered or WipeState.Failed))
        {
            return;
        }
        if (!(ConfirmWipe?.Invoke(name) ?? PhoneWipe.Confirm(this, name)))
        {
            return;
        }
        _progress.WipeStarted();
        ShowWipe();
        (bool Ok, string Detail) result;
        try
        {
            result = await _wipe(name);
        }
        catch (Exception ex)
        {
            result = (false, FailureText.DescribeAgent(ex));
        }
        // The teacher may have moved to another phone meanwhile.
        if (_progress.Name != name || _progress.Wipe != WipeState.Wiping)
        {
            return;
        }
        if (result.Ok)
        {
            _progress.WipeSucceeded();
            // The phone is on its way to "Bem-vindo": that help opens.
            ShowPage();
            WatchWipe(name);
            return;
        }
        _progress.WipeFailed(result.Detail);
        ShowWipe();
    }

    // How often the phone told to wipe is looked at (its tile's presence).
    private static readonly TimeSpan WipeCheckEvery = TimeSpan.FromSeconds(3);

    // "o celular não se apagou", or "o tablet".
    private string NotWipedText => $"o {DeviceWord} não se apagou";

    private DispatcherTimer? _wipeTimer;
    private PhoneWipeWatch? _wipeWatch;
    private string? _wipeWatchName;

    // The phone acked the wipe (here or from its tile's menu): it acks
    // before wiping, so whether it really wipes is seen only from the class.
    // Still answering 90 s later, it did not: the page says so.
    private void WatchWipe(string name)
    {
        _wipeWatch = new PhoneWipeWatch(DateTimeOffset.Now);
        _wipeWatchName = name;
        if (_wipeTimer is null)
        {
            _wipeTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = WipeCheckEvery };
            _wipeTimer.Tick += (_, _) => LookAtWipe();
            Closed += (_, _) => _wipeTimer?.Stop();
        }
        _wipeTimer.Start();
    }

    private void LookAtWipe()
    {
        if (_closed || _wipeWatch is not { } watch || _wipeWatchName is not { } name
            || _progress.Name != name || _progress.Wipe != WipeState.Done)
        {
            // Closed, or the teacher moved to another phone.
            _wipeTimer?.Stop();
            return;
        }
        switch (watch.Tick(DateTimeOffset.Now, _phoneOnline(name) == true))
        {
            case WipeWatchResult.Wiping:
                _wipeTimer?.Stop();
                break;
            case WipeWatchResult.NotWiped:
                _wipeTimer?.Stop();
                _progress.WipeNotDone(NotWipedText);
                ShowPage();
                break;
        }
    }

    // After a failed wipe: "Celular travado com PIN" (recovery mode), the way that
    // needs nothing from the phone's own menu or agent. Opens that help.
    private void OnWipeRestoreClicked(object sender, RoutedEventArgs e)
    {
        _helpOpen = true;
        ShowHelp();
        ScrollToHelp();
    }
}
