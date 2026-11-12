using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Midview.Master;

// The pages the cable leads to (the list, what failed, "Pronto", "ainda não
// apareceu"), the wait for the phone to join the class, and the phone app
// fetched when the window opens.
public partial class PreparePhonesWindow
{
    private static readonly TimeSpan SearchEvery = TimeSpan.FromSeconds(10);

    // The items of the list, in the order PhoneCable goes through them.
    private static readonly (CableStage Stage, string Text, string? Hint)[] CableItems =
    {
        (CableStage.Checking, "Conferindo o celular", null),
        (CableStage.Installing, "Instalando o Midview no celular", "Se o celular perguntar se pode instalar, toque em Instalar."),
        (CableStage.Wifi, "Entregando o Wi-Fi do laboratório", null),
        (CableStage.Pairing, "Dando o número e pareando com este computador", null),
        (CableStage.Owner, "Transformando em celular da escola", null),
    };

    private DispatcherTimer _readyTimer = null!;
    private bool _searching;

    private readonly CancellationTokenSource _appCancel = new();
    private string? _apkPath;
    private bool _appFetching;

    // Why the phone app is not in hand, for the teacher; null while fetching or when it is.
    private string? _appProblem;

    private void InitializeReady()
    {
        _readyTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = SearchEvery };
        _readyTimer.Tick += (_, _) => WatchForPhone();
    }

    // Draws the cable list, the failure, or the ready pages.
    private void ShowCablePage()
    {
        var name = _progress.Name;
        switch (_progress.Page)
        {
            case SetupPage.Cable:
                CableTitle.Text = $"O Midview está terminando o {name}";
                FillCableList();
                Footer(back: false, next: null);
                break;
            case SetupPage.CableFailed:
                var stepNumber = _progress.ProblemStepNumber;
                FailedTitle.Text = $"Não deu para terminar o {name}";
                FailedText.Text = _progress.ProblemText ?? "";
                FailedHint.Text = _progress.ProblemStepId is { } id
                    ? $"Volte ao passo {stepNumber} ({PhoneSetupGuide.Steps[stepNumber!.Value - 1].Title}), confira e toque em \"Tentar de novo\". " +
                      "O celular ainda não é da escola: dá para recomeçar sem problema."
                    : _progress.Problem == CableProblem.TooOld ? "Separe este celular e prepare outro." : "";
                FailedHint.Visibility = Shown(FailedHint.Text.Length > 0);
                Footer(back: stepNumber is not null, next: "Tentar de novo",
                    other: stepNumber is null ? "Preparar outro celular" : null, backText: $"Voltar ao passo {stepNumber}");
                break;
            case SetupPage.Ready:
                ReadyTitle.Text = _progress.ReadyText(_phonesInClass(), PhoneTotalFile.Load());
                ReadyHint.Text = $"No celular aparece \"Pronto. Este é o {name} da escola.\" Ele aparece no filtro Celulares quando entrar no Wi-Fi.";
                ShowReadyStatus();
                Footer(back: false, next: "Preparar o próximo", other: "Terminei");
                break;
            case SetupPage.NotAppeared:
                NotAppearedTitle.Text = $"O {name} ainda não apareceu no Midview";
                NotAppearedHint.Text = "A preparação deu certo: o celular já confia neste computador. Falta ele entrar no Wi-Fi. Confira:";
                NotAppearedList.Children.Clear();
                foreach (var help in PhoneSetupGuide.NotAppeared)
                {
                    NotAppearedList.Children.Add(new Border { Style = (Style)FindResource("Card"), Margin = new Thickness(0, 0, 0, 8), Child = HelpBlock(help) });
                }
                SearchAgainText.Text = "";
                SearchAgainButton.IsEnabled = !_searching;
                Footer(back: false, next: "Preparar o próximo", other: "Terminei");
                break;
        }
    }

    // One row per item: done, being done now, or still to come.
    private void FillCableList()
    {
        CableList.Children.Clear();
        var reached = _progress.Stage is { } stage ? Array.FindIndex(CableItems, item => item.Stage == stage) : -1;
        for (var i = 0; i < CableItems.Length; i++)
        {
            var (_, text, hint) = CableItems[i];
            var (glyph, brush, label) = i < reached
                ? ("", "OnlineBrush", "feito")
                : i == reached ? ("", "AccentBrush", "agora") : ("", "TextMutedBrush", "");
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            var mark = new TextBlock { Text = glyph, FontSize = 18, Width = 30, VerticalAlignment = VerticalAlignment.Top };
            mark.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            mark.SetResourceReference(TextBlock.ForegroundProperty, brush);
            DockPanel.SetDock(mark, Dock.Left);
            row.Children.Add(mark);
            var words = new StackPanel();
            var title = new TextBlock { Text = text, FontSize = 14, TextWrapping = TextWrapping.Wrap };
            if (i == reached)
            {
                title.FontWeight = FontWeights.SemiBold;
            }
            else if (i > reached)
            {
                title.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
            }
            words.Children.Add(title);
            if (i == reached && hint is not null)
            {
                words.Children.Add(new TextBlock { Text = hint, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
            }
            mark.ToolTip = label.Length > 0 ? label : null;
            row.Children.Add(words);
            CableList.Children.Add(row);
        }
    }

    private void ShowReadyStatus()
    {
        var name = _progress.Name;
        ReadyStatus.Text = _progress.HasAppeared
            ? $"O {name} já entrou na turma, no filtro Celulares."
            : $"Procurando o {name} na rede... Pode preparar o próximo enquanto isso.";
    }

    // While "Pronto" waits for the phone: a search of the network every 10 s
    // for 5 minutes, and a look at whether it answers.
    private void UpdateReadyWatch()
    {
        var watching = !_closed && _preparedPhone is not null && !_progress.HasAppeared
                       && _progress.Page is SetupPage.Ready or SetupPage.NotAppeared;
        if (watching && !_readyTimer.IsEnabled)
        {
            _readyTimer.Start();
        }
        else if (!watching)
        {
            _readyTimer.Stop();
        }
    }

    private void StopReadyWatch() => _readyTimer.Stop();

    private async void WatchForPhone()
    {
        if (_closed || _preparedPhone is null)
        {
            return;
        }
        var now = DateTimeOffset.Now;
        var before = _progress.Page;
        _progress.Tick(now);
        if (_progress.Page != before)
        {
            ShowPage();
        }
        if (_progress.Page == SetupPage.Ready && _progress.ReadyAt is { } ready && now - ready < PhoneSetupProgress.NotAppearedAfter)
        {
            await SearchNetwork();
        }
        CheckAppeared();
    }

    private async Task SearchNetwork()
    {
        if (_searching)
        {
            return;
        }
        _searching = true;
        try
        {
            await _host.SearchNetwork();
        }
        finally
        {
            _searching = false;
        }
    }

    private void CheckAppeared()
    {
        if (_closed || _preparedPhone is not { } phone || _progress.HasAppeared || !_host.IsOnline(phone))
        {
            return;
        }
        var before = _progress.Page;
        _progress.Appeared();
        if (_progress.Page != before)
        {
            ShowPage();
        }
        else if (_progress.Page == SetupPage.Ready)
        {
            ShowReadyStatus();
            UpdateReadyWatch();
        }
    }

    private async void OnSearchAgainClicked(object sender, RoutedEventArgs e)
    {
        SearchAgainButton.IsEnabled = false;
        SearchAgainText.Text = "Procurando...";
        await SearchNetwork();
        if (_closed)
        {
            return;
        }
        CheckAppeared();
        if (_progress.Page == SetupPage.NotAppeared)
        {
            SearchAgainButton.IsEnabled = true;
            SearchAgainText.Text = $"O {_progress.Name} ainda não apareceu.";
        }
    }

    // The phone app, fetched once when the window opens (and on "Tentar de novo").
    private async void FetchApp()
    {
        _appFetching = true;
        _appProblem = null;
        ShowApp();
        var token = _appCancel.Token;
        string? path = null;
        string? problem;
        try
        {
            var (result, apkPath) = await Task.Run(() => _host.GetApp(token), token);
            problem = result switch
            {
                AgentApkResult.Ready => null,
                AgentApkResult.Invalid => "O app do celular baixado não é o do Midview. Tente mais tarde.",
                _ => "Sem internet: o Midview precisa baixar o app do celular uma vez.",
            };
            path = problem is null ? apkPath : null;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problem = "O Midview não conseguiu guardar o app do celular neste computador. Tente de novo.";
        }
        finally
        {
            _appFetching = false;
        }
        if (_closed)
        {
            return;
        }
        _apkPath = path;
        _appProblem = problem;
        ShowApp();
    }

    private void OnAppRetryClicked(object sender, RoutedEventArgs e)
    {
        if (!_appFetching)
        {
            FetchApp();
        }
    }

    // The card "Sem internet..." before the cable, and step 1 closed while it shows.
    private void ShowApp()
    {
        var page = _progress.Page;
        var problem = _appProblem;
        AppCard.Visibility = Shown(problem is not null && page is SetupPage.Wifi or SetupPage.Gather or SetupPage.Step);
        AppText.Text = problem ?? "";
        AppRetryButton.IsEnabled = !_appFetching;
        if (page == SetupPage.Step && _progress.StepId == "numero")
        {
            NextButton.IsEnabled = problem is null;
        }
        if (OnCableStep && !_preparing)
        {
            // The cable step says what still lacks; the next look at the cable moves on.
            ShowCableWait(problem ?? (_appFetching ? "Baixando o app do celular... Pode ligar o cabo enquanto isso." : _cableWait));
        }
    }
}
