using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Midview.Master;

// The cable part: on the "cabo" step the cable is looked at every second
// (never on the window's thread: every cable call goes through Task.Run, and
// the handlers run on the Dispatcher, so each await comes back through it;
// the list's stages are sent with Dispatcher.BeginInvoke); once one allowed phone is there, the
// Midview finishes it (PhoneCable.Prepare) and puts it in the class. The
// cable tool is stopped when the window closes, after a preparation still
// running ends.
public partial class PreparePhonesWindow
{
    private static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(1);

    private DispatcherTimer _pollTimer = null!;
    private IAdb? _adb;
    private PhoneCable? _cable;
    private bool _checking;
    private bool _preparing;
    private bool _closed;

    // What the cable step says it sees now.
    private string _cableWait = WaitingText;

    // The phone prepared last, until "Preparar o próximo".
    private KnownMachine? _preparedPhone;

    private const string WaitingText = "Esperando o celular no cabo...";

    private bool OnCableStep => _progress.Page == SetupPage.Step && _progress.StepId == "cabo";

    private void InitializeCable()
    {
        _pollTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = PollEvery };
        _pollTimer.Tick += (_, _) => LookAtCable();
        InitializeReady();
        Closed += OnClosedCable;
        FetchApp();
    }

    private PhoneCable Cable()
    {
        _adb ??= _host.Adb();
        return _cable ??= new PhoneCable(_adb);
    }

    // The cable is looked at only while its step is shown and nothing else
    // uses it; the ready pages keep their own timer.
    private void UpdateCableWatch()
    {
        var polling = !_closed && OnCableStep && !_preparing;
        if (polling && !_pollTimer.IsEnabled)
        {
            _cableWait = WaitingText;
            _pollTimer.Start();
            LookAtCable();
        }
        else if (!polling)
        {
            _pollTimer.Stop();
        }
        UpdateReadyWatch();
    }

    // "Liguei o cabo e toquei em Permitir".
    private void CheckCableNow() => LookAtCable();

    private async void LookAtCable()
    {
        if (_checking || _preparing || _closed || !OnCableStep)
        {
            return;
        }
        _checking = true;
        CableProblem? problem;
        try
        {
            var cable = Cable();
            problem = await Task.Run(() => cable.Check());
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or Win32Exception or UnauthorizedAccessException)
        {
            problem = CableProblem.NoPhone;
        }
        finally
        {
            _checking = false;
        }
        if (_closed)
        {
            // Closed while the cable was looked at: the tool is stopped only now.
            StopCableTool();
            return;
        }
        if (!OnCableStep)
        {
            return;
        }

        if (problem is CableProblem.NoPhone or CableProblem.Unauthorized or CableProblem.TooMany)
        {
            ShowCableWait(problem switch
            {
                CableProblem.Unauthorized =>
                    "O celular está no cabo. Falta tocar em Permitir no celular (marque \"Sempre permitir deste computador\").",
                CableProblem.TooMany => "Há mais de um celular no cabo. Deixe só um.",
                _ => WaitingText,
            });
            return;
        }
        // The phone is there, allowed: the rest (or what is wrong with it)
        // is said by the list. Only with the phone app in hand.
        if (_apkPath is null)
        {
            ShowCableWait(_appFetching
                ? "O celular está no cabo. Esperando o Midview terminar de baixar o app do celular..."
                : _appProblem ?? WaitingText);
            return;
        }
        if (_progress.CableSeen())
        {
            ShowPage();
            Prepare();
        }
    }

    private void ShowCableWait(string text)
    {
        _cableWait = text;
        CableWaitText.Text = text;
    }

    private async void Prepare()
    {
        if (_wifi is not { } wifi || _apkPath is not { } apk || _progress.Number is not { } number)
        {
            // "Tentar de novo" without the phone app (or the Wi-Fi): back to
            // the cable step, which says what lacks (no Wi-Fi: its page first,
            // then back to the cable), instead of a page with no button.
            _progress.CableStopped();
            if (_wifi is null)
            {
                _progress.ChangeWifi();
            }
            ShowPage();
            if (OnCableStep)
            {
                ShowCableWait(_appProblem ?? "Esperando o Midview baixar o app do celular...");
            }
            return;
        }
        _preparing = true;
        UpdateCableWatch();
        var cable = Cable();
        var thumbprint = _host.MasterThumbprint;
        CableResult result;
        try
        {
            result = await Task.Run(() => cable.Prepare(
                apk, number, thumbprint, wifi.Ssid, wifi.Password,
                stage => Dispatcher.BeginInvoke(() => OnCableStage(stage))));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or Win32Exception or UnauthorizedAccessException)
        {
            result = new CableResult(null, CableProblem.Lost, false);
        }
        finally
        {
            _preparing = false;
        }

        // A phone made the school's own trusts this PC: it goes in the class
        // even when the window was closed meanwhile. OwnerUnconfirmed counts
        // as ready; the 5 minutes tell whether it joins.
        if (result.Phone is { } phone)
        {
            _preparedPhone = phone;
            _host.AddPhone(phone);
        }
        if (_closed)
        {
            StopCableTool();
            return;
        }
        if (result.Phone is not null)
        {
            _progress.CablePrepared(DateTimeOffset.Now, result.OwnerUnconfirmed);
        }
        else
        {
            _progress.CableFailed(result.Problem ?? CableProblem.Refused);
        }
        ShowPage();
    }

    private void OnCableStage(CableStage stage)
    {
        if (_closed)
        {
            return;
        }
        _progress.CableStageReached(stage);
        if (_progress.Page == SetupPage.Cable)
        {
            FillCableList();
        }
    }

    private void OnClosedCable(object? sender, EventArgs e)
    {
        _closed = true;
        _pollTimer.Stop();
        StopReadyWatch();
        _appCancel.Cancel();
        // A look at the cable or a preparation still running stops the tool when it ends.
        if (!_preparing && !_checking)
        {
            StopCableTool();
        }
    }

    // Stops the cable tool's server (seconds at worst): never on the window's thread.
    private void StopCableTool()
    {
        if (_adb is IDisposable tool)
        {
            _adb = null;
            _cable = null;
            Task.Run(tool.Dispose);
        }
    }
}
