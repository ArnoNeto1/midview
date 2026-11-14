using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Midview.Master;

// The "chrome" step: every few seconds, while the step waits, Chrome's and
// WebView's versions are read over the cable (on a worker thread, as on the
// cable step); an old one is first installed from the copy kept on this
// computer (ChromeCopy), then, if still old, gets its Play Store page opened
// on the phone, and the step moves on by itself once both are up to date,
// after copying them to this computer for the next devices. Where it stands
// is PhoneSetupProgress.Chrome.
public partial class PreparePhonesWindow
{
    private static readonly TimeSpan ChromePollEvery = TimeSpan.FromSeconds(3);

    private DispatcherTimer _chromeTimer = null!;
    private bool _chromeChecking;

    private bool OnChromeStep => _progress.OnChromeStep;

    // The steps that talk to the phone over the cable, and so need Google's adb.
    private bool UsesCable => OnCableStep || OnChromeStep;

    private void InitializeChrome()
    {
        _chromeTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = ChromePollEvery };
        _chromeTimer.Tick += (_, _) => LookAtChrome();
    }

    // Called by UpdateCableWatch.
    private void UpdateChromeWatch()
    {
        var watching = !_closed && _progress.WatchesChrome && !ToolNeeded;
        if (watching && !_chromeTimer.IsEnabled)
        {
            _chromeTimer.Start();
            LookAtChrome();
        }
        else if (!watching)
        {
            _chromeTimer.Stop();
        }
    }

    private async void LookAtChrome()
    {
        if (_chromeChecking || _checking || _preparing || _closed || !_progress.WatchesChrome || ToolNeeded)
        {
            return;
        }
        _progress.ChromeTick(DateTimeOffset.Now);
        if (!_progress.WatchesChrome)
        {
            // Waited too long: the step says so, and the cable rests.
            ShowChrome();
            UpdateCableWatch();
            return;
        }

        _chromeChecking = true;
        var hasTool = true;
        CableProblem? problem = null;
        ChromeVersions? versions = null;
        try
        {
            if (_adb is null)
            {
                hasTool = await FindToolAsync();
            }
            if (hasTool)
            {
                var cable = Cable();
                (problem, versions) = await Task.Run(cable.LookAtChrome);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or Win32Exception or UnauthorizedAccessException)
        {
            problem = CableProblem.NoPhone;
        }
        finally
        {
            _chromeChecking = false;
        }
        if (_closed)
        {
            StopCableTool();
            return;
        }
        if (!hasTool)
        {
            ShowTool();
            UpdateCableWatch();
            return;
        }

        if (problem is null && (_progress.WantsCopy(versions) || _progress.WantsSave(versions)))
        {
            (problem, versions) = await CopyChrome(versions!);
            if (_closed)
            {
                StopCableTool();
                return;
            }
        }

        var open = _progress.ChromeLooked(problem, versions, DateTimeOffset.Now);
        if (open is not null)
        {
            await OpenStore(open);
            if (_closed)
            {
                return;
            }
        }
        if (OnChromeStep)
        {
            ShowChrome();
            UpdateCableWatch();
        }
        else
        {
            // Both up to date: on to the next step.
            ShowPage();
        }
    }

    // The copy kept on this computer (ChromeCopy): installed on a device
    // whose apps are old, before any Play Store page, or saved from one
    // whose apps are up to date. The card says so while it runs. Returns
    // the versions to go on with (read again after an install).
    private async Task<(CableProblem? Problem, ChromeVersions? Versions)> CopyChrome(ChromeVersions versions)
    {
        _chromeChecking = true;
        var installing = _progress.WantsCopy(versions);
        try
        {
            var cable = Cable();
            var store = new ChromeCopy();
            if (installing)
            {
                var result = await Task.Run(() => cable.InstallChromeCopy(store, versions, () => Started(_progress.CopyStarting)));
                _progress.CopyEnded(result);
                installing = false;
                if (result.Installed.Count == 0)
                {
                    return (null, versions);
                }
                var (problem, after) = await Task.Run(cable.LookAtChrome);
                if (problem is not null || after is null)
                {
                    return (problem, after);
                }
                versions = after;
            }
            if (_progress.WantsSave(versions))
            {
                await Task.Run(() => cable.SaveChromeCopy(store, () => Started(_progress.SaveStarting)));
                _progress.SaveEnded();
            }
            return (null, versions);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or Win32Exception or UnauthorizedAccessException)
        {
            // The copy never fails the step: on as if there were none.
            if (installing)
            {
                _progress.CopyEnded(new ChromeCopyResult(Array.Empty<string>(), null));
            }
            else
            {
                _progress.SaveEnded();
            }
            return (null, versions);
        }
        finally
        {
            _chromeChecking = false;
        }
    }

    // From the worker, right before the long part: the card says what runs.
    private void Started(Action mark) => Dispatcher.BeginInvoke(() =>
    {
        if (!_closed)
        {
            mark();
            ShowChrome();
        }
    });

    private async Task OpenStore(string package)
    {
        _chromeChecking = true;
        bool opened;
        try
        {
            var cable = Cable();
            opened = await Task.Run(() => cable.OpenStore(package));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or Win32Exception or UnauthorizedAccessException)
        {
            opened = false;
        }
        finally
        {
            _chromeChecking = false;
        }
        if (_closed)
        {
            StopCableTool();
            return;
        }
        _progress.StoreOpened(opened);
    }

    // The footer's button on the step: "Liguei o cabo..." looks now; while
    // waiting (or after the wait ran out) it opens the Play Store page again.
    private void OnChromeNext()
    {
        if (_progress.Chrome is ChromeStage.Updating or ChromeStage.TimedOut)
        {
            _progress.ChromeAgain();
        }
        ShowChrome();
        UpdateCableWatch();
        LookAtChrome();
    }

    private void OnChromeSkipClicked(object sender, RoutedEventArgs e)
    {
        _progress.SkipChrome();
        ShowPage();
    }

    // Called by ShowStep, after the footer is drawn, and after each look.
    private void ShowChrome()
    {
        if (!OnChromeStep)
        {
            ChromeCard.Visibility = Visibility.Collapsed;
            return;
        }
        var (status, detail) = ChromeTexts(DateTimeOffset.Now);
        ChromeStatusText.Text = status;
        ChromeStatusText.Style = _progress.Chrome == ChromeStage.TimedOut || _progress.StoreFailed
            ? (Style)FindResource("WarningText")
            : null;
        ChromeDetailText.Text = detail;
        ChromeDetailText.Visibility = Shown(detail.Length > 0);
        ChromeSkipLine.Visibility = Shown(_progress.Chrome != ChromeStage.Done && _progress.Work == ChromeWork.None);
        Footer(back: true, next: _progress.Chrome switch
        {
            ChromeStage.Done => ShownStep!.NextText,
            ChromeStage.Updating => "Abrir a Play Store de novo",
            ChromeStage.TimedOut => "Conferir de novo",
            _ => "Liguei o cabo e toquei em Permitir",
        }, primary: _progress.Chrome is ChromeStage.Done or ChromeStage.Looking or ChromeStage.TimedOut);
        ShowTool();
    }

    // What the card says: where the update is, then how the wait goes.
    private (string Status, string Detail) ChromeTexts(DateTimeOffset now)
    {
        var seen = _progress.ChromeSeen;
        var cable = _progress.ChromeCable switch
        {
            CableProblem.Unauthorized =>
                "O celular está no cabo. Falta tocar em Permitir no celular (marque \"Sempre permitir deste computador\").",
            CableProblem.TooMany => "Há mais de um celular no cabo. Deixe só um.",
            null => null,
            _ => WaitingText,
        };
        switch (_progress.Work)
        {
            case ChromeWork.Installing:
                return ("Instalando o Chrome guardado neste computador...",
                    "Leva cerca de 1 minuto. Se o celular perguntar, toque em Instalar.");
            case ChromeWork.Saving:
                return ("Guardando uma cópia do Chrome neste computador, para os próximos celulares...",
                    "São cerca de 400 MB: leva uns 15 segundos. Não solte o cabo.");
        }
        switch (_progress.Chrome)
        {
            case ChromeStage.Done:
                return ($"Chrome {seen?.Chrome} e Android System WebView {seen?.WebView}: atualizados ✓", "");
            case ChromeStage.TimedOut:
                return ($"Depois de {PhoneSetupProgress.ChromeTimeout.TotalMinutes:0} minutos, o {AppName(_progress.ChromeUpdating)} ainda não foi atualizado. " +
                        "Confira a Play Store no celular (a conta Google e o Wi-Fi) e toque em \"Conferir de novo\".", "");
            case ChromeStage.Updating:
                var app = AppName(_progress.ChromeUpdating);
                var copy = CopyMissText(_progress.CopyMiss) is { } miss ? miss + " " : "";
                var where = _progress.StoreFailed
                    ? $"O Midview não conseguiu abrir a Play Store no celular. No celular, abra a Play Store, toque na busca, escreva \"{app}\" e toque em Atualizar."
                    : "A Play Store abriu no celular: toque em Atualizar.";
                var waited = _progress.WaitingFor(now);
                var wait = cable ?? (waited < TimeSpan.FromMinutes(1)
                    ? "Conferindo a cada 3 segundos..."
                    : $"Conferindo a cada 3 segundos... esperando há {(int)waited.TotalMinutes} min.");
                return (UpdateLine(seen) + " " + copy + where, wait);
            default:
                return (cable ?? "Conferindo o Chrome do celular...", "");
        }
    }

    // Why the copy kept on this computer did not go: one line before the Play Store's.
    private static string? CopyMissText(ChromeCopyMiss? miss) => miss switch
    {
        ChromeCopyMiss.DoesNotFit => "A cópia do Chrome guardada neste computador não serve neste celular; atualize pela Play Store.",
        ChromeCopyMiss.Damaged => "A cópia do Chrome guardada neste computador estava danificada e foi apagada; atualize pela Play Store.",
        ChromeCopyMiss.NoSpace => "A cópia guardada não coube neste celular (falta espaço); libere espaço ou atualize pela Play Store.",
        ChromeCopyMiss.Refused => "A cópia guardada não serviu neste celular; atualize pela Play Store.",
        _ => null,
    };

    private static string AppName(string? package) =>
        package == PhoneCable.WebViewPackage ? "Android System WebView" : "Chrome";

    // "O Chrome deste celular é a versão 126...", or WebView's line once Chrome is done.
    private static string UpdateLine(ChromeVersions? seen)
    {
        if (seen is null || !seen.ChromeOk)
        {
            var version = seen?.Chrome is { } chrome ? $"é a versão {chrome}" : "não foi encontrado";
            return $"O Chrome deste celular {version}; o bloqueio de downloads precisa da {PhoneCable.ChromeMinimum} ou mais nova.";
        }
        var webView = seen.WebView is { } major ? $"está na versão {major}" : "não foi encontrado";
        return $"Chrome {seen.Chrome} ✓. Falta o Android System WebView, que {webView}.";
    }
}
