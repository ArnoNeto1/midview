namespace Midview.Master;

// Where the "chrome" step stands for the phone being prepared.
internal enum ChromeStage
{
    // Looking at the cable for the phone and its versions.
    Looking,
    // The Play Store page of ChromeUpdating is open on the phone: waiting
    // for the teacher's Atualizar and the update.
    Updating,
    // Still not updated ChromeTimeout after the page opened: the cable is
    // no longer looked at until "Conferir de novo".
    TimedOut,
    // Chrome and WebView are up to date.
    Done,
}

// The "chrome" step ("Atualize o Chrome"): right after the developer
// switches, while the phone still has a Google account for the Play Store
// and the cable is allowed, the window reads Chrome's and WebView's versions
// every few seconds (PhoneCable.LookAtChrome) and feeds them here. An old app
// gets its Play Store page opened once (the package ChromeLooked returns),
// and the step moves on by itself when both are up to date, at once for a
// phone that already was. "Pular" goes on all the same, and the ready page
// warns about it for this phone.
internal sealed partial class PhoneSetupProgress
{
    public static readonly TimeSpan ChromeTimeout = TimeSpan.FromMinutes(10);

    private static readonly int ChromeStep = IndexOf("chrome");

    // When the Play Store page of ChromeUpdating was opened.
    private DateTimeOffset? _storeOpenedAt;

    public bool OnChromeStep => Page == SetupPage.Step && StepIndex == ChromeStep;

    public ChromeStage Chrome { get; private set; }

    // The versions read last (null before the first read).
    public ChromeVersions? ChromeSeen { get; private set; }

    // Why the last look found no phone (NoPhone, Unauthorized, TooMany); null when it did.
    public CableProblem? ChromeCable { get; private set; }

    // The app whose Play Store page is open on the phone.
    public string? ChromeUpdating { get; private set; }

    // The phone did not open the Play Store: the step says how to find the app by hand.
    public bool StoreFailed { get; private set; }

    // "Pular" with Chrome, or only WebView, still old (or never read): the
    // ready page warns about this phone.
    public bool ChromeSkipped { get; private set; }

    public bool WebViewSkipped { get; private set; }

    // Whether the window should look at the cable for the step now.
    public bool WatchesChrome => OnChromeStep && Chrome is ChromeStage.Looking or ChromeStage.Updating;

    // How long the Play Store page has been open, for the step's line.
    public TimeSpan WaitingFor(DateTimeOffset now) =>
        _storeOpenedAt is { } opened && Chrome == ChromeStage.Updating ? now - opened : TimeSpan.Zero;

    // A look at the cable on the step: the phone's versions, or why there
    // is none. Returns the app whose Play Store page to open now (null:
    // none, or the one already open). Both up to date: on to the next step.
    public string? ChromeLooked(CableProblem? problem, ChromeVersions? versions, DateTimeOffset now)
    {
        if (!WatchesChrome)
        {
            return null;
        }
        if (problem is not null || versions is null)
        {
            // The phone left (or is not allowed yet): the page opened, if
            // any, stays as it is, and the wait goes on.
            ChromeCable = problem ?? CableProblem.NoPhone;
            return null;
        }
        ChromeCable = null;
        ChromeSeen = versions;
        if (versions.Needed is not { } needed)
        {
            Chrome = ChromeStage.Done;
            ChromeUpdating = null;
            _storeOpenedAt = null;
            ChromeSkipped = false;
            WebViewSkipped = false;
            ShowStepOrMiAccount(ChromeStep + 1);
            return null;
        }
        if (needed == ChromeUpdating)
        {
            return null;
        }
        Chrome = ChromeStage.Updating;
        ChromeUpdating = needed;
        StoreFailed = false;
        _storeOpenedAt = now;
        return needed;
    }

    // What the phone said to opening the Play Store page.
    public void StoreOpened(bool opened)
    {
        if (Chrome == ChromeStage.Updating)
        {
            StoreFailed = !opened;
        }
    }

    // The wait for the update is bounded.
    public void ChromeTick(DateTimeOffset now)
    {
        if (Chrome == ChromeStage.Updating && _storeOpenedAt is { } opened && now - opened >= ChromeTimeout)
        {
            Chrome = ChromeStage.TimedOut;
        }
    }

    // "Abrir a Play Store de novo" or "Conferir de novo": the next look
    // opens the page again (after the Google sign-in, say) and the wait
    // starts over.
    public void ChromeAgain()
    {
        if (!OnChromeStep || Chrome == ChromeStage.Done)
        {
            return;
        }
        Chrome = ChromeStage.Looking;
        ChromeUpdating = null;
        StoreFailed = false;
        _storeOpenedAt = null;
    }

    // "Pular (o bloqueio de downloads não vai funcionar neste celular)".
    public void SkipChrome()
    {
        if (!OnChromeStep)
        {
            return;
        }
        if (Chrome != ChromeStage.Done)
        {
            ChromeSkipped = ChromeSeen?.ChromeOk != true;
            WebViewSkipped = ChromeSeen?.WebViewOk != true;
            Chrome = ChromeStage.Looking;
            ChromeUpdating = null;
            _storeOpenedAt = null;
        }
        ShowStepOrMiAccount(ChromeStep + 1);
    }

    private void ResetChrome()
    {
        Chrome = ChromeStage.Looking;
        ChromeSeen = null;
        ChromeCable = null;
        ChromeUpdating = null;
        StoreFailed = false;
        ChromeSkipped = false;
        WebViewSkipped = false;
        _storeOpenedAt = null;
    }
}
