using Midview.Master;
using Xunit;

// The "chrome" step of the assistant: it goes on by itself for an up-to-date
// phone, opens each Play Store page once, gives up waiting after a while,
// and "Pular" goes on with a warning for this phone only.
public class PhoneSetupProgressChromeTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 13, 10, 0, 0, TimeSpan.Zero);
    private static readonly ChromeVersions Old = new(126, 126);
    private static readonly ChromeVersions ChromeOnly = new(131, 126);
    private static readonly ChromeVersions Updated = new(131, 131);

    internal static PhoneSetupProgress AtChrome()
    {
        var progress = new PhoneSetupProgress();
        progress.Start(hasWifi: true, gathered: true);
        progress.SetNumber("7");
        while (progress.StepId != "chrome")
        {
            progress.Next();
        }
        return progress;
    }

    [Fact]
    public void The_step_waits_for_the_cable_and_next_does_not_skip_it()
    {
        var progress = AtChrome();

        Assert.True(progress.WatchesChrome);
        Assert.Equal(ChromeStage.Looking, progress.Chrome);
        progress.Next();
        Assert.Equal("chrome", progress.StepId);
        Assert.Null(progress.ChromeLooked(CableProblem.Unauthorized, null, T0));
        Assert.Equal(CableProblem.Unauthorized, progress.ChromeCable);
        Assert.Equal("chrome", progress.StepId);
    }

    [Fact]
    public void An_up_to_date_phone_goes_straight_on()
    {
        var progress = AtChrome();

        Assert.Null(progress.ChromeLooked(null, Updated, T0));

        Assert.Equal("conta-google", progress.StepId);
        Assert.Equal(ChromeStage.Done, progress.Chrome);
        Assert.False(progress.WatchesChrome);
        Assert.False(progress.ChromeSkipped);
    }

    [Fact]
    public void Each_store_page_opens_once_chrome_first()
    {
        var progress = AtChrome();

        Assert.Equal(PhoneCable.ChromePackage, progress.ChromeLooked(null, Old, T0));
        progress.StoreOpened(true);
        Assert.Equal(ChromeStage.Updating, progress.Chrome);
        Assert.Null(progress.ChromeLooked(null, Old, T0.AddSeconds(3)));
        Assert.Equal(PhoneCable.WebViewPackage, progress.ChromeLooked(null, ChromeOnly, T0.AddSeconds(6)));
        Assert.Null(progress.ChromeLooked(null, ChromeOnly, T0.AddSeconds(9)));
        Assert.Equal(PhoneCable.WebViewPackage, progress.ChromeUpdating);
        Assert.Equal(TimeSpan.FromSeconds(3), progress.WaitingFor(T0.AddSeconds(9)));
    }

    [Fact]
    public void A_cable_dropped_during_the_wait_keeps_the_page_open()
    {
        var progress = AtChrome();
        progress.ChromeLooked(null, Old, T0);

        Assert.Null(progress.ChromeLooked(CableProblem.NoPhone, null, T0.AddSeconds(3)));
        Assert.Equal(CableProblem.NoPhone, progress.ChromeCable);
        Assert.Null(progress.ChromeLooked(null, Old, T0.AddSeconds(6)));
        Assert.Null(progress.ChromeCable);
        Assert.Equal(ChromeStage.Updating, progress.Chrome);
    }

    [Fact]
    public void The_wait_is_bounded_and_conferir_de_novo_opens_the_page_again()
    {
        var progress = AtChrome();
        progress.ChromeLooked(null, Old, T0);

        progress.ChromeTick(T0 + PhoneSetupProgress.ChromeTimeout - TimeSpan.FromSeconds(1));
        Assert.Equal(ChromeStage.Updating, progress.Chrome);
        progress.ChromeTick(T0 + PhoneSetupProgress.ChromeTimeout);
        Assert.Equal(ChromeStage.TimedOut, progress.Chrome);
        Assert.False(progress.WatchesChrome);
        Assert.Null(progress.ChromeLooked(null, Old, T0 + PhoneSetupProgress.ChromeTimeout));

        progress.ChromeAgain();
        Assert.True(progress.WatchesChrome);
        var later = T0 + PhoneSetupProgress.ChromeTimeout + TimeSpan.FromSeconds(3);
        Assert.Equal(PhoneCable.ChromePackage, progress.ChromeLooked(null, Old, later));
        Assert.Equal(TimeSpan.Zero, progress.WaitingFor(later));
    }

    [Fact]
    public void A_store_that_did_not_open_is_said_and_the_wait_goes_on()
    {
        var progress = AtChrome();
        progress.ChromeLooked(null, Old, T0);

        progress.StoreOpened(false);

        Assert.True(progress.StoreFailed);
        Assert.True(progress.WatchesChrome);
        progress.ChromeLooked(null, Updated, T0.AddSeconds(3));
        Assert.Equal("conta-google", progress.StepId);
    }

    [Fact]
    public void Skipping_an_old_chrome_goes_on_and_warns_on_the_ready_page_of_this_phone_only()
    {
        var progress = AtChrome();
        progress.ChromeLooked(null, Old, T0);

        progress.SkipChrome();

        Assert.Equal("conta-google", progress.StepId);
        Assert.True(progress.ChromeSkipped);
        Assert.True(progress.WebViewSkipped);
        while (progress.StepId != "cabo")
        {
            progress.Next();
        }
        progress.CableSeen();
        progress.CablePrepared(T0);
        Assert.Equal(SetupPage.Ready, progress.Page);
        Assert.Contains("bloqueio de downloads não vai funcionar",
            PhoneSetupGuide.ChromeWarning(progress.ChromeSkipped, progress.WebViewSkipped, progress.ChromeSeen));

        // "Preparar o próximo": a new phone, no warning.
        progress.Next();
        Assert.False(progress.ChromeSkipped);
        Assert.False(progress.WebViewSkipped);
        Assert.Null(progress.ChromeSeen);
        Assert.Equal(ChromeStage.Looking, progress.Chrome);
    }

    [Fact]
    public void Skipping_before_any_look_warns_about_chrome()
    {
        var progress = AtChrome();

        progress.SkipChrome();

        Assert.True(progress.ChromeSkipped);
        Assert.Equal("conta-google", progress.StepId);
    }

    [Fact]
    public void Skipping_only_webview_warns_only_about_webview()
    {
        var progress = AtChrome();
        progress.ChromeLooked(null, ChromeOnly, T0);

        progress.SkipChrome();

        Assert.False(progress.ChromeSkipped);
        Assert.True(progress.WebViewSkipped);
        Assert.StartsWith("Atenção: o Android System WebView",
            PhoneSetupGuide.ChromeWarning(progress.ChromeSkipped, progress.WebViewSkipped, progress.ChromeSeen));
    }

    [Fact]
    public void Back_on_a_done_step_shows_it_done_and_next_goes_on()
    {
        var progress = AtChrome();
        progress.ChromeLooked(null, Updated, T0);

        progress.Back();
        Assert.Equal("chrome", progress.StepId);
        Assert.Equal(ChromeStage.Done, progress.Chrome);
        Assert.False(progress.WatchesChrome);

        progress.Next();
        Assert.Equal("conta-google", progress.StepId);
    }

    [Fact]
    public void Looks_off_the_step_change_nothing()
    {
        var progress = AtChrome();
        progress.Back();

        Assert.Null(progress.ChromeLooked(null, Old, T0));
        progress.SkipChrome();

        Assert.Equal("chaves", progress.StepId);
        Assert.Null(progress.ChromeSeen);
        Assert.False(progress.ChromeSkipped);
    }
}
