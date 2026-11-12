using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Midview.Master;

// The numbered lists of the pages (the taps) and
// "Deu errado?", which opens the helps of the page.
public partial class PreparePhonesWindow
{
    private bool _helpOpen;

    private void OnHelpClicked(object sender, RoutedEventArgs e)
    {
        _helpOpen = !_helpOpen;
        ShowHelp();
        if (_helpOpen)
        {
            ScrollToHelp();
        }
    }

    // The helps open below the fold on a small screen: scroll them up into view.
    private void ScrollToHelp() =>
        Dispatcher.BeginInvoke(
            () => PageScroll.ScrollToVerticalOffset(HelpArea.TranslatePoint(new Point(0, 0), PageContent).Y),
            DispatcherPriority.Loaded);

    private void FillTaps(IReadOnlyList<string> taps)
    {
        TapsPanel.Children.Clear();
        for (var i = 0; i < taps.Count; i++)
        {
            TapsPanel.Children.Add(NumberedRow(i + 1, new TextBlock { Text = taps[i], TextWrapping = TextWrapping.Wrap, FontSize = 13 }));
        }
    }

    private FrameworkElement NumberedRow(int number, FrameworkElement content)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        var badge = new Label { Content = number.ToString(), Style = (Style)FindResource("StepBadge"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 10, 0) };
        content.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(content, 1);
        row.Children.Add(badge);
        row.Children.Add(content);
        return row;
    }

    private StackPanel HelpBlock(GuideHelp help)
    {
        var block = new StackPanel();
        block.Children.Add(new TextBlock { Text = help.Problem, FontWeight = FontWeights.SemiBold, FontSize = 13, TextWrapping = TextWrapping.Wrap });
        var answer = new TextBlock { Text = help.Answer, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        answer.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");
        block.Children.Add(answer);
        return block;
    }

    // "Deu errado?" for this page: the step's helps (the Wi-Fi's), and the
    // fixed helps they point to, so none is a dead end.
    private void ShowHelp()
    {
        var helps = HelpsOfPage();
        HelpLink.Visibility = Shown(helps.Count > 0);
        // Short when open: the footer also holds the long "próximo passo" buttons.
        HelpLinkRun.Text = _helpOpen ? "Fechar a ajuda" : "Deu errado?";
        HelpList.Children.Clear();
        HelpArea.Visibility = Shown(_helpOpen && helps.Count > 0);
        if (!_helpOpen)
        {
            return;
        }
        foreach (var help in helps)
        {
            var card = new Border { Style = (Style)FindResource("Card"), Margin = new Thickness(0, 0, 0, 8), Child = HelpBlock(help) };
            HelpList.Children.Add(card);
        }
    }

    private List<GuideHelp> HelpsOfPage()
    {
        var helps = _progress.Page switch
        {
            SetupPage.Wifi => PhoneSetupGuide.WifiHelp.ToList(),
            SetupPage.Step => _progress.Step!.Help.ToList(),
            // What the cable found: the helps of the step that fixes it.
            SetupPage.CableFailed when _progress.ProblemStepId is { } id =>
                PhoneSetupGuide.Steps.First(step => step.Id == id).Help.ToList(),
            _ => new List<GuideHelp>(),
        };
        if (_progress.Page == SetupPage.CableFailed && _progress.Problem == CableProblem.AlreadySchool)
        {
            // A school phone that does not show up in the Midview cannot be wiped from it.
            helps.Add(PhoneSetupGuide.LockedWithPin);
        }
        if (_progress.Page == SetupPage.ConfirmRedo
            || (_progress.Page == SetupPage.Step && _progress.Wipe == WipeState.Failed))
        {
            // The way out of a wipe that failed (and of redoing an offline phone): first in the list.
            helps.Insert(0, PhoneSetupGuide.LockedWithPin);
        }
        // The fixed helps an answer points to come after it, once each.
        foreach (var help in helps.ToList())
        {
            foreach (var link in help.Links ?? Array.Empty<GuideHelp>())
            {
                if (!helps.Contains(link))
                {
                    helps.Add(link);
                }
            }
        }
        return helps;
    }
}
